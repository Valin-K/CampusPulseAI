using System.ComponentModel.DataAnnotations;

namespace CampusHousing.Backend.Models;

public sealed class KnowledgeQuestionRequest
{
    [Required]
    [StringLength(1000, MinimumLength = 1)]
    public required string Question { get; set; }
}
