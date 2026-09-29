using System.Net.Http.Json;
using System.Text.Json.Nodes;
using AcademicRecords.Backend.Contracts;

namespace AcademicRecords.Backend.Services;

public sealed class StudentCourseMetadataService(IHttpClientFactory clients)
{
    public async Task<IReadOnlyDictionary<string, CourseMetadataResponse>> GetCourseMetadataAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var courses = await clients.CreateClient("courseManagementDatabase")
                .GetFromJsonAsync<CourseMetadataResponse[]>("/api/courses", cancellationToken);

            return courses?.ToDictionary(course => course.Code, StringComparer.OrdinalIgnoreCase)
                ?? new Dictionary<string, CourseMetadataResponse>(StringComparer.OrdinalIgnoreCase);
        }
        catch (HttpRequestException)
        {
            return new Dictionary<string, CourseMetadataResponse>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public static void AddCourseMetadata(
        JsonObject student,
        IReadOnlyDictionary<string, CourseMetadataResponse> courses)
    {
        if (student["enrollments"] is not JsonArray enrollments)
        {
            return;
        }

        foreach (var enrollment in enrollments.OfType<JsonObject>())
        {
            var courseCode = enrollment["courseCode"]?.GetValue<string>();
            CourseMetadataResponse? course = null;
            if (courseCode is not null)
            {
                courses.TryGetValue(courseCode, out course);
            }

            enrollment["credits"] = course?.CreditPoints;
            enrollment["courseTitle"] = course?.Name;
            enrollment["gradeType"] = course?.GradeType;
        }
    }

    public static StudentStatisticsProfile ApplyCourseMetadata(
        StudentStatisticsProfile profile,
        IReadOnlyDictionary<string, CourseMetadataResponse> courses) =>
        profile with
        {
            Enrollments = profile.Enrollments?.Select(enrollment =>
            {
                if (courses.TryGetValue(enrollment.CourseCode, out var course))
                {
                    return enrollment with
                    {
                        CourseTitle = course.Name,
                        Credits = course.CreditPoints,
                        GradeType = course.GradeType
                    };
                }

                return enrollment;
            }).ToList()
        };
}
