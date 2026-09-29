using System.Net.Http.Json;
using System.Text.Json;

namespace CampusPulse.Shared.Ai;

public sealed class AiGatewayClient(HttpClient httpClient) : IAiGatewayClient
{
    public async Task<AiGenerationResponse> GenerateAsync(
        string prompt, string context, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        ArgumentNullException.ThrowIfNull(context);
        using var response = await httpClient.PostAsJsonAsync(
            "api/ai/generate", new AiGenerationRequest(prompt, context), cancellationToken);
        var result = await ReadAsync<AiGenerationResponse>(response, cancellationToken);
        if (string.IsNullOrWhiteSpace(result.Text) || string.IsNullOrWhiteSpace(result.Model))
        {
            throw new HttpRequestException("The AI gateway returned an empty answer or model.");
        }

        return result;
    }

    public async Task<AiEmbeddingResponse> EmbedAsync(
        IReadOnlyList<string> texts, bool isQuery, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(texts);
        if (texts.Count == 0 || texts.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("At least one non-empty text is required.", nameof(texts));
        }

        var vectors = new List<float[]>(texts.Count);
        string? model = null;
        foreach (var batch in texts.Chunk(100))
        {
            using var response = await httpClient.PostAsJsonAsync(
                "api/ai/embeddings", new AiEmbeddingRequest(batch, isQuery), cancellationToken);
            var result = await ReadAsync<AiEmbeddingResponse>(response, cancellationToken);
            if (string.IsNullOrWhiteSpace(result.Model)
                || (model is not null && model != result.Model)
                || result.Embeddings is null
                || result.Embeddings.Length != batch.Length
                || result.Embeddings.Any(vector => vector is null || vector.Length == 0 || vector.Any(value => !float.IsFinite(value))))
            {
                throw new HttpRequestException("The AI gateway returned invalid embeddings.");
            }

            model = result.Model;
            vectors.AddRange(result.Embeddings);
        }

        if (vectors.Any(vector => vector.Length != vectors[0].Length))
        {
            throw new HttpRequestException("The AI gateway returned inconsistent embedding dimensions.");
        }

        return new AiEmbeddingResponse(vectors.ToArray(), model!);
    }

    public async Task<AiGatewayStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync("api/ai/status", cancellationToken);
        var result = await ReadAsync<AiGatewayStatus>(response, cancellationToken);
        if (string.IsNullOrWhiteSpace(result.GenerationModel)
            || string.IsNullOrWhiteSpace(result.EmbeddingModel)
            || result.EmbeddingDimensions <= 0)
        {
            throw new HttpRequestException("The AI gateway returned an invalid status.");
        }

        return result;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"The AI gateway returned HTTP {(int)response.StatusCode}.", null, response.StatusCode);
        }

        try
        {
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
                ?? throw new HttpRequestException("The AI gateway returned an empty response.");
        }
        catch (JsonException exception)
        {
            throw new HttpRequestException("The AI gateway returned invalid JSON.", exception);
        }
        catch (NotSupportedException exception)
        {
            throw new HttpRequestException("The AI gateway returned an unsupported response.", exception);
        }
    }
}
