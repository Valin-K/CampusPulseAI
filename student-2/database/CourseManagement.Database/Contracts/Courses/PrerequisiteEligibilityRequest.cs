using System.ComponentModel.DataAnnotations;

namespace CourseManagement.Backend.Contracts.Courses;

public sealed class PrerequisiteEligibilityRequest
{
    [Required]
    public required IReadOnlyList<CompletedCourseRequest> CompletedCourses { get; set; }
}

public sealed class CompletedCourseRequest
{
    [Required]
    [StringLength(20)]
    public required string CourseCode { get; set; }

    [Required]
    [StringLength(20)]
    public required string Grade { get; set; }
}

public sealed record PrerequisiteEligibilityResponse(
    int CourseId,
    string CourseCode,
    bool IsEligible,
    IReadOnlyList<PrerequisiteRequirementResponse> Requirements);

public sealed record PrerequisiteRequirementResponse(
    int RequiredCourseId,
    string RequiredCourseCode,
    string MinimumGrade,
    string? AchievedGrade,
    bool IsMet,
    string Reason);
