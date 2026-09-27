namespace RagServer.Models;

public sealed class RagOptions
{
    public int ChunkSize { get; set; } = 800;
    public int ChunkOverlap { get; set; } = 100;
    public int DefaultTopK { get; set; } = 4;
    public double MinimumConfidence { get; set; } = 0.55;
    public int EmbeddingDimensions { get; set; } = 768;
    public string EmbeddingModel { get; set; } = "gemini-embedding-001";
    public string GenerationModel { get; set; } = "gemini-3.5-flash-lite";
    public string[] AllowedCollections { get; set; } = [];
}

public sealed record RagDocument(string Id, string Text, Dictionary<string, string>? Metadata);

public sealed record IngestRequest(List<RagDocument> Documents);

public sealed record IngestResponse(string Collection, int Documents, int Chunks, string Embedder);

public sealed record AnswerRequest(
    string Question,
    int? TopK,
    string? Instructions,
    double? MinimumConfidence,
    string? SupplementalContext = null);

public sealed record RagSource(
    int Rank,
    string DocumentId,
    int ChunkIndex,
    double Score,
    string Text,
    Dictionary<string, string>? Metadata);

public sealed record AnswerResponse(
    string Collection,
    string Question,
    string Status,
    string? Answer,
    double Confidence,
    IReadOnlyList<RagSource> Sources,
    string Embedder,
    string? Detail,
    DateTimeOffset ExecutedAtUtc);

public sealed record CollectionSummary(string Name, int Documents, int Chunks);

public sealed record StoredChunk(
    string DocumentId,
    int ChunkIndex,
    string Text,
    Dictionary<string, string>? Metadata,
    float[] Vector);
