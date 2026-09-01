using System.ComponentModel.DataAnnotations;

namespace CourseManagement.Backend.Contracts.Courses;

public sealed class CourseGradeTypeRequest
{
    [Required(ErrorMessage = "Course grade type is required.")]
    [StringLength(20)]
    public required string GradeType { get; set; }
}
