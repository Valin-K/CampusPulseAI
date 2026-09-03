using System.Net.Http.Json;
using System.Text.Json;
using CourseManagement.Backend.Contracts.CourseAccess;

namespace CourseManagement.Backend.Clients;

public sealed class CourseCatalogueClient(HttpClient httpClient) : ICourseCatalogueClient
{
    public async Task<CourseCatalogueResponse?> GetByCodeAsync(
        string courseCode,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(
            "api/courses",
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new CourseCatalogueResponseException(
                $"Course Management Database returned HTTP {(int)response.StatusCode}.");
        }

        try
        {
            var courses = await response.Content.ReadFromJsonAsync<
                    List<CourseCatalogueResponse>>(cancellationToken)
                ?? throw new CourseCatalogueResponseException(
                    "Course Management Database returned an empty Course response.");

            return courses.SingleOrDefault(course =>
                string.Equals(
                    course.Code,
                    courseCode,
                    StringComparison.OrdinalIgnoreCase));
        }
        catch (JsonException exception)
        {
            throw new CourseCatalogueResponseException(
                "Course Management Database returned an invalid Course response.",
                exception);
        }
        catch (NotSupportedException exception)
        {
            throw new CourseCatalogueResponseException(
                "Course Management Database returned an unsupported Course response.",
                exception);
        }
        catch (InvalidOperationException exception)
        {
            throw new CourseCatalogueResponseException(
                "Course Management Database returned duplicate Course codes.",
                exception);
        }
    }

    public async Task<CourseCatalogueEligibilityResponse> CheckPrerequisiteEligibilityAsync(
        int courseId,
        IReadOnlyList<CompletedCourseRequest> completedCourses,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(
            $"api/courses/{courseId}/prerequisite-eligibility",
            new CourseCatalogueEligibilityRequest(completedCourses),
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new CourseCatalogueResponseException(
                $"Course Management Database returned HTTP {(int)response.StatusCode} " +
                "while checking prerequisite eligibility.");
        }

        try
        {
            return await response.Content.ReadFromJsonAsync<
                    CourseCatalogueEligibilityResponse>(cancellationToken)
                ?? throw new CourseCatalogueResponseException(
                    "Course Management Database returned an empty prerequisite eligibility response.");
        }
        catch (JsonException exception)
        {
            throw new CourseCatalogueResponseException(
                "Course Management Database returned an invalid prerequisite eligibility response.",
                exception);
        }
        catch (NotSupportedException exception)
        {
            throw new CourseCatalogueResponseException(
                "Course Management Database returned an unsupported prerequisite eligibility response.",
                exception);
        }
    }
}
