using CourseManagement.Backend.Clients;
using CourseManagement.Backend.Contracts.CourseAccess;
using CourseManagement.Backend.Controllers;
using CourseManagement.Backend.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CourseManagement.Tests;

public sealed class StudentCourseAccessControllerTests
{
    [Test]
    public async Task GetSummaryCalculatesCurrentCoursesAndCompletedCredits()
    {
        var client = new StubAcademicRecordsClient(Student(
        [
            Enrollment(1, "CPAI1001", "Enrolled", null, false),
            Enrollment(2, "CPAI2001", "In Progress", 6, false),
            Enrollment(3, "CPAI3001", "Passed", 6, true),
            Enrollment(4, "CPAI4001", "Completed", 3, true),
            Enrollment(5, "CPAI5001", "Failed", 6, true)
        ]));
        var controller = CreateController(client);

        var result = await controller.GetSummary(CancellationToken.None);

        var ok = result.Result as OkObjectResult;
        var summary = ok?.Value as StudentCourseSummaryResponse;
        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.Not.Null);
            Assert.That(summary, Is.Not.Null);
            Assert.That(summary!.EnrolledCourseCount, Is.EqualTo(2));
            Assert.That(summary.CompletedCredits, Is.EqualTo(9));
            Assert.That(
                summary.Enrollments.Single(item => item.CourseCode == "CPAI1001").IsCurrent,
                Is.True);
            Assert.That(
                summary.Enrollments.Single(item => item.CourseCode == "CPAI3001").IsCurrent,
                Is.False);
        });
    }

    [Test]
    public async Task CreateEnrollmentUsesConfiguredTermAndReturnsRefreshedSummary()
    {
        var client = new StubAcademicRecordsClient(
            Student([]),
            Student([Enrollment(10, "CPAI3001", "Enrolled", null, false)]));
        var controller = CreateController(client);

        var result = await controller.CreateEnrollment(
            new CreateCourseEnrollmentRequest(
                " CPAI3001 ",
                [new CompletedCourseRequest("CPAI2001", "Credit")]),
            CancellationToken.None);

        var created = result.Result as CreatedAtActionResult;
        var summary = created?.Value as StudentCourseSummaryResponse;
        Assert.Multiple(() =>
        {
            Assert.That(created, Is.Not.Null);
            Assert.That(client.CreatedStudentId, Is.EqualTo("STU003"));
            Assert.That(
                client.CreatedRequest,
                Is.EqualTo(new AcademicRecordsEnrollmentRequest(
                    "CPAI3001",
                    15,
                    2026)));
            Assert.That(summary!.EnrolledCourseCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task CreateEnrollmentRejectsExistingCurrentEnrollment()
    {
        var client = new StubAcademicRecordsClient(Student(
        [
            Enrollment(10, "CPAI3001", "In Progress", null, false)
        ]));
        var controller = CreateController(client);

        var result = await controller.CreateEnrollment(
            new CreateCourseEnrollmentRequest("cpai3001", []),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Result, Is.TypeOf<ConflictObjectResult>());
            Assert.That(client.CreatedRequest, Is.Null);
        });
    }

    [Test]
    public async Task CreateEnrollmentRejectsInactiveCourse()
    {
        var client = new StubAcademicRecordsClient(Student([]));
        var controller = CreateController(
            client,
            new CourseCatalogueResponse(1, "CPAI3001", false));

        var result = await controller.CreateEnrollment(
            new CreateCourseEnrollmentRequest("CPAI3001", []),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Result, Is.TypeOf<ConflictObjectResult>());
            Assert.That(client.CreatedRequest, Is.Null);
        });
    }

    [Test]
    public async Task CreateEnrollmentRejectsCourseOutsideCatalogue()
    {
        var client = new StubAcademicRecordsClient(Student([]));
        var controller = CreateController(
            client,
            course: null,
            useDefaultCourse: false);

        var result = await controller.CreateEnrollment(
            new CreateCourseEnrollmentRequest("UNKNOWN", []),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Result, Is.TypeOf<NotFoundObjectResult>());
            Assert.That(client.CreatedRequest, Is.Null);
        });
    }

    [Test]
    public async Task CreateEnrollmentRejectsUnmetPrerequisitesBeforeAcademicRecords()
    {
        var client = new StubAcademicRecordsClient(Student([]));
        var controller = CreateController(
            client,
            eligibility: new CourseCatalogueEligibilityResponse(
                1,
                "CPAI3001",
                false,
                [
                    new CourseCatalogueRequirementResponse(
                        2,
                        "CPAI2001",
                        "Credit",
                        "Pass",
                        false,
                        "Achieved Pass; minimum grade Credit is required.")
                ]));

        var result = await controller.CreateEnrollment(
            new CreateCourseEnrollmentRequest(
                "CPAI3001",
                [new CompletedCourseRequest("CPAI2001", "Pass")]),
            CancellationToken.None);

        var conflict = result.Result as ConflictObjectResult;
        var problem = conflict?.Value as ProblemDetails;
        Assert.Multiple(() =>
        {
            Assert.That(conflict, Is.Not.Null);
            Assert.That(problem?.Title, Is.EqualTo("Prerequisites not met"));
            Assert.That(client.GetStudentCallCount, Is.Zero);
            Assert.That(client.CreatedRequest, Is.Null);
        });
    }

    [Test]
    public async Task CreateEnrollmentRejectsMismatchedEligibilityResponse()
    {
        var client = new StubAcademicRecordsClient(Student([]));
        var controller = CreateController(
            client,
            eligibility: new CourseCatalogueEligibilityResponse(
                99,
                "CPAI9999",
                true,
                []));

        var result = await controller.CreateEnrollment(
            new CreateCourseEnrollmentRequest("CPAI3001", []),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(
                (result.Result as ObjectResult)?.StatusCode,
                Is.EqualTo(StatusCodes.Status502BadGateway));
            Assert.That(client.GetStudentCallCount, Is.Zero);
            Assert.That(client.CreatedRequest, Is.Null);
        });
    }

    static StudentCourseAccessController CreateController(
        IAcademicRecordsClient client,
        CourseCatalogueResponse? course = null,
        bool useDefaultCourse = true,
        CourseCatalogueEligibilityResponse? eligibility = null) =>
        new(
            client,
            new StubCourseCatalogueClient(
                useDefaultCourse && course is null
                    ? new CourseCatalogueResponse(1, "CPAI3001", true)
                    : course,
                eligibility ?? new CourseCatalogueEligibilityResponse(
                    1,
                    "CPAI3001",
                    true,
                    [])),
            Options.Create(new CourseAccessOptions
            {
                DemoStudentId = "STU003",
                DemoEnrollmentTermId = 15,
                DemoAcademicYear = 2026
            }),
            NullLogger<StudentCourseAccessController>.Instance);

    static AcademicRecordsStudentResponse Student(
        List<AcademicRecordsEnrollmentResponse> enrollments) =>
        new(3, "STU003", enrollments);

    static AcademicRecordsEnrollmentResponse Enrollment(
        int id,
        string courseCode,
        string status,
        decimal? credits,
        bool isFinalized) =>
        new(id, courseCode, 15, status, credits, isFinalized);

    sealed class StubAcademicRecordsClient(
        params AcademicRecordsStudentResponse[] students) : IAcademicRecordsClient
    {
        readonly Queue<AcademicRecordsStudentResponse> studentResponses = new(students);

        public int GetStudentCallCount { get; private set; }
        public string? CreatedStudentId { get; private set; }
        public AcademicRecordsEnrollmentRequest? CreatedRequest { get; private set; }

        public Task<IReadOnlyList<AcademicRecordsUserResponse>> GetUsersAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AcademicRecordsUserResponse>>([]);

        public Task<AcademicRecordsStudentResponse> GetStudentAsync(
            string studentId,
            CancellationToken cancellationToken)
        {
            GetStudentCallCount++;
            return Task.FromResult(studentResponses.Dequeue());
        }

        public Task CreateEnrollmentAsync(
            string studentId,
            AcademicRecordsEnrollmentRequest request,
            CancellationToken cancellationToken)
        {
            CreatedStudentId = studentId;
            CreatedRequest = request;
            return Task.CompletedTask;
        }
    }

    sealed class StubCourseCatalogueClient(
        CourseCatalogueResponse? course,
        CourseCatalogueEligibilityResponse eligibility) : ICourseCatalogueClient
    {
        public Task<CourseCatalogueResponse?> GetByCodeAsync(
            string courseCode,
            CancellationToken cancellationToken) =>
            Task.FromResult(course);

        public Task<CourseCatalogueEligibilityResponse> CheckPrerequisiteEligibilityAsync(
            int courseId,
            IReadOnlyList<CompletedCourseRequest> completedCourses,
            CancellationToken cancellationToken) =>
            Task.FromResult(eligibility);
    }
}
