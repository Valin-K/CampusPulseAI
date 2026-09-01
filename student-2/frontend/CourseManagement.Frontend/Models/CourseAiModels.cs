namespace CourseManagement.Frontend.Models;

public sealed record CourseQuestionRequest(
    string Question,
    IReadOnlyList<CourseQuestionContext> Courses,
    bool CoursesExplicitlySelected);

public sealed record CourseQuestionContext(
    string Code,
    string Name,
    string Description,
    int Level,
    int CreditPoints);

public sealed record CourseQuestionResponse(
    string Answer,
    string Model,
    bool IsSyllabusGrounded);
