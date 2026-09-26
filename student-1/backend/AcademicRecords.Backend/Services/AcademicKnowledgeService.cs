using System.Globalization;
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
        var corpus = await BuildCorpusAsync(request.UserId, cancellationToken);

        // AGENT-MIGRATION: handing the corpus to the shared RAG assistant (ask + citation parsing)
        // moves into the multi-agent server; building this feature's corpus stays here because this
        // feature owns its data.
        return await knowledge.AskAsync(Feature, request.Question, corpus, cancellationToken);
    }

    private async Task<IReadOnlyList<RagDocument>> BuildCorpusAsync(
        int? userId, CancellationToken cancellationToken)
    {
        var documents = new List<RagDocument>(PolicyDocuments);
        List<CourseMetadataResponse> courses = [];

        try
        {
            var loadedCourses = await clients.CreateClient("courseManagementDatabase")
                .GetFromJsonAsync<List<CourseMetadataResponse>>("/api/courses", cancellationToken);
            courses = loadedCourses ?? [];
            documents.AddRange(courses.Select(CourseDocument));
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Could not load the course catalogue for the knowledge corpus.");
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Timed out loading the course catalogue for the knowledge corpus.");
        }

        if (userId is > 0)
        {
            await AddEnrollmentDocumentsAsync(userId.Value, courses, documents, cancellationToken);
        }

        return documents;
    }

    private async Task AddEnrollmentDocumentsAsync(
        int userId,
        IReadOnlyList<CourseMetadataResponse> courses,
        List<RagDocument> documents,
        CancellationToken cancellationToken)
    {
        try
        {
            var profile = await clients.CreateClient("academicRecordsDatabase")
                .GetFromJsonAsync<StudentEnrollmentProfile>(
                    $"/api/students/by-user/{userId}", cancellationToken);
            if (profile?.Enrollments is null)
            {
                return;
            }

            var coursesByCode = courses.ToDictionary(course => course.Code, StringComparer.OrdinalIgnoreCase);
            documents.AddRange(profile.Enrollments.Select(enrollment =>
                EnrollmentDocument(profile.StudentId, enrollment, coursesByCode)));
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Could not load student enrollments for the knowledge corpus.");
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Timed out loading student enrollments for the knowledge corpus.");
        }
    }

    private static RagDocument CourseDocument(CourseMetadataResponse course) =>
        new(
            $"course-{course.Code}",
            $"Course {course.Code} — {course.Name}. Worth {course.CreditPoints} credit points, graded as {course.GradeType}.",
            new Dictionary<string, string> { ["code"] = course.Code, ["title"] = course.Name });

    private static RagDocument EnrollmentDocument(
        string studentId,
        StudentEnrollmentResponse enrollment,
        IReadOnlyDictionary<string, CourseMetadataResponse> courses)
    {
        courses.TryGetValue(enrollment.CourseCode, out var course);
        var courseTitle = course?.Name ?? enrollment.CourseCode;
        var term = enrollment.TermName ?? enrollment.TermCode ?? "Unknown term";
        var credits = course is null
            ? string.Empty
            : $" Worth {course.CreditPoints.ToString(CultureInfo.InvariantCulture)} credit points. ";
        var mark = enrollment.GradeValue is decimal gradeValue
            ? $" Recorded mark {gradeValue.ToString(CultureInfo.InvariantCulture)}."
            : string.Empty;
        var gradeType = course is null ? string.Empty : $" Graded as {course.GradeType}.";

        return new RagDocument(
            $"enrollment-{enrollment.EnrollmentId}",
            $"Student {studentId} is enrolled in {courseTitle} ({enrollment.CourseCode}) for {term} "
            + $"{enrollment.AcademicYear}. Current status: {enrollment.Status}. "
            + $"{(enrollment.IsFinalized ? "Finalized." : "Not finalized.")}{credits}{mark}{gradeType}",
            new Dictionary<string, string>
            {
                ["studentId"] = studentId,
                ["courseCode"] = enrollment.CourseCode,
                ["title"] = courseTitle,
                ["term"] = term,
                ["status"] = enrollment.Status
            });
    }

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
}
