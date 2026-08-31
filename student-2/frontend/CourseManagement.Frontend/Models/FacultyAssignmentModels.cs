using System.ComponentModel.DataAnnotations;

namespace CourseManagement.Frontend.Models;

public sealed record FacultyAssignmentSummary(
    int Id,
    int CourseId,
    string CourseCode,
    string StaffName,
    string StaffEmail,
    string Role,
    string TeachingPeriod);

public sealed class FacultyAssignmentFormModel
{
    public int CourseId { get; set; }

    [Required(ErrorMessage = "Staff name is required.")]
    [StringLength(200)]
    public string StaffName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Staff email is required.")]
    [EmailAddress(ErrorMessage = "Enter a valid staff email address.")]
    [StringLength(320)]
    public string StaffEmail { get; set; } = string.Empty;

    [Required(ErrorMessage = "Role is required.")]
    [StringLength(100)]
    public string Role { get; set; } = string.Empty;

    [Required(ErrorMessage = "Teaching period is required.")]
    [StringLength(50)]
    public string TeachingPeriod { get; set; } = string.Empty;

    public static FacultyAssignmentFormModel FromAssignment(
        FacultyAssignmentSummary assignment)
    {
        return new FacultyAssignmentFormModel
        {
            CourseId = assignment.CourseId,
            StaffName = assignment.StaffName,
            StaffEmail = assignment.StaffEmail,
            Role = assignment.Role,
            TeachingPeriod = assignment.TeachingPeriod
        };
    }
}
