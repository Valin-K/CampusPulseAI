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
            var courses = await clients.CreateClient("courseManagementDatabase")
                .GetFromJsonAsync<List<CourseMetadataResponse>>("/api/courses", cancellationToken);
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
            "Enrolment and grading. An enrolment moves through statuses such as Enrolled or In Progress and becomes "
            + "finalized once it is graded. Only finalized, passed enrolments that carry credit points count towards "
            + "graduation, WAM and GPA.",
            new Dictionary<string, string> { ["title"] = "Enrolment and grading" }),
    ];
}
