namespace FinancialAidBilling.Backend.Models;

public sealed record McpToolCallRequest(string Tool, Dictionary<string, object?> Arguments);

public sealed record RagQuestionRequest(string Question);

public sealed record RagDocument(string Id, string Text, Dictionary<string, string> Metadata);

public sealed record RagIngestRequest(IReadOnlyList<RagDocument> Documents);

public sealed record RagAnswerRequest(string Question, int? TopK, string Instructions, double? MinimumConfidence);
