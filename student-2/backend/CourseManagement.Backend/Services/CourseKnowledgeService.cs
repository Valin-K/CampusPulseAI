using System.Globalization;
using System.Net.Http.Json;
using CampusPulse.Shared.Rag.Assistant;
using CampusPulse.Shared.Rag.Contracts;
using CampusPulse.Shared.Rag.Generation;
using CourseManagement.Backend.Contracts.Ai;

namespace CourseManagement.Backend.Services;

public sealed class CourseKnowledgeService(
    IHttpClientFactory clients,
    RagKnowledgeService knowledge,
    ILogger<CourseKnowledgeService> logger) : ICourseRagService
{
    public const string CatalogueClientName = "courseCatalogueData";

    private static readonly RagFeature Feature = RagFeature.CourseManagement;

    public async Task<RagAssistantAnswer> AskAsync(
        KnowledgeQuestionRequest request, CancellationToken cancellationToken)
    {
        var corpus = await BuildCorpusAsync(cancellationToken);

        // AGENT-MIGRATION: handing the corpus to the shared RAG assistant (ask + citation parsing)
        // moves into the multi-agent server; building this feature's corpus stays here because this
        // feature owns its data.
        return await knowledge.AskAsync(Feature, request.Question, corpus, cancellationToken);
    }

    private async Task<IReadOnlyList<RagDocument>> BuildCorpusAsync(CancellationToken cancellationToken)
    {
        var documents = new List<RagDocument>(PolicyDocuments);

        try
        {
            var courses = await clients.CreateClient(CatalogueClientName)
                .GetFromJsonAsync<List<CourseCatalogueItem>>("/api/courses", cancellationToken);
            documents.AddRange(courses?.Select(CourseDocument) ?? []);
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

    private static RagDocument CourseDocument(CourseCatalogueItem course)
    {
        var culture = CultureInfo.InvariantCulture;
        var text =
            $"Course {course.Code} — {course.Name}. Worth {course.CreditPoints.ToString(culture)} credit points, "
            + $"graded as {course.GradeType}. {(course.IsActive ? "It is open for enrolment." : "It is not currently offered.")}";
        return new RagDocument(
            $"course-{course.Code}",
            text,
            new Dictionary<string, string> { ["code"] = course.Code, ["title"] = course.Name });
    }

    private sealed record CourseCatalogueItem(string Code, string Name, int CreditPoints, string GradeType, bool IsActive);

    private static readonly IReadOnlyList<RagDocument> PolicyDocuments =
    [
        new(
            "policy-catalogue",
            "Course catalogue. Every course has a unique code, a name, a credit-point value and a grading type. "
            + "A course can be active or inactive, and only active courses are open for enrolment.",
            new Dictionary<string, string> { ["title"] = "Course catalogue" }),
        new(
            "policy-prerequisites",
            "Prerequisites. A course may require other courses to be completed first, each with a minimum grade. "
            + "A student is eligible to enrol in a course only when every prerequisite requirement is met.",
            new Dictionary<string, string> { ["title"] = "Prerequisites" }),
        new(
            "policy-enrolment",
            "Enrolment. Students enrol in courses for an academic term. Enrolment is gated by prerequisite "
            + "eligibility and by whether the course is currently active.",
            new Dictionary<string, string> { ["title"] = "Enrolment" }),
    ];
}
