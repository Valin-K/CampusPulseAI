using System.Net.Http.Json;
using System.Text.Json;
using McpServer.Contracts;
using McpServer.Tools;

namespace McpServer.Tools.AcademicRecords;

public sealed class AcademicRecordsApiClient(HttpClient academicRecords) : IDisposable
{
    public void Dispose() => academicRecords.Dispose();

    public string RequiredStudentId(
        IReadOnlyDictionary<string, object?> arguments)
    {
        var studentId = McpToolArguments.GetString(arguments, "student_id");
        return !string.IsNullOrWhiteSpace(studentId)
            ? studentId
            : throw new McpToolArgumentException(
                "A non-empty student_id argument is required.");
    }

    public int RequiredUserId(
        IReadOnlyDictionary<string, object?> arguments)
    {
        var userId = McpToolArguments.GetInt32(arguments, "user_id");
        return userId is > 0
            ? userId.Value
            : throw new McpToolArgumentException(
                "A positive user_id argument is required.");
    }

    public string RequiredSubjectCode(
        IReadOnlyDictionary<string, object?> arguments)
    {
        var subjectCode = McpToolArguments.GetString(arguments, "subject_code");
        return !string.IsNullOrWhiteSpace(subjectCode)
            ? subjectCode.Trim()
            : throw new McpToolArgumentException(
                "A non-empty subject_code argument is required.");
    }

    public async Task<McpToolExecution> GetStudentCountAsync(
        CancellationToken cancellationToken)
    {
        var (students, error) = await GetStudentProfilesAsync(cancellationToken);
        return error ?? new McpToolExecution(
            false,
            new { student_count = students.GetArrayLength() });
    }

    public async Task<McpToolExecution> GetStudentsBySubjectAsync(
        string subjectCode,
        CancellationToken cancellationToken)
    {
        var (students, error) = await GetStudentProfilesAsync(cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var studentCount = students.EnumerateArray().Count(student =>
            student.ValueKind == JsonValueKind.Object
            && student.TryGetProperty("enrollments", out var enrollments)
            && enrollments.ValueKind == JsonValueKind.Array
            && enrollments.EnumerateArray().Any(enrollment =>
                enrollment.ValueKind == JsonValueKind.Object
                && enrollment.TryGetProperty("courseCode", out var courseCode)
                && courseCode.ValueKind == JsonValueKind.String
                && string.Equals(
                    courseCode.GetString(),
                    subjectCode,
                    StringComparison.OrdinalIgnoreCase)));

        return new McpToolExecution(
            false,
            new
            {
                subject_code = subjectCode,
                student_count = studentCount
            });
    }

    public async Task<McpToolExecution> GetEnrollmentsAsync(
        int userId,
        CancellationToken cancellationToken)
    {
        using var response = await academicRecords.GetAsync(
            $"/api/students/by-user/{userId}",
            cancellationToken);
        var payload = await ReadPayloadAsync(response, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return new McpToolExecution(
                true,
                new { error = "No student profile was found for this user_id." });
        }

        if (!response.IsSuccessStatusCode)
        {
            return Error(response, payload);
        }

        if (payload.ValueKind != JsonValueKind.Object
            || !payload.TryGetProperty("studentId", out var studentId)
            || studentId.ValueKind != JsonValueKind.String)
        {
            return new McpToolExecution(
                true,
                new { error = "Academic Records returned an invalid student enrollment response." });
        }

        var enrollments = payload.TryGetProperty("enrollments", out var enrollmentList)
            && enrollmentList.ValueKind == JsonValueKind.Array
                ? enrollmentList.Clone()
                : JsonSerializer.SerializeToElement(Array.Empty<object>());

        return new McpToolExecution(
            false,
            new
            {
                studentId = studentId.GetString(),
                enrollments
            });
    }

    public async Task<McpToolExecution> GetAcademicStatisticsAsync(
        string studentId,
        CancellationToken cancellationToken)
    {
        return await GetAsync(
            $"/api/students/{Uri.EscapeDataString(studentId)}/statistics",
            cancellationToken);
    }

    private async Task<McpToolExecution> GetAsync(
        string requestUri,
        CancellationToken cancellationToken)
    {
        using var response = await academicRecords.GetAsync(
            requestUri,
            cancellationToken);
        return await ToExecutionAsync(response, cancellationToken);
    }

    private async Task<(JsonElement Students, McpToolExecution? Error)> GetStudentProfilesAsync(
        CancellationToken cancellationToken)
    {
        using var response = await academicRecords.GetAsync(
            "/api/students",
            cancellationToken);
        var payload = await ReadPayloadAsync(response, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return (default, Error(response, payload));
        }

        if (payload.ValueKind != JsonValueKind.Array)
        {
            return (
                default,
                new McpToolExecution(
                    true,
                    new { error = "Academic Records returned an invalid student list response." }));
        }

        return (payload, null);
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
