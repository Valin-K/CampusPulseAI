using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using CampusPulse.Shared.Components.Models;
using CampusPulse.Shared.Components.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

namespace AcademicRecords.Tests;

public sealed class CampusAccountServiceTests
{
    private static readonly CampusAccountUser[] DirectoryUsers =
    [
        new(12, "Avery", "Student", "avery@example.edu", "Student", "STU012", null),
        new(23, "Morgan", "Staff", "morgan@example.edu", "Staff", null, "STF023")
    ];

    [Test]
    public async Task InitializeMatchesTheAuthenticatedEmailToOneDirectoryAccount()
    {
        var service = CreateService(DirectoryHandler(), "avery@example.edu");

        await service.InitializeAsync();

        Assert.That(service.IsInitialized, Is.True);
        Assert.That(service.Users, Has.Count.EqualTo(1));
        Assert.That(service.CurrentUser, Is.EqualTo(DirectoryUsers[0]));
        Assert.That(service.ErrorMessage, Is.Null);
    }

    [Test]
    public async Task InitializeDoesNotSelectAnAccountWhenTheAuthenticatedEmailIsUnknown()
    {
        var service = CreateService(DirectoryHandler(), "unknown@example.edu");

        await service.InitializeAsync();

        Assert.That(service.CurrentUser, Is.Null);
        Assert.That(service.Users, Is.Empty);
        Assert.That(service.CanRegisterStudent, Is.True);
        Assert.That(service.ErrorMessage, Does.Contain("not linked"));
    }

