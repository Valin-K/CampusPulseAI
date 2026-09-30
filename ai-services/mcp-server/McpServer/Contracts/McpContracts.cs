using System.Text.Json.Serialization;

namespace McpServer.Contracts;

public sealed record McpCallRequest(string Tool, Dictionary<string, object?> Arguments)
{
    [JsonIgnore]
    internal IReadOnlyList<McpToolDescriptor> RegisteredTools { get; init; } = [];
}

internal sealed record McpToolDescriptor(
    string Name,
    string Owner,
    string Description,
    IReadOnlyDictionary<string, string> InputSchema);

public sealed record McpToolResult(
    string Tool,
    bool IsError,
    object Result,
    DateTimeOffset ExecutedAtUtc);

public sealed record McpToolExecution(
    bool IsError,
    object Result);
