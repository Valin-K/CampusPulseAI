using System.Net;
using CampusPulse.Shared.Ai;
using CampusPulse.Shared.Ai.Gateway;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 2 * 1024 * 1024);
var options = new GeminiOptions
{
    ApiKey = builder.Configuration["GEMINI_API_KEY"] ?? "",
    GenerationModel = builder.Configuration["Gemini:GenerationModel"] ?? "gemini-3.5-flash-lite",
    EmbeddingModel = builder.Configuration["Gemini:EmbeddingModel"] ?? "gemini-embedding-001",
    EmbeddingDimensions = builder.Configuration.GetValue("Gemini:EmbeddingDimensions", 768)
};
if (string.IsNullOrWhiteSpace(options.GenerationModel) || string.IsNullOrWhiteSpace(options.EmbeddingModel)
    || options.EmbeddingDimensions is < 1 or > 3072)
{
    throw new InvalidOperationException("The gateway requires valid Gemini models and embedding dimensions between 1 and 3072.");
}

builder.Services.AddSingleton(options);
builder.Services.AddHttpClient<GeminiService>(client =>
{
    client.BaseAddress = new Uri("https://generativelanguage.googleapis.com/");
    client.Timeout = TimeSpan.FromSeconds(90);
    if (!string.IsNullOrWhiteSpace(options.ApiKey))
    {
        client.DefaultRequestHeaders.Add("x-goog-api-key", options.ApiKey);
    }
});

var app = builder.Build();
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "ai-gateway" }));
app.MapGet("/api/ai/status", (GeminiService gemini) => Results.Ok(gemini.Status));
app.MapPost("/api/ai/generate", async (
    AiGenerationRequest request, GeminiService gemini, ILogger<GeminiService> logger, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Prompt) || request.Context is null
        || (long)request.Prompt.Length + request.Context.Length > 1_000_000)
    {
        return Results.BadRequest(new { error = "A non-empty prompt and context string, totalling at most 1,000,000 characters, are required." });
    }

    return await ExecuteAsync(() => gemini.GenerateAsync(request.Prompt, request.Context, cancellationToken), logger, cancellationToken);
});
app.MapPost("/api/ai/embeddings", async (
    AiEmbeddingRequest request, GeminiService gemini, ILogger<GeminiService> logger, CancellationToken cancellationToken) =>
{
    if (request.Texts is null || request.Texts.Count is < 1 or > 100
        || request.Texts.Any(string.IsNullOrWhiteSpace)
        || request.Texts.Sum(text => (long)text.Length) > 1_000_000)
    {
        return Results.BadRequest(new { error = "Between 1 and 100 non-empty texts, totalling at most 1,000,000 characters, are required." });
    }

    return await ExecuteAsync(() => gemini.EmbedAsync(request.Texts, request.IsQuery, cancellationToken), logger, cancellationToken);
});
app.Run();

static async Task<IResult> ExecuteAsync<T>(
    Func<Task<T>> action, ILogger logger, CancellationToken cancellationToken)
{
    try
    {
        return Results.Ok(await action());
    }
    catch (HttpRequestException exception)
    {
        logger.LogWarning(exception, "The Gemini gateway request failed.");
        var status = exception.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable
            ? StatusCodes.Status503ServiceUnavailable
            : StatusCodes.Status502BadGateway;
        return Results.Problem(statusCode: status, title: "The AI provider is unavailable or returned an invalid response.");
    }
    catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
    {
        logger.LogWarning(exception, "The Gemini gateway request timed out.");
        return Results.Problem(statusCode: StatusCodes.Status504GatewayTimeout, title: "The AI provider request timed out.");
    }
}
