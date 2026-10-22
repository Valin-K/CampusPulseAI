using System.Net;
using System.Text;
using EventScheduling.Backend.Clients;
using EventScheduling.Backend.Controllers;
using EventScheduling.Backend.Models;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace EventScheduling.Tests;

public sealed class BookingsControllerTests
{
    private static readonly IReadOnlyList<AcademicRecordsUser> Users =
    [
        new(5, "Ryan", "Takayasu", "ryan.takayasu@example.edu", "Student", "STU005", null),
        new(14, "Marcus", "Lee", "marcus@example.edu", "Staff", null, "STF004")
    ];

    private static BookingRequest Request(int studentId = 5) => new(
        studentId, RoomId: 1, RefEquipmentId: null, "Study Room 101",
        new DateTime(2030, 6, 1, 10, 0, 0, DateTimeKind.Utc),
        new DateTime(2030, 6, 1, 12, 0, 0, DateTimeKind.Utc),
        BookingStatus.Confirmed);

    [Test]
    public async Task Create_RejectsABookingForAStudentIdAcademicRecordsDoesNotRecognise()
    {
        var database = new RecordingHttpMessageHandler(Created());
        var controller = Controller(database, new StubAcademicRecordsClient(Users));

        var result = await controller.Create(Request(studentId: 999), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.TypeOf<ForbidResult>());
            Assert.That(database.CallCount, Is.Zero, "an unverified booking should never reach the database tier");
        });
    }

    [Test]
    public async Task Create_ForwardsToTheDatabaseWhenTheStudentIdIsRecognised()
    {
        var database = new RecordingHttpMessageHandler(Created());
        var controller = Controller(database, new StubAcademicRecordsClient(Users));

        var result = await controller.Create(Request(studentId: 5), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That((result as ContentResult)?.StatusCode, Is.EqualTo(201));
            Assert.That(database.CallCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Create_FailsOpenAndForwardsToTheDatabaseWhenAcademicRecordsIsUnavailable()
    {
        var database = new RecordingHttpMessageHandler(Created());
        var controller = Controller(database, new UnavailableAcademicRecordsClient());

        var result = await controller.Create(Request(studentId: 5), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That((result as ContentResult)?.StatusCode, Is.EqualTo(201),
                "booking must keep working when Academic Records is down");
            Assert.That(database.CallCount, Is.EqualTo(1));
        });
    }

    private static BookingsController Controller(HttpMessageHandler databaseHandler, IAcademicRecordsClient academicRecords)
    {
        var controller = new BookingsController(new NamedHttpClientFactory(databaseHandler), academicRecords);
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, "Student"), new Claim("user_id", "5")],
            "test"));
        controller.ControllerContext = new ControllerContext { HttpContext = context };
        return controller;
    }

    private static HttpResponseMessage Created() => new(HttpStatusCode.Created)
    {
        Content = new StringContent(
            """{"id":1,"studentId":5,"roomId":1,"equipmentId":null,"refEquipmentId":null,"resourceType":0,"resourceName":"Study Room 101","startUtc":"2030-06-01T10:00:00","endUtc":"2030-06-01T12:00:00","status":1}""",
            Encoding.UTF8, "application/json")
    };

    private sealed class StubAcademicRecordsClient(IReadOnlyList<AcademicRecordsUser> users) : IAcademicRecordsClient
    {
        public Task<IReadOnlyList<AcademicRecordsUser>> GetUsersAsync(CancellationToken cancellationToken) =>
            Task.FromResult(users);
    }

    private sealed class UnavailableAcademicRecordsClient : IAcademicRecordsClient
    {
        public Task<IReadOnlyList<AcademicRecordsUser>> GetUsersAsync(CancellationToken cancellationToken) =>
            throw new HttpRequestException("Academic Records is unreachable.");
    }

    private sealed class NamedHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler) { BaseAddress = new Uri("https://database.test/") };
    }

    private sealed class RecordingHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(response);
        }
    }
}
