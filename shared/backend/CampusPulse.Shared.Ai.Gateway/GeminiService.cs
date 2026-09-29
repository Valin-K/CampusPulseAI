using System.Net;
using System.Text.Json;

namespace CampusPulse.Shared.Ai.Gateway;

public sealed class GeminiService(HttpClient httpClient, GeminiOptions options)
{
    public AiGatewayStatus Status => new(
        !string.IsNullOrWhiteSpace(options.ApiKey),
        options.GenerationModel, options.EmbeddingModel, options.EmbeddingDimensions);

    public async Task<AiGenerationResponse> GenerateAsync(
        string prompt, string context, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var request = new
        {
            systemInstruction = new Content([new Part(prompt)]),
            contents = new[] { new Content([new Part(string.IsNullOrWhiteSpace(context) ? "Perform the requested task." : context)]) }
        };
        using var response = await httpClient.PostAsJsonAsync(
            $"v1beta/models/{Uri.EscapeDataString(options.GenerationModel)}:generateContent", request, cancellationToken);
        var result = await ReadAsync<GenerateResponse>(response, cancellationToken);
        var candidate = result.Candidates?.FirstOrDefault();
        if (candidate?.FinishReason is not (null or "STOP"))
        {
            throw new HttpRequestException("Gemini did not complete the answer.");
        }

        if (candidate?.Content?.Parts?.Any(part => part is null) == true)
        {
            throw new HttpRequestException("Gemini returned an invalid answer part.");
        }

        var text = string.Concat(candidate?.Content?.Parts?
            .Where(part => !part.Thought).Select(part => part.Text) ?? []).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new HttpRequestException("Gemini returned an empty or blocked answer.");
        }

        return new AiGenerationResponse(text, options.GenerationModel);
    }

    public async Task<AiEmbeddingResponse> EmbedAsync(
        IReadOnlyList<string> texts, bool isQuery, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var request = new
        {
            requests = texts.Select(text => new
            {
                model = $"models/{options.EmbeddingModel}",
                content = new Content([new Part(text)]),
                taskType = isQuery ? "RETRIEVAL_QUERY" : "RETRIEVAL_DOCUMENT",
                outputDimensionality = options.EmbeddingDimensions
            }).ToArray()
        };
        using var response = await httpClient.PostAsJsonAsync(
            $"v1beta/models/{Uri.EscapeDataString(options.EmbeddingModel)}:batchEmbedContents", request, cancellationToken);
        var result = await ReadAsync<EmbedResponse>(response, cancellationToken);
        if (result.Embeddings is null || result.Embeddings.Length != texts.Count
            || result.Embeddings.Any(embedding => embedding?.Values is null
                || embedding.Values.Length != options.EmbeddingDimensions
                || embedding.Values.Any(value => !float.IsFinite(value))))
        {
            throw new HttpRequestException("Gemini returned invalid embeddings.");
        }

        return new AiEmbeddingResponse(result.Embeddings.Select(embedding => embedding.Values!).ToArray(), options.EmbeddingModel);
    }

    private void EnsureConfigured()
    {
        if (!Status.GenerationAvailable)
        {
            throw new HttpRequestException("Gemini is not configured on the AI gateway.", null, HttpStatusCode.ServiceUnavailable);
        }
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Gemini returned HTTP {(int)response.StatusCode}.", null, response.StatusCode);
        }

        try
        {
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
                ?? throw new HttpRequestException("Gemini returned an empty response.");
        }
        catch (JsonException exception)
        {
            throw new HttpRequestException("Gemini returned invalid JSON.", exception);
        }
        catch (NotSupportedException exception)
        {
            throw new HttpRequestException("Gemini returned an unsupported response.", exception);
        }
    }

    private sealed record Part(string? Text, bool Thought = false);
    private sealed record Content(Part[]? Parts);
    private sealed record Candidate(Content? Content, string? FinishReason);
    private sealed record GenerateResponse(Candidate[]? Candidates);
    private sealed record Embedding(float[]? Values);
    private sealed record EmbedResponse(Embedding[]? Embeddings);
}
