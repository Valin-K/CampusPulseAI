using CampusPulse.Shared.Ai;

namespace RagServer.Services;

public sealed class RagAiClient(IAiGatewayClient gateway)
{
    private const string FallbackEmbedder = "hashed-bag-of-words";
    private readonly SemaphoreSlim _statusLock = new(1, 1);
    private volatile AiGatewayStatus? _status;

    public string EmbedderName => _status is { } status
        ? status.GenerationAvailable ? status.EmbeddingModel : FallbackEmbedder
        : "uninitialized";

    public bool? GenerationAvailable => _status?.GenerationAvailable;

    public async Task<List<float[]>> EmbedAsync(
        IReadOnlyList<string> texts, bool isQuery, CancellationToken cancellationToken)
    {
        var status = await GetPinnedStatusAsync(cancellationToken);
        if (!status.GenerationAvailable)
        {
            return texts.Select(text => HashedEmbedding(text, status.EmbeddingDimensions)).ToList();
        }

        var result = await gateway.EmbedAsync(texts, isQuery, cancellationToken);
        if (!string.Equals(result.Model, status.EmbeddingModel, StringComparison.Ordinal)
            || result.Embeddings.Any(vector => vector.Length != status.EmbeddingDimensions))
        {
            throw RestartRequired();
        }

        if (result.Embeddings.Length != texts.Count
            || result.Embeddings.Any(vector => vector.Any(value => !float.IsFinite(value))))
        {
            throw new InvalidOperationException("The AI gateway returned invalid embeddings.");
        }

        return result.Embeddings.ToList();
    }

    public async Task<string?> GenerateAsync(
        string prompt, string context, CancellationToken cancellationToken)
    {
        var status = await GetPinnedStatusAsync(cancellationToken);
        var result = await gateway.GenerateAsync(prompt, context, cancellationToken);
        if (!string.Equals(result.Model, status.GenerationModel, StringComparison.Ordinal))
        {
            throw RestartRequired();
        }

        return string.IsNullOrWhiteSpace(result.Text) ? null : result.Text.Trim();
    }

    private async Task<AiGatewayStatus> GetPinnedStatusAsync(CancellationToken cancellationToken)
    {
        await _statusLock.WaitAsync(cancellationToken);
        try
        {
            // Failed requests never initialize the snapshot or enable local fallback.
            var current = await gateway.GetStatusAsync(cancellationToken);
            if (current.EmbeddingDimensions <= 0
                || string.IsNullOrWhiteSpace(current.EmbeddingModel)
                || string.IsNullOrWhiteSpace(current.GenerationModel))
            {
                throw new InvalidOperationException("The AI gateway returned an invalid configuration.");
            }

            if (_status is not null && _status != current)
            {
                throw RestartRequired();
            }

            return _status ??= current;
        }
        finally
        {
            _statusLock.Release();
        }
    }

    private static InvalidOperationException RestartRequired() =>
        new("AI gateway configuration changed. Restart the RAG server and re-ingest documents before continuing.");

    private static float[] HashedEmbedding(string text, int dimensions)
    {
        var vector = new float[dimensions];
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
}
