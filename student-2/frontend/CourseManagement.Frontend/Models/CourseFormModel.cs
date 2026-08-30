using System.ComponentModel.DataAnnotations;

namespace CourseManagement.Frontend.Models;

public sealed class CourseFormModel
{
    [Required(ErrorMessage = "Course code is required.")]
    [StringLength(20)]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Course name is required.")]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Course description is required.")]
    [StringLength(2000)]
    public string Description { get; set; } = string.Empty;

    [Range(1, 48)]
    public int CreditPoints { get; set; } = 6;

    [Range(1, 10)]
    public int Level { get; set; } = 1;

    public bool IsActive { get; set; } = true;

    public static CourseFormModel FromCourse(CourseDetail course)
    {
        return new CourseFormModel
        {
            Code = course.Code,
            Name = course.Name,
            Description = course.Description,
            CreditPoints = course.CreditPoints,
            Level = course.Level,
            IsActive = course.IsActive
        };
    }
}
