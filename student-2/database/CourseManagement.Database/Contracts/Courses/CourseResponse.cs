using CourseManagement.Backend.Contracts.FacultyAssignments;
using CourseManagement.Backend.Contracts.Prerequisites;

namespace CourseManagement.Backend.Contracts.Courses;

public sealed record CourseResponse(
    int Id,
    string Code,
    string Name,
    string Description,
    int CreditPoints,
    int Level,
    bool IsActive,
    string? SyllabusFileName);

public sealed record CourseDetailResponse(
    int Id,
    string Code,
    string Name,
    string Description,
    int CreditPoints,
    int Level,
    bool IsActive,
    string? SyllabusFileName,
    IReadOnlyList<PrerequisiteResponse> Prerequisites,
    IReadOnlyList<FacultyAssignmentResponse> FacultyAssignments);
