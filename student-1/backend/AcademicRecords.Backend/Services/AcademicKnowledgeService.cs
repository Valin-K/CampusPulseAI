using System.Net.Http.Json;
using System.Text.Json;
using AcademicRecords.Backend.Contracts;
using AcademicRecords.Backend.Models;
using CampusPulse.Shared.Rag.Assistant;
using CampusPulse.Shared.Rag.Contracts;
using CampusPulse.Shared.Rag.Generation;

namespace AcademicRecords.Backend.Services;

public sealed class AcademicKnowledgeService(
    IHttpClientFactory clients,
    RagKnowledgeService knowledge,
    ILogger<AcademicKnowledgeService> logger) : IAcademicKnowledgeService
{
    private static readonly RagFeature Feature = RagFeature.AcademicRecords;

    public async Task<RagAssistantAnswer> AskAsync(
        KnowledgeQuestionRequest request, CancellationToken cancellationToken)
    {
        var mcpContext = await GetStudentMcpContextAsync(request.UserId, cancellationToken);
        var corpus = await BuildCorpusAsync(cancellationToken);

        // AGENT-MIGRATION: handing the corpus to the shared RAG assistant (ask + citation parsing)
        // moves into the multi-agent server; building this feature's corpus stays here because this
        // feature owns its data.
        var answer = await knowledge.AskAsync(
            Feature, request.Question, corpus, cancellationToken, mcpContext?.Context);
        if (mcpContext is null)
        {
            return answer;
        }

        var sources = answer.Sources.ToList();
        foreach (var source in mcpContext.Sources)
        {
            sources.Add(new RagKnowledgeSource(
                sources.Count + 1,
                source.Name,
                $"mcp:{source.ToolName}",
                1));
        }

        return answer with { Sources = sources };
    }

    private async Task<IReadOnlyList<RagDocument>> BuildCorpusAsync(
        CancellationToken cancellationToken)
    {
        var documents = new List<RagDocument>(PolicyDocuments);

        try
        {
            var loadedCourses = await clients.CreateClient("courseManagementDatabase")
                .GetFromJsonAsync<List<CourseMetadataResponse>>("/api/courses", cancellationToken);
            documents.AddRange((loadedCourses ?? []).Select(CourseDocument));
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Could not load the course catalogue for the knowledge corpus.");
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Timed out loading the course catalogue for the knowledge corpus.");
        }

        return documents;
    }

    private async Task<StudentMcpContext?> GetStudentMcpContextAsync(
        int? userId, CancellationToken cancellationToken)
    {
        var enrollmentTool = McpTool.GetStudentEnrollments;
        var statisticsTool = McpTool.CalculateAcademicStatistics;
        var enrollmentResult = await CallMcpToolAsync(
            enrollmentTool,
            new Dictionary<string, object?> { ["user_id"] = userId },
            cancellationToken);
        if (enrollmentResult is null)
        {
            return null;
        }

        var profile = enrollmentResult.Value.Deserialize<StudentEnrollmentToolResult>(JsonOptions);
        if (profile is null)
        {
            return null;
        }

        var context = new Dictionary<string, object?>
        {
            ["studentId"] = profile.StudentId,
            ["enrollments"] = profile.Enrollments
        };
        var sources = new List<McpContextSource> { enrollmentTool.Source };
        var statistics = await CallMcpToolAsync(
            statisticsTool,
            new Dictionary<string, object?> { ["student_id"] = profile.StudentId },
            cancellationToken);
        if (statistics is not null)
        {
            context["academicStatistics"] = statistics.Value;
            sources.Add(statisticsTool.Source);
        }

        return new StudentMcpContext(JsonSerializer.Serialize(context, JsonOptions), sources);
    }

    private async Task<JsonElement?> CallMcpToolAsync(
        McpTool tool,
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await clients.CreateClient("mcp").PostAsJsonAsync(
                "/api/mcp/tools/call",
                new McpToolCallRequest(tool.Name, arguments),
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "The MCP server returned HTTP {StatusCode} for {ToolName}; continuing without that MCP context.",
                    (int)response.StatusCode,
                    tool.Name);
                return null;
            }

            var toolResponse = await response.Content.ReadFromJsonAsync<McpToolCallResponse>(
                cancellationToken: cancellationToken);
            if (toolResponse is null || toolResponse.IsError || toolResponse.Result.ValueKind != JsonValueKind.Object)
            {
                logger.LogWarning("MCP tool {ToolName} returned no data; continuing without that MCP context.", tool.Name);
                return null;
            }

            return toolResponse.Result;
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Could not call MCP tool {ToolName}; continuing without that MCP context.", tool.Name);
            return null;
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "MCP tool {ToolName} timed out; continuing without that MCP context.", tool.Name);
            return null;
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "MCP tool {ToolName} returned invalid data; continuing without that MCP context.", tool.Name);
            return null;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static RagDocument CourseDocument(CourseMetadataResponse course) =>
        new(
            $"course-{course.Code}",
            $"Course {course.Code} — {course.Name}. Worth {course.CreditPoints} credit points, graded as {course.GradeType}.",
            new Dictionary<string, string> { ["code"] = course.Code, ["title"] = course.Name });

    private static readonly IReadOnlyList<RagDocument> PolicyDocuments =
    [
        new(
            "policy-graduation",
            "Graduation eligibility. A student must earn 144 credit points to be eligible to graduate. Credit "
            + "is earned from a course only when its enrolment is finalized and the result is passed or completed. "
            + "Any active enrolments must be completed before a student can graduate.",
            new Dictionary<string, string> { ["title"] = "Graduation eligibility" }),
        new(
            "policy-wam",
            "Weighted Average Mark. The WAM is the credit-weighted average of a student's marks across finalized "
            + "enrolments that have a mark and carry credit points. Each mark is weighted by the credit points of "
            + "its course.",
            new Dictionary<string, string> { ["title"] = "Weighted Average Mark" }),
        new(
            "policy-gpa",
            "Grade Point Average. The GPA is the credit-weighted average of grade points. A mark of 85 or above "
            + "earns 7 points, 75 to 84 earns 6 points, 65 to 74 earns 5 points, 50 to 64 earns 4 points, and a "
            + "mark below 50 earns 0 points.",
            new Dictionary<string, string> { ["title"] = "Grade Point Average" }),
        new(
            "policy-enrolment",
            "Enrolment and grading. An enrolment is Enrolled before its session starts and In Progress from the "
            + "session start through its end, even if it already has a grade or is finalized. After the session ends, "
            + "its status is Passed, Failed or Completed based on its result. Enrolled and In Progress courses are "
            + "active. Graduation credit counts finalized Passed enrolments with credit points; WAM and GPA use "
            + "finalized enrolments with a mark and positive credit points, including failed marks.",
            new Dictionary<string, string> { ["title"] = "Enrolment and grading" }),
    ];

    private sealed record McpToolCallRequest(string Tool, Dictionary<string, object?> Arguments);
    private sealed record McpToolCallResponse(bool IsError, JsonElement Result);
    private sealed record McpContextSource(string ToolName, string Name);
    private sealed record McpTool(string Name, McpContextSource Source)
    {
        public static McpTool GetStudentEnrollments { get; } = new(
            "get_student_enrollments", new("get_student_enrollments", "Student enrollment MCP"));

        public static McpTool CalculateAcademicStatistics { get; } = new(
            "calculate_student_academic_statistics", new("calculate_student_academic_statistics", "Academic statistics MCP"));
    }

    private sealed record StudentMcpContext(string Context, IReadOnlyList<McpContextSource> Sources);
    private sealed record StudentEnrollmentToolResult(string StudentId, List<StudentEnrollmentToolRecord> Enrollments);
    private sealed record StudentEnrollmentToolRecord(
        string CourseCode,
        string? TermCode,
        string? TermName,
        int AcademicYear,
        string Status,
        decimal? GradeValue,
        bool IsFinalized);
}
