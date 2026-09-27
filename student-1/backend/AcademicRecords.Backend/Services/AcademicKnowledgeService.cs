using System.Net.Http.Json;
using System.Text.Json;
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
        if (IsStudentEnrollmentQuestion(request.Question))
        {
            return request.UserId is > 0
                ? await GetStudentEnrollmentsAsync(request, cancellationToken)
                : MissingStudentContext(request.Question);
        }

        var corpus = await BuildCorpusAsync(cancellationToken);

        // AGENT-MIGRATION: handing the corpus to the shared RAG assistant (ask + citation parsing)
        // moves into the multi-agent server; building this feature's corpus stays here because this
        // feature owns its data.
        return await knowledge.AskAsync(Feature, request.Question, corpus, cancellationToken);
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

    private async Task<RagAssistantAnswer> GetStudentEnrollmentsAsync(
        KnowledgeQuestionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await clients.CreateClient("mcp").PostAsJsonAsync(
                "/api/mcp/tools/call",
                new McpToolCallRequest(
                    "get_student_enrollments",
                    new Dictionary<string, object?> { ["user_id"] = request.UserId }),
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"The MCP server returned HTTP {(int)response.StatusCode} while retrieving student enrollments.");
            }

            var toolResponse = await response.Content.ReadFromJsonAsync<McpToolCallResponse>(
                cancellationToken: cancellationToken);
            if (toolResponse is null || toolResponse.IsError || toolResponse.Result.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("The MCP server could not retrieve student enrollments.");
            }

            var profile = toolResponse.Result.Deserialize<StudentEnrollmentToolResult>(JsonOptions);
            if (profile is null)
            {
                throw new InvalidOperationException("The MCP server returned an invalid enrollment response.");
            }

            var reply = profile.Enrollments.Count == 0
                ? $"No enrollment records were found for student {profile.StudentId}."
                : $"Enrollment records for {profile.StudentId}:\n{string.Join("\n", profile.Enrollments.Select(FormatEnrollment))}";

            return new RagAssistantAnswer(
                request.Question,
                "answered",
                reply,
                1,
                [new RagKnowledgeSource(1, "Student enrollment records", "mcp:get_student_enrollments", 1)],
                "mcp");
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Could not retrieve student enrollments from the MCP server.");
            throw new InvalidOperationException("The MCP server is unavailable while retrieving student enrollments.", exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Timed out retrieving student enrollments from the MCP server.");
            throw new InvalidOperationException("The MCP server timed out while retrieving student enrollments.", exception);
        }
    }

    private static bool IsStudentEnrollmentQuestion(string question)
    {
        var normalized = $" {question.Trim().ToLowerInvariant()} ";
        var isPersonal = new[] { " my ", " i ", " me ", " have i ", " am i ", " did i ", " for me " }
            .Any(normalized.Contains);
        var isEnrollmentRelated = new[]
        {
            "enroll", "enrol", "course", "class", "subject", "grade", "mark", "passed", "failed", "completed", "term"
        }.Any(normalized.Contains);

        return isPersonal && isEnrollmentRelated;
    }

    private static RagAssistantAnswer MissingStudentContext(string question) =>
        new(
            question,
            "insufficient_context",
            "Open your student profile to ask about your enrollment records.",
            0,
            [],
            "mcp");

    private static string FormatEnrollment(StudentEnrollmentToolRecord enrollment)
    {
        var term = enrollment.TermName ?? enrollment.TermCode ?? "Unknown term";
        var mark = enrollment.GradeValue is decimal grade
            ? $", mark {grade.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}"
            : string.Empty;
        var finalized = enrollment.IsFinalized ? "finalized" : "not finalized";
        return $"- {enrollment.CourseCode} — {term} {enrollment.AcademicYear}: {enrollment.Status}{mark}, {finalized}";
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
