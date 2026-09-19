using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using RagServer.Models;

namespace RagServer.Services;

public sealed class GeminiClient(IHttpClientFactory clients, IConfiguration configuration, IOptions<RagOptions> options)
{
    private const int BatchLimit = 100;
    private const string FallbackEmbedder = "hashed-bag-of-words";

    public bool HasApiKey => !string.IsNullOrWhiteSpace(
        configuration["GEMINI_API_KEY"] ?? configuration["Gemini:ApiKey"]);

    public string EmbedderName => HasApiKey ? options.Value.EmbeddingModel : FallbackEmbedder;

    public async Task<List<float[]>> EmbedAsync(IReadOnlyList<string> texts, bool isQuery, CancellationToken cancellationToken)
    {
        if (!HasApiKey)
        {
            return texts.Select(HashedEmbedding).ToList();
        }

        var model = options.Value.EmbeddingModel;
        var taskType = isQuery ? "RETRIEVAL_QUERY" : "RETRIEVAL_DOCUMENT";
        var vectors = new List<float[]>(texts.Count);
        foreach (var batch in texts.Chunk(BatchLimit))
        {
            var request = new BatchEmbedRequest(batch.Select(text => new EmbedRequest(
                $"models/{model}",
                new Content([new Part(text)]),
                taskType,
                options.Value.EmbeddingDimensions)).ToArray());

            using var response = await clients.CreateClient("gemini")
                .PostAsJsonAsync($"v1beta/models/{model}:batchEmbedContents", request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<BatchEmbedResponse>(cancellationToken: cancellationToken);
            var embeddings = result?.Embeddings ?? [];
            if (embeddings.Length != batch.Length)
            {
                throw new InvalidOperationException("Gemini returned a different number of embeddings than requested.");
            }

            vectors.AddRange(embeddings.Select(embedding => embedding.Values ?? []));
        }

        return vectors;
    }

    public async Task<string?> GenerateAsync(string prompt, CancellationToken cancellationToken)
    {
        var model = options.Value.GenerationModel;
        var request = new GenerateRequest([new Content([new Part(prompt)])]);
        using var response = await clients.CreateClient("gemini")
            .PostAsJsonAsync($"v1beta/models/{model}:generateContent", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<GenerateResponse>(cancellationToken: cancellationToken);
        var text = result?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

	// shows hashed embedding on the client side when GEMINI_API_KEY is not configured
	private float[] HashedEmbedding(string text)
    {
        var vector = new float[options.Value.EmbeddingDimensions];
        foreach (var token in Tokenise(text))
        {
            var hash = Fnv1a(token);
            var index = (int)(hash % (uint)vector.Length);
            var sign = (hash & 0x80000000u) == 0 ? 1f : -1f;
            vector[index] += sign;
        }

        return vector;
    }

    private static IEnumerable<string> Tokenise(string text)
    {
        var start = -1;
        for (var index = 0; index <= text.Length; index++)
        {
            var isWordCharacter = index < text.Length && char.IsLetterOrDigit(text[index]);
            if (isWordCharacter && start < 0)
            {
                start = index;
            }
            else if (!isWordCharacter && start >= 0)
            {
                yield return text[start..index].ToLowerInvariant();
                start = -1;
            }
        }
    }

    private static uint Fnv1a(string value)
    {
        var hash = 2166136261u;
        foreach (var character in value)
        {
            hash ^= character;
            hash *= 16777619u;
        }

        return hash;
    }

    private sealed record Part(string Text);
    private sealed record Content(Part[] Parts);
    private sealed record EmbedRequest(string Model, Content Content, string TaskType, int OutputDimensionality);
    private sealed record BatchEmbedRequest(EmbedRequest[] Requests);
    private sealed record Embedding([property: JsonPropertyName("values")] float[]? Values);
    private sealed record BatchEmbedResponse([property: JsonPropertyName("embeddings")] Embedding[]? Embeddings);
    private sealed record GenerateRequest(Content[] Contents);
    private sealed record ResponsePart([property: JsonPropertyName("text")] string? Text);
    private sealed record ResponseContent([property: JsonPropertyName("parts")] ResponsePart[]? Parts);
    private sealed record Candidate([property: JsonPropertyName("content")] ResponseContent? Content);
    private sealed record GenerateResponse([property: JsonPropertyName("candidates")] Candidate[]? Candidates);
}
