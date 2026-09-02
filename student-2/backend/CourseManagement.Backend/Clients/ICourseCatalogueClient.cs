using CourseManagement.Backend.Contracts.CourseAccess;

namespace CourseManagement.Backend.Clients;

public interface ICourseCatalogueClient
{
    Task<CourseCatalogueResponse?> GetByCodeAsync(
        string courseCode,
        CancellationToken cancellationToken);

    Task<CourseCatalogueEligibilityResponse> CheckPrerequisiteEligibilityAsync(
        int courseId,
        IReadOnlyList<CompletedCourseRequest> completedCourses,
        CancellationToken cancellationToken);
}

public sealed record CourseCatalogueResponse(
    int Id,
    string Code,
    bool IsActive);

public sealed record CourseCatalogueEligibilityRequest(
    IReadOnlyList<CompletedCourseRequest> CompletedCourses);

public sealed record CourseCatalogueEligibilityResponse(
    int CourseId,
    string CourseCode,
    bool IsEligible,
    IReadOnlyList<CourseCatalogueRequirementResponse> Requirements);

public sealed record CourseCatalogueRequirementResponse(
    int RequiredCourseId,
    string RequiredCourseCode,
    string MinimumGrade,
    string? AchievedGrade,
    bool IsMet,
    string Reason);

public sealed class CourseCatalogueResponseException(
    string message,
    Exception? innerException = null) : Exception(message, innerException);
