using System.ComponentModel.DataAnnotations;

namespace CourseManagement.Frontend.Models;

public sealed class PrerequisiteFormModel
{
    public int CourseId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Select a prerequisite Course.")]
    public int RequiredCourseId { get; set; }

    [Required(ErrorMessage = "Minimum grade is required.")]
    public string MinimumGrade { get; set; } = "Pass";

    public static PrerequisiteFormModel FromPrerequisite(
        PrerequisiteSummary prerequisite)
    {
        return new PrerequisiteFormModel
        {
            CourseId = prerequisite.CourseId,
            RequiredCourseId = prerequisite.RequiredCourseId,
            MinimumGrade = prerequisite.MinimumGrade ?? "Pass"
        };
    }
}

public sealed class CompletedCourseSelection
{
    public int CourseId { get; init; }
    public required string CourseCode { get; init; }
    public required string CourseName { get; init; }
    public bool IsCompleted { get; set; }
    public string Grade { get; set; } = "Pass";
}

public sealed record CompletedCourseRequest(string CourseCode, string Grade);

public sealed record PrerequisiteEligibilityRequest(
    IReadOnlyList<CompletedCourseRequest> CompletedCourses);

public sealed record PrerequisiteEligibilityResult(
    int CourseId,
    string CourseCode,
    bool IsEligible,
    IReadOnlyList<PrerequisiteRequirementResult> Requirements);

public sealed record PrerequisiteRequirementResult(
    int RequiredCourseId,
    string RequiredCourseCode,
    string MinimumGrade,
    string? AchievedGrade,
    bool IsMet,
    string Reason);

public static class GradeOptions
{
    public static IReadOnlyList<string> Values { get; } =
    [
        "Fail",
        "Pass",
        "Credit",
        "Distinction",
        "High Distinction"
    ];
}
