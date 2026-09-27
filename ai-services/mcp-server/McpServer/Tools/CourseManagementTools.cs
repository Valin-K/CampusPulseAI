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
            CourseIdentifierSchema()),
        new(
            "course.get_prerequisites",
            "Get the prerequisite requirements for one course.",
            CourseIdentifierSchema()),
        new(
            "course.get_faculty_assignments",
            "Get the faculty members assigned to one course.",
            CourseIdentifierSchema()),
        new(
            "course.check_prerequisite_eligibility",
            "Check whether completed courses and grades satisfy a target course's prerequisite requirements.",
            CourseIdentifierSchema(
                ("completedCourses",
                    "Required array of objects containing courseCode and grade.")))
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
                await GetCourseDetailsAsync(
                    request.Arguments,
                    cancellationToken),
            "course.get_prerequisites" =>
                await GetCoursePropertyAsync(
                    await ResolveCourseIdAsync(
                        request.Arguments,
                        cancellationToken),
                    "prerequisites",
                    cancellationToken),
            "course.get_faculty_assignments" =>
                await GetFacultyAssignmentsAsync(
                    request.Arguments,
                    cancellationToken),
            "course.check_prerequisite_eligibility" =>
                await CheckEligibilityAsync(
                    await ResolveCourseIdAsync(
                        request.Arguments,
                        cancellationToken),
                    RequiredCompletedCourses(request.Arguments),
                    cancellationToken),
            _ => new McpToolExecution(
                true,
                new { error = $"Course tool '{request.Tool}' is not registered." })
        };
    }

    private async Task<McpToolExecution> GetCourseDetailsAsync(
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var courseId = await ResolveCourseIdAsync(
            arguments,
            cancellationToken);
        return await GetAsync(
            $"/api/courses/{courseId}",
            cancellationToken);
    }

    private async Task<McpToolExecution> GetFacultyAssignmentsAsync(
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var courseId = await ResolveCourseIdAsync(
            arguments,
            cancellationToken);
        return await GetAsync(
            $"/api/faculty-assignments?courseId={courseId}",
            cancellationToken);
    }

    private async Task<int> ResolveCourseIdAsync(
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

    private static Dictionary<string, string> CourseIdentifierSchema(
        params (string Name, string Description)[] additionalArguments)
    {
        var schema = new Dictionary<string, string>
        {
            ["courseId"] =
                "Optional positive Course database ID. Provide exactly one Course identifier.",
            ["courseCode"] =
                "Optional exact Course code. Provide exactly one Course identifier.",
            ["courseName"] =
                "Optional exact Course name. Provide exactly one Course identifier."
        };

        foreach (var (name, description) in additionalArguments)
        {
            schema[name] = description;
        }

        return schema;
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
