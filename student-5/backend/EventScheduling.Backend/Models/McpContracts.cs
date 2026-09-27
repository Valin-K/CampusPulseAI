namespace EventScheduling.Backend.Models;

public sealed record McpToolCallRequest(string Tool, Dictionary<string, object?> Arguments);

public sealed record McpIntegrationResponse(string Tool, bool IsError, McpIntegrationResult Result, DateTimeOffset ExecutedAtUtc);

public sealed record McpIntegrationResult(string Feature, bool Valid, IReadOnlyList<string> Checks);
