using System.ComponentModel.DataAnnotations;

namespace CourseManagement.Backend.Contracts.FacultyAssignments;

public sealed class FacultyAssignmentRequest
{
    [Range(1, int.MaxValue)]
    public int CourseId { get; set; }

    [Required]
    [StringLength(200)]
    public required string StaffName { get; set; }

    [Required]
    [EmailAddress]
    [StringLength(320)]
    public required string StaffEmail { get; set; }

    [Required]
    [StringLength(100)]
    public required string Role { get; set; }

    [Required]
    [StringLength(50)]
    public required string TeachingPeriod { get; set; }
}

public sealed record FacultyAssignmentResponse(
    int Id,
    int CourseId,
    string CourseCode,
    string StaffName,
    string StaffEmail,
    string Role,
    string TeachingPeriod);
