using CourseManagement.Backend.Clients;
using CourseManagement.Backend.Contracts.CourseAccess;
using CourseManagement.Backend.Controllers;
using CourseManagement.Backend.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;

namespace CourseManagement.Tests;

public sealed class CourseAccessControllerTests
{
    [Test]
    public async Task GetUsersReturnsOnlyConfiguredMappedUsers()
    {
        var users = new[]
        {
            User(1, "Other", "Student", studentId: "STU001"),
            User(3, "Joyce", "Student", studentId: "STU003"),
            User(8, "Grace", "Staff", staffId: "STF001"),
            User(9, "Other", "Staff", staffId: "STF002")
        };
        var controller = CreateController(users);

        var result = await controller.GetUsers(CancellationToken.None);

        var ok = result.Result as OkObjectResult;
        var response = ok?.Value as CourseAccessUserResponse[];
        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.Not.Null);
            Assert.That(response, Has.Length.EqualTo(2));
            Assert.That(response![0], Is.EqualTo(new CourseAccessUserResponse(
                3,
                "STU003",
                "Joyce User",
                "joyce@example.edu",
                "Student",
                CourseAccessRoles.Student)));
            Assert.That(response[1], Is.EqualTo(new CourseAccessUserResponse(
                8,
                "STF001",
                "Grace User",
                "grace@example.edu",
                "Staff",
                CourseAccessRoles.CourseAdministrator)));
        });
    }

    [Test]
    public async Task GetUsersReturnsBadGatewayWhenConfiguredStudentIsMissing()
    {
        var controller = CreateController(
            [User(8, "Grace", "Staff", staffId: "STF001")]);

        var result = await controller.GetUsers(CancellationToken.None);

        AssertProblemStatus(result.Result, StatusCodes.Status502BadGateway);
    }

    [Test]
    public async Task GetUsersReturnsBadGatewayWhenConfiguredUserHasWrongRole()
    {
        var controller = CreateController(
        [
            User(3, "Joyce", "Staff", studentId: "STU003"),
            User(8, "Grace", "Staff", staffId: "STF001")
        ]);

        var result = await controller.GetUsers(CancellationToken.None);

        AssertProblemStatus(result.Result, StatusCodes.Status502BadGateway);
    }

    [Test]
    public async Task GetUsersReturnsBadGatewayWhenConfiguredUserIsIncomplete()
    {
        var controller = CreateController(
        [
            User(3, "", "Student", studentId: "STU003"),
            User(8, "Grace", "Staff", staffId: "STF001")
        ]);

        var result = await controller.GetUsers(CancellationToken.None);

        AssertProblemStatus(result.Result, StatusCodes.Status502BadGateway);
    }

    [Test]
    public async Task GetUsersReturnsBadGatewayForInvalidUpstreamResponse()
    {
        var controller = CreateController(
            new AcademicRecordsResponseException("Invalid response."));

        var result = await controller.GetUsers(CancellationToken.None);

        AssertProblemStatus(result.Result, StatusCodes.Status502BadGateway);
    }

    [Test]
    public async Task GetUsersReturnsServiceUnavailableWhenUpstreamCannotBeReached()
    {
        var controller = CreateController(new HttpRequestException("Unavailable."));

        var result = await controller.GetUsers(CancellationToken.None);

        AssertProblemStatus(result.Result, StatusCodes.Status503ServiceUnavailable);
    }

    [Test]
    public async Task GetUsersReturnsServiceUnavailableWhenUpstreamTimesOut()
    {
        var controller = CreateController(new TaskCanceledException("Timed out."));

        var result = await controller.GetUsers(CancellationToken.None);

        AssertProblemStatus(result.Result, StatusCodes.Status503ServiceUnavailable);
    }

    static CourseAccessController CreateController(
        IReadOnlyList<AcademicRecordsUserResponse> users) =>
        new(
            new StubAcademicRecordsClient(users),
            Options.Create(new CourseAccessOptions()),
            NullLogger<CourseAccessController>.Instance);

    static CourseAccessController CreateController(Exception exception) =>
        new(
            new StubAcademicRecordsClient(exception),
            Options.Create(new CourseAccessOptions()),
            NullLogger<CourseAccessController>.Instance);

    static AcademicRecordsUserResponse User(
        int userId,
        string firstName,
        string role,
        string? studentId = null,
        string? staffId = null) =>
        new(
            userId,
            firstName,
            "User",
            $"{firstName.ToLowerInvariant()}@example.edu",
            role,
            studentId,
            staffId);

    static void AssertProblemStatus(
        IActionResult? result,
        int expectedStatus)
    {
        var objectResult = result as ObjectResult;
        Assert.Multiple(() =>
        {
            Assert.That(objectResult, Is.Not.Null);
            Assert.That(objectResult!.StatusCode, Is.EqualTo(expectedStatus));
            Assert.That(objectResult.Value, Is.TypeOf<ProblemDetails>());
        });
    }

    sealed class StubAcademicRecordsClient : IAcademicRecordsClient
    {
        readonly IReadOnlyList<AcademicRecordsUserResponse>? users;
        readonly Exception? exception;

        public StubAcademicRecordsClient(IReadOnlyList<AcademicRecordsUserResponse> users)
        {
            this.users = users;
        }

        public StubAcademicRecordsClient(Exception exception)
        {
            this.exception = exception;
        }

        public Task<IReadOnlyList<AcademicRecordsUserResponse>> GetUsersAsync(
            CancellationToken cancellationToken) =>
            exception is null
                ? Task.FromResult(users!)
                : Task.FromException<IReadOnlyList<AcademicRecordsUserResponse>>(exception);

        public Task<AcademicRecordsStudentResponse> GetStudentAsync(
            string studentId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task CreateEnrollmentAsync(
            string studentId,
            AcademicRecordsEnrollmentRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
