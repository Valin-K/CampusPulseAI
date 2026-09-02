namespace CourseManagement.Frontend.Models;

public sealed record CourseDetail(
    int Id,
    string Code,
    string Name,
    string Description,
    int CreditPoints,
    int Level,
    bool IsActive,
    string? SyllabusFileName,
    IReadOnlyList<PrerequisiteSummary> Prerequisites,
    IReadOnlyList<FacultyAssignmentSummary> FacultyAssignments);

public sealed record PrerequisiteSummary(
    int Id,
    int CourseId,
    string CourseCode,
    int RequiredCourseId,
    string RequiredCourseCode,
    string RequiredCourseName,
    string? MinimumGrade);
