using System.Text.Json;

namespace CampusPulse.Shared.Mcp.Contracts;

public sealed record McpToolDefinition(
    string Name,
    string Description,
    IReadOnlyDictionary<string, string> InputSchema,
    string Owner = "");

public sealed record McpToolCallResponse(
    string Tool,
    bool IsError,
    JsonElement Result,
    DateTimeOffset ExecutedAtUtc);

internal sealed record McpToolCallRequest(
    string Tool,
    IReadOnlyDictionary<string, JsonElement> Arguments);
