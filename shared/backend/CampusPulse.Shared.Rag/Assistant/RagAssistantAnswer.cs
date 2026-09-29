using CampusPulse.Shared.Rag.Contracts;

namespace CampusPulse.Shared.Rag.Assistant;

public sealed record RagKnowledgeSource(int Number, string Label, string DocumentId, double Score);

public sealed record RagAssistantAnswer(
    string Question,
    string Status,
    string Reply,
    double Confidence,
    string ConfidenceCategory,
    IReadOnlyList<RagKnowledgeSource> Sources,
    string Embedder);

// A coarse, user-facing confidence band derived from the retrieval score. Answers that were not
// grounded in retrieved context (insufficient / generation unavailable / failed) are always
// "Insufficient" so the UI never shows a confidence level for an unsupported answer.
public static class RagConfidenceCategory
{
    public const string High = "High";
    public const string Medium = "Medium";
    public const string Low = "Low";
    public const string Insufficient = "Insufficient";

    public static string From(string status, double confidence) =>
        status != RagStatus.Answered
            ? Insufficient
            : confidence switch
            {
                >= 0.75 => High,
                >= 0.65 => Medium,
                _ => Low,
            };
}
