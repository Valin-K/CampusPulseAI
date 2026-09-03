using System.ComponentModel.DataAnnotations;

namespace CourseManagement.Backend.Contracts.Prerequisites;

public sealed class PrerequisiteRequest
{
    [Range(1, int.MaxValue)]
    public int CourseId { get; set; }

    [Range(1, int.MaxValue)]
    public int RequiredCourseId { get; set; }

    [StringLength(20)]
    public string? MinimumGrade { get; set; }
}

public sealed record PrerequisiteResponse(
    int Id,
    int CourseId,
    string CourseCode,
    int RequiredCourseId,
    string RequiredCourseCode,
    string RequiredCourseName,
    string? MinimumGrade);
