using System.ComponentModel.DataAnnotations;

namespace CourseManagement.Backend.Contracts.Ai;

public sealed class KnowledgeQuestionRequest
{
    [Required]
    [StringLength(1000, MinimumLength = 1)]
    public required string Question { get; set; }
}
