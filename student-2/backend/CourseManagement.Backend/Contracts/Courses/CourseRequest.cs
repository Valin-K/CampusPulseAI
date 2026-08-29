using System.ComponentModel.DataAnnotations;

namespace CourseManagement.Backend.Contracts.Courses;

public sealed class CourseRequest
{
    [Required]
    [StringLength(20)]
    public required string Code { get; set; }

    [Required]
    [StringLength(200)]
    public required string Name { get; set; }

    [Required]
    [StringLength(2000)]
    public required string Description { get; set; }

    [Range(1, 48)]
    public int CreditPoints { get; set; }

    [Range(1, 10)]
    public int Level { get; set; }

    public bool IsActive { get; set; } = true;
}
