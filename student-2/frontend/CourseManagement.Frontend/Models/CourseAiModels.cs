namespace CourseManagement.Frontend.Models;

public sealed record KnowledgeQuestionRequest(string Question);

public sealed record KnowledgeAnswer(
    string Reply,
    IReadOnlyList<KnowledgeSource>? Sources,
    string? ConfidenceCategory);

public sealed record KnowledgeSource(
    string Label,
    string DocumentId);
