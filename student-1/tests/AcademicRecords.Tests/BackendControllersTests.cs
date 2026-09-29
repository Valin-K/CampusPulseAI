using System.Net;
using System.Text;
using System.Text.Json;
using AcademicRecords.Backend.Contracts;
using AcademicRecords.Backend.Controllers;
using AcademicRecords.Backend.Models;
using AcademicRecords.Backend.Services;
using CampusPulse.Shared.Mcp.Client;
using CampusPulse.Shared.Rag.Assistant;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace AcademicRecords.Tests;

public sealed class BackendControllersTests
{
    [Test]
    public async Task ProxyControllersForwardRoutesMethodsBodiesAndResponses()
    {
        using var clients = new RecordingHttpClientFactory((_, _, _) => Task.FromResult(JsonResponse("downstream", HttpStatusCode.Accepted)));
        var users = new UsersController(clients);
        var staff = new StaffController(clients);
        var enrollments = new EnrollmentsController(clients);
        var courses = new CourseManagementController(clients);
        var results = new IActionResult[]
        {
            await users.GetAll(CancellationToken.None),
            await staff.GetAll(CancellationToken.None),
            await staff.GetByUser(42, CancellationToken.None),
            await SendWithBody(staff, "staff enrollment", () => staff.Enroll(CancellationToken.None)),
            await SendWithBody(staff, "staff enrollment", () => staff.Update("STF001", CancellationToken.None)),
            await SendWithBody(enrollments, "grade update", () => enrollments.UpdateGrade(17, CancellationToken.None)),
            await courses.GetCourses(CancellationToken.None),
            await courses.GetFacultyAssignments(CancellationToken.None)
        };

        Assert.Multiple(() =>
        {
            Assert.That(results.OfType<ContentResult>().Select(result => result.StatusCode),
                Is.EqualTo(Enumerable.Repeat((int)HttpStatusCode.Accepted, results.Length)));
            Assert.That(results.OfType<ContentResult>().Select(result => result.Content),
                Is.EqualTo(Enumerable.Repeat("downstream", results.Length)));
            Assert.That(clients.Requests.Select(request => (request.ClientName, request.Method, request.Path)),
                Is.EqualTo(new[]
                {
                    ("academicRecordsDatabase", "GET", "/api/users"),
                    ("academicRecordsDatabase", "GET", "/api/staff"),
                    ("academicRecordsDatabase", "GET", "/api/staff/by-user/42"),
                    ("academicRecordsDatabase", "POST", "/api/staff/enroll"),
                    ("academicRecordsDatabase", "PUT", "/api/staff/STF001"),
                    ("academicRecordsDatabase", "PUT", "/api/enrollments/17/grade"),
                    ("courseManagementDatabase", "GET", "/api/courses"),
                    ("courseManagementDatabase", "GET", "/api/faculty-assignments")
                }));
            Assert.That(clients.Requests[3].Body, Is.EqualTo("staff enrollment"));
            Assert.That(clients.Requests[4].Body, Is.EqualTo("staff enrollment"));
            Assert.That(clients.Requests[5].Body, Is.EqualTo("grade update"));
            Assert.That(clients.Requests[5].ContentType, Is.EqualTo("application/json"));
        });
    }

    [Test]
    public async Task GetByStudentIdEscapesIdAndEnrichesEnrollmentsWithCourseMetadata()
    {
        const string profile = """
            {"studentId":"STU 001","enrollments":[{"courseCode":"CS101"},{"courseCode":"UNKNOWN"}]}
            """;
        using var clients = new RecordingHttpClientFactory((name, _, _) => Task.FromResult(name switch
        {
            "academicRecordsDatabase" => JsonResponse(profile),
            "courseManagementDatabase" => JsonResponse("""[{"code":"CS101","name":"Intro to Computing","creditPoints":12,"gradeType":"Mark"}]"""),
            _ => throw new InvalidOperationException($"Unexpected client: {name}")
        }));
        var controller = new StudentsController(clients, new StudentCourseMetadataService(clients));

        var result = await controller.GetByStudentId("STU 001", CancellationToken.None);

        var content = (ContentResult)result;
        var student = JsonDocument.Parse(content.Content!).RootElement;
        var enrollments = student.GetProperty("enrollments");
        Assert.Multiple(() =>
        {
            Assert.That(clients.Requests[0].Path, Is.EqualTo("/api/students/STU%20001"));
            Assert.That(clients.Requests[1].Path, Is.EqualTo("/api/courses"));
            Assert.That(content.StatusCode, Is.EqualTo(200));
            Assert.That(enrollments[0].GetProperty("credits").GetInt32(), Is.EqualTo(12));
            Assert.That(enrollments[0].GetProperty("courseTitle").GetString(), Is.EqualTo("Intro to Computing"));
            Assert.That(enrollments[0].GetProperty("gradeType").GetString(), Is.EqualTo("Mark"));
            Assert.That(enrollments[1].GetProperty("credits").ValueKind, Is.EqualTo(JsonValueKind.Null));
        });
    }

