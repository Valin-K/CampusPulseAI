using System.Net.Http.Json;
using System.Text.Json;
using McpServer.Contracts;
using McpServer.Tools;

namespace McpServer.Tools.CourseManagement;

public sealed class CourseManagementApiClient(HttpClient courseManagement) : IDisposable
{
    public void Dispose() => courseManagement.Dispose();

    public async Task<int> ResolveCourseIdAsync(
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var hasCourseId = arguments.ContainsKey("courseId");
        var courseCode = OptionalString(arguments, "courseCode");
        var courseName = OptionalString(arguments, "courseName");
        var identifierCount = (hasCourseId ? 1 : 0)
            + (courseCode is null ? 0 : 1)
            + (courseName is null ? 0 : 1);

        if (identifierCount != 1)
        {
            throw new McpToolArgumentException(
                "Provide exactly one of courseId, courseCode, or courseName.");
        }

        if (hasCourseId)
        {
            return RequiredCourseId(arguments);
        }

        using var response = await courseManagement.GetAsync(
            "/api/courses",
            cancellationToken);
        var payload = await ReadPayloadAsync(response, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"The Course API returned HTTP {(int)response.StatusCode} while resolving the Course.");
        }

        if (payload.ValueKind != JsonValueKind.Array)
        {
            throw new HttpRequestException(
                "The Course API returned an invalid Course catalogue.");
        }

        var propertyName = courseCode is null ? "name" : "code";
        var expectedValue = courseCode ?? courseName!;
        var matches = payload
            .EnumerateArray()
            .Where(course =>
                course.TryGetProperty(propertyName, out var property)
                && property.ValueKind == JsonValueKind.String
                && string.Equals(
                    property.GetString(),
                    expectedValue,
                    StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matches.Count == 0)
        {
            throw new McpToolArgumentException(
                $"No Course matched {propertyName} '{expectedValue}'.");
        }

        if (matches.Count > 1)
        {
            throw new McpToolArgumentException(
                $"More than one Course matched {propertyName} '{expectedValue}'.");
        }

        if (!matches[0].TryGetProperty("id", out var id)
            || !id.TryGetInt32(out var courseId)
            || courseId <= 0)
        {
            throw new HttpRequestException(
                "The matching Course did not contain a valid ID.");
        }

        return courseId;
    }

    public int RequiredCourseId(
        IReadOnlyDictionary<string, object?> arguments)
    {
        if (!arguments.TryGetValue("courseId", out var value))
        {
            throw new McpToolArgumentException(
                "A positive courseId argument is required.");
        }

        var courseId = value switch
        {
            JsonElement { ValueKind: JsonValueKind.Number } element
                when element.TryGetInt32(out var number) => number,
            int number => number,
            long number when number <= int.MaxValue => (int)number,
            _ when int.TryParse(value?.ToString(), out var number) => number,
            _ => 0
        };

        return courseId > 0
            ? courseId
            : throw new McpToolArgumentException(
                "A positive courseId argument is required.");
    }

    public JsonElement RequiredCompletedCourses(
        IReadOnlyDictionary<string, object?> arguments)
    {
        if (arguments.TryGetValue("completedCourses", out var value)
            && value is JsonElement
            {
                ValueKind: JsonValueKind.Array
            } completedCourses)
        {
            return completedCourses.Clone();
        }

        throw new McpToolArgumentException(
            "A completedCourses array is required.");
    }

    public Task<McpToolExecution> ListCoursesAsync(
        CancellationToken cancellationToken) =>
        GetAsync("/api/courses", cancellationToken);

    public Task<McpToolExecution> GetCourseDetailsAsync(
        int courseId,
        CancellationToken cancellationToken) =>
        GetAsync($"/api/courses/{courseId}", cancellationToken);

    public async Task<McpToolExecution> GetCoursePrerequisitesAsync(
        int courseId,
        CancellationToken cancellationToken)
    {
        using var response = await courseManagement.GetAsync(
            $"/api/courses/{courseId}",
            cancellationToken);
        var payload = await ReadPayloadAsync(response, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return Error(response, payload);
        }

        if (payload.ValueKind != JsonValueKind.Object
            || !payload.TryGetProperty("prerequisites", out var property))
        {
            return new McpToolExecution(
                true,
                new
                {
                    error = "The Course API response did not contain 'prerequisites'."
                });
        }

        return new McpToolExecution(
            false,
            new Dictionary<string, object?>
            {
                ["courseId"] = courseId,
                ["prerequisites"] = property.Clone()
            });
    }

    public Task<McpToolExecution> GetFacultyAssignmentsAsync(
        int courseId,
        CancellationToken cancellationToken) =>
        GetAsync($"/api/faculty-assignments?courseId={courseId}", cancellationToken);

    public async Task<McpToolExecution> CheckPrerequisiteEligibilityAsync(
        int courseId,
        JsonElement completedCourses,
        CancellationToken cancellationToken)
    {
        using var response = await courseManagement.PostAsJsonAsync(
            $"/api/courses/{courseId}/prerequisite-eligibility",
            new Dictionary<string, object?>
            {
                ["completedCourses"] = completedCourses
            },
            cancellationToken);

        return await ToExecutionAsync(response, cancellationToken);
    }

    private async Task<McpToolExecution> GetAsync(
        string requestUri,
        CancellationToken cancellationToken)
    {
        using var response = await courseManagement.GetAsync(
            requestUri,
            cancellationToken);
        return await ToExecutionAsync(response, cancellationToken);
    }

    private static async Task<McpToolExecution> ToExecutionAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var payload = await ReadPayloadAsync(response, cancellationToken);
        return response.IsSuccessStatusCode
            ? new McpToolExecution(false, payload)
            : Error(response, payload);
    }

    private static McpToolExecution Error(
        HttpResponseMessage response,
        JsonElement payload) =>
        new(
            true,
            new
            {
                statusCode = (int)response.StatusCode,
                error = payload
            });

    private static async Task<JsonElement> ReadPayloadAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength == 0)
        {
            return JsonSerializer.SerializeToElement(new { });
        }

        return await response.Content.ReadFromJsonAsync<JsonElement>(
            cancellationToken);
    }

    private static string? OptionalString(
        IReadOnlyDictionary<string, object?> arguments,
        string name)
    {
        if (!arguments.TryGetValue(name, out var value))
        {
            return null;
        }

        var text = value switch
        {
            JsonElement { ValueKind: JsonValueKind.String } element =>
                element.GetString(),
            string stringValue => stringValue,
            _ => null
        };

        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }
}
