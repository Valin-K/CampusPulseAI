using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AcademicRecords.Backend.Models;
using AcademicRecords.Backend.Services;
using CampusPulse.Shared.Rag.Assistant;
using CampusPulse.Shared.Rag.Client;
using CampusPulse.Shared.Rag.Contracts;
using CampusPulse.Shared.Rag.Generation;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace AcademicRecords.Tests;

public sealed class AcademicKnowledgeServiceTests
{
    private static readonly IReadOnlyList<CourseMetadataResponse> Courses =
    [
        new("CS101", "Introduction to Computing", 12, "Mark")
    ];

    [Test]
    public async Task AskAsync_WithPersonalEnrollmentQuestion_UsesMcpWithoutIngestingEnrollmentDocuments()
    {
        using var clients = new TestHttpClientFactory(Courses);
        var rag = new CapturingRagServerClient();
        var service = CreateService(clients, rag);

        var answer = await service.AskAsync(
            new KnowledgeQuestionRequest { Question = "What courses have I completed?", UserId = 42 }, CancellationToken.None);

        var arguments = clients.McpRequest!.Value.GetProperty("arguments");

        Assert.Multiple(() =>
        {
            Assert.That(clients.McpPaths, Is.EqualTo(new[] { "/api/mcp/tools/call" }));
            Assert.That(clients.McpRequest!.Value.GetProperty("tool").GetString(), Is.EqualTo("get_student_enrollments"));
            Assert.That(arguments.GetProperty("user_id").GetInt32(), Is.EqualTo(42));
            Assert.That(answer.Reply, Does.Contain("CS101"));
            Assert.That(answer.Reply, Does.Contain("mark 82"));
            Assert.That(answer.Sources.Single().DocumentId, Is.EqualTo("mcp:get_student_enrollments"));
            Assert.That(rag.LastIngest, Is.Null);
        });
    }

    [Test]
    public async Task AskAsync_WithGeneralQuestion_UsesRagWithoutStudentEnrollmentDocuments()
    {
        using var clients = new TestHttpClientFactory(Courses);
        var rag = new CapturingRagServerClient();
        var service = CreateService(clients, rag);

        await service.AskAsync(new KnowledgeQuestionRequest { Question = "What is enrolment?", UserId = 42 }, CancellationToken.None);

        var documents = rag.LastIngest!.Documents;

        Assert.Multiple(() =>
        {
            Assert.That(clients.McpPaths, Is.Empty);
            Assert.That(documents.Any(document => document.Id.StartsWith("enrollment-", StringComparison.Ordinal)), Is.False);
            Assert.That(documents.Any(document => document.Id == "policy-enrolment"), Is.True);
            Assert.That(documents.Any(document => document.Id == "course-CS101"), Is.True);
            Assert.That(rag.LastAnswer!.Question, Is.EqualTo("What is enrolment?"));
        });
    }

    [Test]
    public async Task AskAsync_WithPersonalEnrollmentQuestionAndNoUserId_RequestsStudentContext()
    {
        using var clients = new TestHttpClientFactory(Courses);
        var rag = new CapturingRagServerClient();
        var service = CreateService(clients, rag);

        var answer = await service.AskAsync(
            new KnowledgeQuestionRequest { Question = "What courses have I completed?" }, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(answer.Status, Is.EqualTo("insufficient_context"));
            Assert.That(answer.Reply, Does.Contain("student profile"));
            Assert.That(clients.McpPaths, Is.Empty);
            Assert.That(rag.LastIngest, Is.Null);
        });
    }

    [Test]
    public void AskAsync_WhenMcpToolReturnsAnError_DoesNotFallBackToRag()
    {
        using var clients = new TestHttpClientFactory(Courses, mcpError: true);
        var rag = new CapturingRagServerClient();
        var service = CreateService(clients, rag);

        Assert.ThrowsAsync<InvalidOperationException>(() => service.AskAsync(
            new KnowledgeQuestionRequest { Question = "What courses have I completed?", UserId = 42 }, CancellationToken.None));
        Assert.That(rag.LastIngest, Is.Null);
    }

    private static AcademicKnowledgeService CreateService(
        IHttpClientFactory clients,
        CapturingRagServerClient rag) =>
        new(clients, new RagKnowledgeService(rag, new RagCitationParser()), NullLogger<AcademicKnowledgeService>.Instance);

    private sealed class CapturingRagServerClient : IRagServerClient
    {
        public RagIngestRequest? LastIngest { get; private set; }
        public RagAnswerRequest? LastAnswer { get; private set; }

        public Task<RagIngestResponse?> IngestAsync(
            string collection, RagIngestRequest request, CancellationToken cancellationToken)
        {
            LastIngest = request;
            return Task.FromResult<RagIngestResponse?>(
                new RagIngestResponse(collection, request.Documents.Count, request.Documents.Count, "test-embedder"));
        }

        public Task<RagAnswerResponse?> AnswerAsync(
            string collection, RagAnswerRequest request, CancellationToken cancellationToken)
        {
            LastAnswer = request;
            return Task.FromResult<RagAnswerResponse?>(new RagAnswerResponse(
                collection, request.Question, RagStatus.GenerationUnavailable, null, 0, [], "test-embedder", null,
                DateTimeOffset.UtcNow));
        }
    }

    private sealed class TestHttpClientFactory : IHttpClientFactory, IDisposable
    {
        private readonly HttpClient courseManagementClient;
        private readonly HttpClient mcpClient;

        public List<string> McpPaths { get; } = [];
        public JsonElement? McpRequest { get; private set; }

        public TestHttpClientFactory(
            IReadOnlyList<CourseMetadataResponse> courses,
            bool mcpError = false)
        {
            courseManagementClient = CreateHttpClient(request =>
                request.RequestUri!.AbsolutePath == "/api/courses"
                    ? JsonResponse(courses)
                    : new HttpResponseMessage(HttpStatusCode.NotFound));
            mcpClient = CreateHttpClient(async (request, cancellationToken) =>
            {
                McpPaths.Add(request.RequestUri!.AbsolutePath);
                McpRequest = await request.Content!.ReadFromJsonAsync<JsonElement>(cancellationToken);
                return JsonResponse(new
                {
                    tool = "get_student_enrollments",
                    isError = mcpError,
                    result = new
                    {
                        studentId = "STU042",
                        enrollments = new[]
                        {
                            new
                            {
                                courseCode = "CS101",
                                termCode = "2026-T1",
                                termName = "Term 1",
                                academicYear = 2026,
                                status = "Passed",
                                gradeValue = 82m,
                                isFinalized = true
                            }
                        }
                    }
                });
            });
        }

        public HttpClient CreateClient(string name) => name switch
        {
            "courseManagementDatabase" => courseManagementClient,
            "mcp" => mcpClient,
            _ => throw new ArgumentException($"Unexpected HttpClient name: {name}", nameof(name))
        };

        public void Dispose()
        {
            courseManagementClient.Dispose();
            mcpClient.Dispose();
        }

        private static HttpClient CreateHttpClient(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
            new(new StubHttpMessageHandler(responder)) { BaseAddress = new Uri("http://localhost/") };

        private static HttpClient CreateHttpClient(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) =>
            new(new AsyncStubHttpMessageHandler(responder)) { BaseAddress = new Uri("http://localhost/") };

        private static HttpResponseMessage JsonResponse<T>(T value) =>
            new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(responder(request));
        }
    }

    private sealed class AsyncStubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            responder(request, cancellationToken);
    }
}
