namespace EventScheduling.Frontend.Models;

public sealed record McpIntegrationResponse(string Tool, bool IsError, McpIntegrationResult Result, DateTimeOffset ExecutedAtUtc);

public sealed record McpIntegrationResult(string Feature, bool Valid, IReadOnlyList<string> Checks);

public sealed record McpVerifyResult(bool Success, McpIntegrationResponse? Response, string? Error);
