using System.ComponentModel.DataAnnotations;

namespace EventScheduling.Backend.Models;

public sealed class AssistantChatRequest
{
    [Range(1, int.MaxValue)]
    public int UserId { get; set; }

    [Required]
    [MinLength(1)]
    [MaxLength(20)]
    public IReadOnlyList<AssistantMessage> Messages { get; set; } = [];
}

public sealed class AssistantMessage
{
    // "user" or "assistant".
    [Required]
    [RegularExpression("user|assistant")]
    public required string Role { get; set; }

    [Required]
    [StringLength(2000, MinimumLength = 1)]
    public required string Content { get; set; }
}

public sealed record AssistantChatResponse(
    string Reply,
    ProposedBooking? Proposal,
    string Model);

public sealed record ProposedBooking(
    AssistantResourceKind Kind,
    int ResourceId,
    string ResourceName,
    DateTime StartUtc,
    DateTime EndUtc,
    string Rationale);

public enum AssistantResourceKind
{
    Room,
    Equipment
}

public sealed record GeminiGenerateRequest(IReadOnlyList<GeminiContent> Contents);

public sealed record GeminiContent(IReadOnlyList<GeminiPart> Parts);

public sealed record GeminiPart(string Text);

public sealed record GeminiGenerateResponse(IReadOnlyList<GeminiCandidate>? Candidates);

public sealed record GeminiCandidate(GeminiContent? Content);
