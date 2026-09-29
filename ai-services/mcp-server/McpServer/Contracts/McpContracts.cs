namespace McpServer.Contracts;

public sealed record McpCallRequest(
    string Tool,
    Dictionary<string, object?> Arguments);

public sealed record McpToolResult(
    string Tool,
    bool IsError,
    object Result,
    DateTimeOffset ExecutedAtUtc);

public sealed record McpToolExecution(
    bool IsError,
    object Result);