    [Test]
    public async Task GetStudentsForwardsDatabaseFailureWithoutLoadingCourseMetadata()
    {
        using var clients = new RecordingHttpClientFactory((_, _, _) => Task.FromResult(JsonResponse(
            "{\"error\":\"database unavailable\"}", HttpStatusCode.ServiceUnavailable, "application/problem+json")));
        var controller = new StudentsController(clients, new StudentCourseMetadataService(clients));

        var result = (ContentResult)await controller.GetAll(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.StatusCode, Is.EqualTo(503));
            Assert.That(result.Content, Is.EqualTo("{\"error\":\"database unavailable\"}"));
            Assert.That(result.ContentType, Is.EqualTo("application/problem+json"));
            Assert.That(clients.Requests, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task GetWamReturnsStatisticUsingEnrichedCourseCredits()
    {
        const string profile = """
            {"studentId":"STU001","enrollments":[{"courseCode":"CS101","academicYear":2025,"status":"Passed","gradeValue":75,"isFinalized":true}]}
            """;
        using var clients = new RecordingHttpClientFactory((name, _, _) => Task.FromResult(name switch
        {
            "academicRecordsDatabase" => JsonResponse(profile),
            "courseManagementDatabase" => JsonResponse("""[{"code":"CS101","name":"Intro","creditPoints":12,"gradeType":"Mark"}]"""),
            _ => throw new InvalidOperationException($"Unexpected client: {name}")
        }));
        var controller = new StudentAcademicController(
            clients,
            new StudentCourseMetadataService(clients),
            new StubTranscriptPdfGenerator());

        var result = (OkObjectResult)await controller.GetWam("STU001", CancellationToken.None);
        var statistic = (StudentStatisticResponse)result.Value!;

        Assert.Multiple(() =>
        {
            Assert.That(statistic.StudentId, Is.EqualTo("STU001"));
            Assert.That(statistic.Statistic, Is.EqualTo("WAM"));
            Assert.That(statistic.Value, Is.EqualTo(75));
            Assert.That(statistic.GradedEnrollmentCount, Is.EqualTo(1));
            Assert.That(statistic.TotalCredits, Is.EqualTo(12));
        });
    }

    [Test]
    public async Task GetStatisticsForwardsProfileFailureWithoutLoadingCourses()
    {
        using var clients = new RecordingHttpClientFactory((_, _, _) => Task.FromResult(JsonResponse(
            "profile not found", HttpStatusCode.NotFound, "text/plain")));
        var controller = new StudentAcademicController(
            clients,
            new StudentCourseMetadataService(clients),
            new StubTranscriptPdfGenerator());

        var result = (ContentResult)await controller.GetStatistics("STU404", CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.StatusCode, Is.EqualTo(404));
            Assert.That(result.Content, Is.EqualTo("profile not found"));
            Assert.That(result.ContentType, Is.EqualTo("text/plain"));
            Assert.That(clients.Requests, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task GetTranscriptReturnsGeneratedPdfWithStudentSpecificFilename()
    {
        using var clients = new RecordingHttpClientFactory((name, _, _) => Task.FromResult(name switch
        {
            "academicRecordsDatabase" => JsonResponse("""{"studentId":"STU001","firstName":"Ada","lastName":"Lovelace","enrollments":[]}"""),
            "courseManagementDatabase" => JsonResponse("[]"),
            _ => throw new InvalidOperationException($"Unexpected client: {name}")
        }));
        var pdf = new StubTranscriptPdfGenerator();
        var controller = new StudentAcademicController(clients, new StudentCourseMetadataService(clients), pdf);

        var result = (FileContentResult)await controller.GetTranscript("STU001", CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.ContentType, Is.EqualTo("application/pdf"));
            Assert.That(result.FileDownloadName, Is.EqualTo("transcript-STU001.pdf"));
            Assert.That(result.FileContents, Is.EqualTo(pdf.GeneratedBytes));
            Assert.That(pdf.LastTranscript?.StudentId, Is.EqualTo("STU001"));
        });
    }

    [Test]
    public async Task AskRejectsInvalidRequestsWithoutCallingKnowledgeService()
    {
        using var clients = new RecordingHttpClientFactory((_, _, _) => throw new InvalidOperationException("No HTTP call expected."));
        var knowledge = new StubAcademicKnowledgeService((_, _) => throw new InvalidOperationException("No service call expected."));
        var controller = new AiController(clients, null!, null!, knowledge);

        var emptyQuestion = (BadRequestObjectResult)await controller.Ask(
            new KnowledgeQuestionRequest { Question = "  " }, CancellationToken.None);
        var invalidUser = (BadRequestObjectResult)await controller.Ask(
            new KnowledgeQuestionRequest { Question = "When does term start?", UserId = 0 }, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(emptyQuestion.Value, Is.Not.Null);
            Assert.That(invalidUser.Value, Is.Not.Null);
            Assert.That(knowledge.CallCount, Is.Zero);
        });
    }

    [TestCase("invalid-operation", 503)]
    [TestCase("gemini-configuration", 503)]
    [TestCase("gemini-response", 502)]
    [TestCase("mcp", 503)]
    [TestCase("http-request", 503)]
    [TestCase("timeout", 504)]
    public async Task AskMapsKnowledgeServiceFailuresToGatewayResponses(string failure, int expectedStatus)
    {
        using var clients = new RecordingHttpClientFactory((_, _, _) => throw new InvalidOperationException("No HTTP call expected."));
        var knowledge = new StubAcademicKnowledgeService((_, _) => failure switch
        {
            "invalid-operation" => Task.FromException<RagAssistantAnswer>(new InvalidOperationException("RAG is not configured.")),
            "gemini-configuration" => Task.FromException<RagAssistantAnswer>(new AcademicGeminiConfigurationException("Gemini is not configured.")),
            "gemini-response" => Task.FromException<RagAssistantAnswer>(new AcademicGeminiResponseException("Gemini returned an invalid response.")),
            "mcp" => Task.FromException<RagAssistantAnswer>(new McpResponseException("MCP is unavailable.")),
            "http-request" => Task.FromException<RagAssistantAnswer>(new HttpRequestException("RAG unavailable.")),
            "timeout" => Task.FromException<RagAssistantAnswer>(new TaskCanceledException()),
            _ => throw new ArgumentOutOfRangeException(nameof(failure))
        });
        var controller = new AiController(clients, null!, null!, knowledge);

        var result = (ObjectResult)await controller.Ask(
            new KnowledgeQuestionRequest { Question = "When does term start?" }, CancellationToken.None);

        Assert.That(result.StatusCode, Is.EqualTo(expectedStatus));
    }

    private static HttpResponseMessage JsonResponse(
        string content,
        HttpStatusCode statusCode = HttpStatusCode.OK,
        string mediaType = "application/json") => new(statusCode)
    {
        Content = new StringContent(content, Encoding.UTF8, mediaType)
    };

    private static async Task<IActionResult> SendWithBody(ControllerBase controller, string body, Func<Task<IActionResult>> send)
    {
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        controller.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        return await send();
    }

    private sealed class RecordingHttpClientFactory(
        Func<string, HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : IHttpClientFactory, IDisposable
    {
        public List<RecordedRequest> Requests { get; } = [];

        public HttpClient CreateClient(string name) =>
            new(new RecordingHandler(name, Requests, respond)) { BaseAddress = new Uri("http://backend.test") };

        public void Dispose()
        {
        }
    }

    private sealed class RecordingHandler(
        string clientName,
        List<RecordedRequest> requests,
        Func<string, HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            requests.Add(new RecordedRequest(
                clientName,
                request.Method.Method,
                request.RequestUri!.PathAndQuery,
                body,
                request.Content?.Headers.ContentType?.MediaType));
            return await respond(clientName, request, cancellationToken);
        }
    }

    private sealed record RecordedRequest(string ClientName, string Method, string Path, string? Body, string? ContentType);

    private sealed class StubTranscriptPdfGenerator : ITranscriptPdfGenerator
    {
        public byte[] GeneratedBytes { get; } = [1, 2, 3];
        public TranscriptData? LastTranscript { get; private set; }

        public byte[] Generate(TranscriptData transcript)
        {
            LastTranscript = transcript;
            return GeneratedBytes;
        }
    }

    private sealed class StubAcademicKnowledgeService(
        Func<KnowledgeQuestionRequest, CancellationToken, Task<RagAssistantAnswer>> ask) : IAcademicKnowledgeService
    {
        public int CallCount { get; private set; }

        public Task<RagAssistantAnswer> AskAsync(KnowledgeQuestionRequest request, CancellationToken cancellationToken)
        {
            CallCount++;
            return ask(request, cancellationToken);
        }
    }
}
