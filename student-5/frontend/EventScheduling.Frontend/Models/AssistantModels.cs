namespace EventScheduling.Frontend.Models;

public sealed record AssistantMessage(string Role, string Content);

public sealed record AssistantChatRequest(int UserId, IReadOnlyList<AssistantMessage> Messages);

public sealed record AssistantChatResponse(
    string Reply,
    ProposedBooking? Proposal,
    string Model);

public sealed record ProposedBooking(
    ResourceType Kind,
    int ResourceId,
    string ResourceName,
    DateTime StartUtc,
    DateTime EndUtc,
    string Rationale);

public sealed record AssistantResult(bool Success, AssistantChatResponse? Response, string? Error);
