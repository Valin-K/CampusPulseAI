using System.ComponentModel.DataAnnotations;

namespace CourseManagement.Backend.Contracts.Ai;

public sealed class CourseQuestionRequest
{
    [Required]
    [StringLength(1000, MinimumLength = 3)]
    public required string Question { get; set; }

    [MaxLength(50)]
    public IReadOnlyList<CourseQuestionContext> Courses { get; set; } = [];

    public bool CoursesExplicitlySelected { get; set; }

    [StringLength(20)]
    public string? CourseCode { get; set; }

    [StringLength(200)]
    public string? CourseName { get; set; }
}

public sealed class CourseQuestionContext
{
    [Required]
    [StringLength(20)]
    public required string Code { get; set; }

    [Required]
    [StringLength(200)]
    public required string Name { get; set; }

    [Required]
    [StringLength(1000)]
    public required string Description { get; set; }

    [Range(0, 20)]
    public int Level { get; set; }

    [Range(0, 1000)]
    public int CreditPoints { get; set; }
}

public sealed record CourseQuestionResponse(
    string Answer,
    string Model,
    bool IsSyllabusGrounded);
