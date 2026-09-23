namespace CampusPulse.Shared.Rag.Assistant;

public sealed record RagKnowledgeSource(int Number, string Label, string DocumentId, double Score);

public sealed record RagAssistantAnswer(
    string Question,
    string Status,
    string Reply,
    double Confidence,
    IReadOnlyList<RagKnowledgeSource> Sources,
    string Embedder);
