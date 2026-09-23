using System.ComponentModel.DataAnnotations;

namespace EventScheduling.Backend.Models;

public sealed class KnowledgeQuestionRequest
{
    [Required]
    [StringLength(1000, MinimumLength = 1)]
    public required string Question { get; set; }
}

public sealed record KnowledgeAnswerResponse(
    string Question,
    string Status,
    string Reply,
    double Confidence,
    IReadOnlyList<KnowledgeSource> Sources,
    string Embedder);

public sealed record KnowledgeSource(int Number, string Label, string DocumentId, double Score);
