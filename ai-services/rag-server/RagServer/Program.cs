using CampusPulse.Shared.Ai;
using CampusPulse.Shared.Rag.Generation;
using Microsoft.Extensions.Options;
using RagServer.Models;
using RagServer.Services;
using SharedRagSource = CampusPulse.Shared.Rag.Contracts.RagSource;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
builder.Services.Configure<RagOptions>(builder.Configuration.GetSection("Rag"));

builder.Services.AddAiGateway(builder.Configuration);
builder.Services.AddSingleton<VectorStore>();
builder.Services.AddSingleton<TextChunker>();
builder.Services.AddSingleton<RagAiClient>();
builder.Services.AddSingleton<RagPromptBuilder>();

var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors();

app.MapGet("/health", (VectorStore store, RagAiClient ai, IOptions<RagOptions> options) => Results.Ok(new
{
    status = "healthy",
    service = "rag",
    embedder = ai.EmbedderName,
    generationAvailable = ai.GenerationAvailable,
    collections = store.Summaries().Count,
    allowedCollections = options.Value.AllowedCollections
}));

app.MapGet("/api/rag/collections", (VectorStore store) => Results.Ok(store.Summaries()));

app.MapPost("/api/rag/{collection}/documents", async (
    string collection,
    IngestRequest request,
    VectorStore store,
    TextChunker chunker,
    RagAiClient ai,
    IOptions<RagOptions> options,
    CancellationToken cancellationToken) =>
{
    if (!IsAllowedCollection(collection, options.Value))
    {
        return UnknownCollection(collection, options.Value);
    }

    var documents = (request.Documents ?? [])
        .Where(document => !string.IsNullOrWhiteSpace(document.Id) && !string.IsNullOrWhiteSpace(document.Text))
        .ToList();
    if (documents.Count == 0)
    {
        return Results.BadRequest(new { error = "At least one document with a non-empty id and text is required." });
    }

    var pending = documents
        .SelectMany(document => chunker.Split(document.Text)
            .Select((text, index) => (document, index, text)))
        .ToList();

    List<float[]> vectors;
    try
    {
        vectors = await ai.EmbedAsync(pending.Select(item => item.text).ToList(), isQuery: false, cancellationToken);
    }
    catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or TaskCanceledException)
    {
        return Results.Json(new { error = "Embedding failed.", detail = exception.Message }, statusCode: StatusCodes.Status502BadGateway);
    }

    foreach (var group in pending.Select((item, position) => (item, vector: vectors[position])).GroupBy(entry => entry.item.document.Id))
    {
        var document = group.First().item.document;
        store.Upsert(collection, document.Id, group.Select(entry =>
            new StoredChunk(document.Id, entry.item.index, entry.item.text, document.Metadata, entry.vector)));
    }

    return Results.Ok(new IngestResponse(collection, documents.Count, pending.Count, ai.EmbedderName));
});

app.MapDelete("/api/rag/{collection}", (string collection, VectorStore store, IOptions<RagOptions> options) =>
    IsAllowedCollection(collection, options.Value)
        ? Results.Ok(new { collection, deleted = store.Delete(collection) })
        : UnknownCollection(collection, options.Value));

app.MapPost("/api/rag/{collection}/answer", async (
    string collection,
    AnswerRequest request,
    VectorStore store,
    RagAiClient ai,
    RagPromptBuilder promptBuilder,
    IOptions<RagOptions> options,
    CancellationToken cancellationToken) =>
{
    if (!IsAllowedCollection(collection, options.Value))
    {
        return UnknownCollection(collection, options.Value);
    }

    if (string.IsNullOrWhiteSpace(request.Question))
    {
        return Results.BadRequest(new { error = "A question is required." });
    }

    var topK = Math.Clamp(request.TopK ?? options.Value.DefaultTopK, 1, 20);
    var threshold = request.MinimumConfidence ?? options.Value.MinimumConfidence;

    float[] queryVector;
    try
    {
        queryVector = (await ai.EmbedAsync([request.Question], isQuery: true, cancellationToken))[0];
    }
    catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or TaskCanceledException)
    {
        return Results.Json(new { error = "Embedding failed.", detail = exception.Message }, statusCode: StatusCodes.Status502BadGateway);
    }

    var sources = store.Search(collection, queryVector, topK)
        .Select((hit, index) => new RagSource(
            index + 1, hit.Chunk.DocumentId, hit.Chunk.ChunkIndex, Math.Round(hit.Score, 4), hit.Chunk.Text, hit.Chunk.Metadata))
        .ToList();
    var confidence = sources.Count == 0 ? 0 : sources[0].Score;

    AnswerResponse Respond(string status, string? answer, string? detail = null) =>
        new(collection, request.Question, status, answer, confidence, sources, ai.EmbedderName, detail, DateTimeOffset.UtcNow);

    if (sources.Count == 0 || confidence < threshold)
    {
        if (string.IsNullOrWhiteSpace(request.SupplementalContext))
        {
            return Results.Ok(Respond(
                "insufficient_context",
                "The knowledge base does not contain enough information to answer this question.",
                sources.Count == 0 ? "No documents matched." : $"Top score {confidence:0.####} is below the threshold {threshold:0.####}."));
        }

        sources = [];
    }

    if (ai.GenerationAvailable == false)
    {
        return Results.Ok(Respond("generation_unavailable", null, "The AI gateway reports generation unavailable; returning retrieved context only."));
    }

    try
    {
        var promptSources = sources
            .Select(source => new SharedRagSource(
                source.Rank, source.DocumentId, source.ChunkIndex, source.Score, source.Text, source.Metadata))
            .ToList();
        var prompt = promptBuilder.BuildParts(new RagPromptRequest
        {
            Question = request.Question,
            Sources = promptSources,
            Persona = request.Instructions,
            SupplementalContext = request.SupplementalContext,
        });
        var answer = await ai.GenerateAsync(prompt.Prompt, prompt.Context, cancellationToken);
        return Results.Ok(answer is null
            ? Respond("generation_failed", null, "The AI gateway returned an empty response.")
            : Respond("answered", answer));
    }
    catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or TaskCanceledException)
    {
        return Results.Ok(Respond("generation_failed", null, exception.Message));
    }
});

app.Run();

static bool IsAllowedCollection(string collection, RagOptions options) =>
    options.AllowedCollections.Length == 0
    || options.AllowedCollections.Contains(collection, StringComparer.OrdinalIgnoreCase);

static IResult UnknownCollection(string collection, RagOptions options) => Results.NotFound(new
{
    error = $"Collection '{collection}' is not registered.",
    allowedCollections = options.AllowedCollections
});
