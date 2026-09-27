using System.Net.Http.Json;
using System.Text.Json;
using McpServer.Contracts;

namespace McpServer.Tools;

public sealed class CourseManagementTools(HttpClient courseManagement)
{
    public static readonly IReadOnlyList<McpToolDefinition> Definitions =
    [
        new(
            "course.list_courses",
            "List all courses in the Course Management catalogue, including their active status and syllabus availability.",
            new Dictionary<string, string>()),
        new(
            "course.get_details",
            "Get one course with its prerequisites, faculty assignments and syllabus metadata.",
            new Dictionary<string, string>
            {
                ["courseId"] = "Required positive course database ID."
            }),
        new(
            "course.get_prerequisites",
            "Get the prerequisite requirements for one course.",
            new Dictionary<string, string>
            {
                ["courseId"] = "Required positive course database ID."
            }),
        new(
            "course.get_faculty_assignments",
            "Get the faculty members assigned to one course.",
            new Dictionary<string, string>
            {
                ["courseId"] = "Required positive course database ID."
            }),
        new(
            "course.check_prerequisite_eligibility",
            "Check whether completed courses and grades satisfy a target course's prerequisite requirements.",
            new Dictionary<string, string>
            {
                ["courseId"] = "Required positive target course database ID.",
                ["completedCourses"] = "Required array of objects containing courseCode and grade."
            })
    ];

    private static readonly HashSet<string> ToolNames =
        Definitions.Select(tool => tool.Name).ToHashSet(
            StringComparer.OrdinalIgnoreCase);

    public static bool CanHandle(string toolName) => ToolNames.Contains(toolName);

    public async Task<McpToolExecution> ExecuteAsync(
        McpCallRequest request,
        CancellationToken cancellationToken)
    {
        return request.Tool.ToLowerInvariant() switch
        {
            "course.list_courses" =>
                await GetAsync("/api/courses", cancellationToken),
            "course.get_details" =>
                await GetAsync(
                    $"/api/courses/{RequiredCourseId(request.Arguments)}",
                    cancellationToken),
            "course.get_prerequisites" =>
                await GetCoursePropertyAsync(
                    RequiredCourseId(request.Arguments),
                    "prerequisites",
                    cancellationToken),
            "course.get_faculty_assignments" =>
                await GetAsync(
                    $"/api/faculty-assignments?courseId={RequiredCourseId(request.Arguments)}",
                    cancellationToken),
            "course.check_prerequisite_eligibility" =>
                await CheckEligibilityAsync(
                    RequiredCourseId(request.Arguments),
                    RequiredCompletedCourses(request.Arguments),
                    cancellationToken),
            _ => new McpToolExecution(
                true,
                new { error = $"Course tool '{request.Tool}' is not registered." })
        };
    }

    private async Task<McpToolExecution> GetCoursePropertyAsync(
        int courseId,
        string propertyName,
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
            || !payload.TryGetProperty(propertyName, out var property))
        {
            return new McpToolExecution(
                true,
                new
                {
                    error = $"The Course API response did not contain '{propertyName}'."
                });
        }

        return new McpToolExecution(
            false,
            new Dictionary<string, object?>
            {
                ["courseId"] = courseId,
                [propertyName] = property.Clone()
            });
    }

    private async Task<McpToolExecution> CheckEligibilityAsync(
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

    private static int RequiredCourseId(
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

    private static JsonElement RequiredCompletedCourses(
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
}

public sealed class McpToolArgumentException(string message)
    : Exception(message);