    [Test]
    public async Task UnauthenticatedInitializationDoesNotLoadTheDirectory()
    {
        var requests = 0;
        var handler = new TestHttpMessageHandler((_, _) =>
        {
            requests++;
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, DirectoryUsers));
        });
        var service = CreateService(handler, null);

        await service.InitializeAsync();

        Assert.That(service.CurrentUser, Is.Null);
        Assert.That(service.IsAuthenticated, Is.False);
        Assert.That(requests, Is.Zero);
    }

    [Test]
    public async Task CreateStudentPostsStudentFieldsToTheStudentEnrollmentEndpoint()
    {
        CapturedRequest? captured = null;
        var handler = new TestHttpMessageHandler(async (request, cancellationToken) =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/users")
            {
                return JsonResponse(HttpStatusCode.OK, Array.Empty<CampusAccountUser>());
            }

            captured = await CaptureAsync(request, cancellationToken);
            return JsonResponse(HttpStatusCode.Created, new { studentId = "STU101", userId = 101 });
        });
        var service = CreateService(handler, "riley@example.edu");
        await service.InitializeAsync();
        var registration = new CampusStudentRegistration(
            "Riley", "Jones", "riley@example.edu", new DateOnly(2004, 5, 6),
            "1 Campus Way", "Jordan Jones", "0400000000", "0400000001", "Bachelor", "Science");

        var created = await service.CreateStudentAsync(registration);

        Assert.That(created, Is.EqualTo(new CampusCreatedUser("STU101", 101)));
        Assert.That(captured!.Method, Is.EqualTo(HttpMethod.Post));
        Assert.That(captured.Path, Is.EqualTo("/api/students/enroll"));
        using var body = JsonDocument.Parse(captured.Body);
        Assert.That(body.RootElement.GetProperty("firstName").GetString(), Is.EqualTo("Riley"));
        Assert.That(body.RootElement.GetProperty("mailingAddress").GetString(), Is.EqualTo("1 Campus Way"));
    }

    [Test]
    public async Task CreateStaffPostsToTheStaffEnrollmentEndpoint()
    {
        CapturedRequest? captured = null;
        var handler = new TestHttpMessageHandler(async (request, cancellationToken) =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/users")
            {
                return JsonResponse(HttpStatusCode.OK, Array.Empty<CampusAccountUser>());
            }

            captured = await CaptureAsync(request, cancellationToken);
            return JsonResponse(HttpStatusCode.Created, new { staffId = "STF102", userId = 102 });
        });
        var service = CreateService(handler, "jamie@example.edu");
        await service.InitializeAsync();

        var created = await service.CreateStaffAsync(new CampusStaffRegistration(
            "Jamie", "Lee", "jamie@example.edu", new DateOnly(1985, 7, 8), "0400000002"));

        Assert.That(created, Is.EqualTo(new CampusCreatedStaff("STF102", 102)));
        Assert.That(captured!.Method, Is.EqualTo(HttpMethod.Post));
        Assert.That(captured.Path, Is.EqualTo("/api/staff/enroll"));
    }

    [Test]
    public async Task StudentProfileReadsAndUpdatesTheSignedInStudentsRecord()
    {
        var requests = new List<CapturedRequest>();
        var handler = new TestHttpMessageHandler(async (request, cancellationToken) =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/users")
            {
                return JsonResponse(HttpStatusCode.OK, DirectoryUsers);
            }

            requests.Add(await CaptureAsync(request, cancellationToken));
            if (request.Method == HttpMethod.Get)
            {
                return JsonResponse(HttpStatusCode.OK, new
                {
                    userId = 12,
                    studentId = "STU012",
                    firstName = "Avery",
                    lastName = "Student",
                    email = "avery@example.edu",
                    dateOfBirth = "2001-02-03",
                    phoneNumber = "0400000000",
                    mailingAddress = "1 Old Road",
                    emergencyContactName = "Casey",
                    emergencyContactPhone = "0400000001",
                    enrollmentDate = "2020-02-01",
                    degree = "Bachelor",
                    major = "Arts",
                    subMajor = "History"
                });
            }

            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        var service = CreateService(handler, "avery@example.edu");
        await service.InitializeAsync();

        var profile = await service.GetCurrentProfileAsync();
        Assert.That(profile.UserId, Is.EqualTo(12));
        Assert.That(profile.StudentId, Is.EqualTo("STU012"));
        Assert.That(profile.MailingAddress, Is.EqualTo("1 Old Road"));

        profile.FirstName = "Avery Updated";
        profile.MailingAddress = "2 New Road";
        await service.SaveCurrentProfileAsync(profile);

        Assert.That(requests.Select(request => (request.Method, request.Path)), Is.EqualTo(new[]
        {
            (HttpMethod.Get, "/api/students/by-user/12"),
            (HttpMethod.Put, "/api/students/STU012")
        }));
        using var update = JsonDocument.Parse(requests[1].Body);
        Assert.That(update.RootElement.GetProperty("firstName").GetString(), Is.EqualTo("Avery Updated"));
        Assert.That(update.RootElement.GetProperty("mailingAddress").GetString(), Is.EqualTo("2 New Road"));
        Assert.That(service.CurrentUser?.FirstName, Is.EqualTo("Avery Updated"));
    }

    [Test]
    public async Task StaffProfileUsesStaffEndpointsAndStaffUpdateShape()
    {
        CapturedRequest? updateRequest = null;
        var handler = new TestHttpMessageHandler(async (request, cancellationToken) =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/users")
            {
                return JsonResponse(HttpStatusCode.OK, DirectoryUsers);
            }

            if (request.Method == HttpMethod.Get)
            {
                return JsonResponse(HttpStatusCode.OK, new
                {
                    userId = 23,
                    staffId = "STF023",
                    firstName = "Morgan",
                    lastName = "Staff",
                    email = "morgan@example.edu",
                    dateOfBirth = "1985-07-08",
                    phoneNumber = "0400000002"
                });
            }

            updateRequest = await CaptureAsync(request, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        var service = CreateService(handler, "morgan@example.edu");
        await service.InitializeAsync();

        var profile = await service.GetCurrentProfileAsync();
        profile.PhoneNumber = "0400000099";
        await service.SaveCurrentProfileAsync(profile);

        Assert.That(profile.StaffId, Is.EqualTo("STF023"));
        Assert.That(updateRequest!.Method, Is.EqualTo(HttpMethod.Put));
        Assert.That(updateRequest.Path, Is.EqualTo("/api/staff/STF023"));
        using var body = JsonDocument.Parse(updateRequest.Body);
        Assert.That(body.RootElement.GetProperty("phoneNumber").GetString(), Is.EqualTo("0400000099"));
    }

    [Test]
    public async Task ProfileUpdateCannotTargetAnotherUser()
    {
        var requests = 0;
        var handler = new TestHttpMessageHandler((_, _) =>
        {
            requests++;
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, DirectoryUsers));
        });
        var service = CreateService(handler, "avery@example.edu");
        await service.InitializeAsync();

        Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveCurrentProfileAsync(new CampusUserProfile
        {
            UserId = 23,
            StaffId = "STF023",
            FirstName = "Morgan",
            LastName = "Staff",
            Email = "morgan@example.edu",
            Role = "Staff"
        }));
        Assert.That(requests, Is.EqualTo(1));
    }

    [Test]
    public async Task UserIdHeaderIsSentOnlyForAValidSessionAccount()
    {
        var javaScript = new TestJsRuntime("23");
        var handler = new TestHttpMessageHandler((request, _) =>
        {
            var userIdHeader = request.Headers.TryGetValues("X-CampusPulse-User-Id", out var values)
                ? values.Single()
                : null;
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, new { userIdHeader }));
        });
        var userIdHandler = new CampusUserIdHeaderHandler(javaScript) { InnerHandler = handler };
        using var client = new HttpClient(userIdHandler) { BaseAddress = new Uri("https://service.test/") };

        using var response = await client.GetAsync("api/data");
        var body = await response.Content.ReadFromJsonAsync<HeaderResponse>();

        Assert.That(body?.UserIdHeader, Is.EqualTo("23"));
    }

    [Test]
    public async Task DirectoryFailureLeavesAccountSignedOutAndReportsAnError()
    {
        var handler = new TestHttpMessageHandler((_, _) => throw new HttpRequestException("offline"));
        var service = CreateService(handler, "avery@example.edu");

        await service.InitializeAsync();

        Assert.That(service.CurrentUser, Is.Null);
        Assert.That(service.Users, Is.Empty);
        Assert.That(service.ErrorMessage, Does.Contain("offline"));
        Assert.That(service.IsInitialized, Is.True);
    }

    private static CampusAccountService CreateService(HttpMessageHandler handler, string? email) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("https://academic-records.test/") },
            new TestAuthenticationStateProvider(email));

    private static TestHttpMessageHandler DirectoryHandler() => new((_, _) =>
        Task.FromResult(JsonResponse(HttpStatusCode.OK, DirectoryUsers)));

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, object body) => new(statusCode)
    {
        Content = JsonContent.Create(body)
    };

    private static async Task<CapturedRequest> CaptureAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken) => new(
            request.Method,
            request.RequestUri!.AbsolutePath,
            request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));

    private sealed record CapturedRequest(HttpMethod Method, string Path, string Body);

    private sealed record HeaderResponse(string? UserIdHeader);

    private sealed class TestAuthenticationStateProvider(string? email) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            var identity = email is null
                ? new ClaimsIdentity()
                : new ClaimsIdentity([new Claim("preferred_username", email)], "test");
            return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity)));
        }
    }

    private sealed class TestHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => responder(request, cancellationToken);
    }

    private sealed class TestJsRuntime(string? sessionUserId = null) : IJSRuntime
    {
        public string? SessionUserId { get; private set; } = sessionUserId;
        public List<string> Invocations { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            Invocations.Add(identifier);
            object? result = null;
            switch (identifier)
            {
                case "campusPulseAccountSession.getUserId":
                    result = SessionUserId;
                    break;
                case "campusPulseAccountSession.setUserId":
                    SessionUserId = args?[0]?.ToString();
                    break;
                case "campusPulseAccountSession.removeUserId":
                    SessionUserId = null;
                    break;
            }

            return ValueTask.FromResult(result is TValue value ? value : default!);
        }
    }
}
