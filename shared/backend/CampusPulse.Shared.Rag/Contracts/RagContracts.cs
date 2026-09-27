namespace CampusPulse.Shared.Rag.Contracts;

public sealed record RagSource(
    int Rank,
    string DocumentId,
    int ChunkIndex,
    double Score,
    string Text,
    IReadOnlyDictionary<string, string>? Metadata);

public sealed record RagDocument(
    string Id,
    string Text,
    IReadOnlyDictionary<string, string>? Metadata);

public sealed record RagIngestRequest(IReadOnlyList<RagDocument> Documents);

public sealed record RagIngestResponse(string Collection, int Documents, int Chunks, string Embedder);

public sealed record RagAnswerRequest(
    string Question,
    int? TopK = null,
    string? Instructions = null,
    double? MinimumConfidence = null,
    string? SupplementalContext = null);

public static class RagStatus
{
    public const string Answered = "answered";
    public const string InsufficientContext = "insufficient_context";
    public const string GenerationUnavailable = "generation_unavailable";
    public const string GenerationFailed = "generation_failed";
}

public sealed record RagAnswerResponse(
    string Collection,
    string Question,
    string Status,
    string? Answer,
    double Confidence,
    IReadOnlyList<RagSource> Sources,
    string Embedder,
    string? Detail,
    DateTimeOffset ExecutedAtUtc);
