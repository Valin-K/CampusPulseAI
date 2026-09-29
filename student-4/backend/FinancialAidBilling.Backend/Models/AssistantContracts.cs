namespace FinancialAidBilling.Backend.Models;

public sealed record McpToolCallRequest(string Tool, Dictionary<string, object?> Arguments);

public sealed record RagQuestionRequest(string Question);
