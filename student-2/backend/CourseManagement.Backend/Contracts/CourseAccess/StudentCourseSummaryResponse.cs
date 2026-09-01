namespace CourseManagement.Backend.Contracts.CourseAccess;

public sealed record StudentCourseSummaryResponse(
    int EnrolledCourseCount,
    decimal CompletedCredits,
    IReadOnlyList<CourseEnrollmentResponse> Enrollments);

public sealed record CourseEnrollmentResponse(
    int EnrollmentId,
    string CourseCode,
    string Status,
    decimal? Credits,
    bool IsFinalized,
    bool IsCurrent);

public sealed record CompletedCourseRequest(
    string CourseCode,
    string Grade);

public sealed record CreateCourseEnrollmentRequest(
    string CourseCode,
    IReadOnlyList<CompletedCourseRequest> CompletedCourses);
