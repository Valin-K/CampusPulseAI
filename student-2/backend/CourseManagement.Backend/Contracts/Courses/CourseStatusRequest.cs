using System.ComponentModel.DataAnnotations;

namespace CourseManagement.Backend.Contracts.Courses;

public sealed class CourseStatusRequest
{
    [Required(ErrorMessage = "Course status is required.")]
    public bool? IsActive { get; set; }
}
