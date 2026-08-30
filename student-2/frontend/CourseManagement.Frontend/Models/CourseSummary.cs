namespace CourseManagement.Frontend.Models;

public sealed record CourseSummary(
    int Id,
    string Code,
    string Name,
    string Description,
    int CreditPoints,
    int Level,
    bool IsActive,
    string? SyllabusFileName);
