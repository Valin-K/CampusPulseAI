using System.Net.Http.Json;
using System.Text.Json;
using McpServer.Contracts;
using McpServer.Tools;

namespace McpServer.Tools.CourseManagement;

public sealed class CourseManagementApiClient(HttpClient courseManagement) : IDisposable
{
    public void Dispose() => courseManagement.Dispose();

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
}
