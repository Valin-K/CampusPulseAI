namespace CourseManagement.Frontend.Models;

public sealed record StudentCourseSummary(
    int EnrolledCourseCount,
    decimal CompletedCredits,
    IReadOnlyList<CourseEnrollment> Enrollments);

public sealed record CourseEnrollment(
    int EnrollmentId,
    string CourseCode,
    string Status,
    decimal? Credits,
    bool IsFinalized,
    bool IsCurrent);

public sealed record CreateCourseEnrollmentRequest(
    string CourseCode,
    IReadOnlyList<CompletedCourseRequest> CompletedCourses);
