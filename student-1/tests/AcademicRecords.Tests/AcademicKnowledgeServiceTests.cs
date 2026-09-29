using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AcademicRecords.Backend.Contracts;
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
    public async Task AskAsync_ForAnyQuestion_CallsMcpBeforeRagAndPassesEnrollmentContext()
    {
        using var clients = new TestHttpClientFactory(Courses);
        var rag = new CapturingRagServerClient(clients.PipelineOrder);
        var service = CreateService(clients, rag);

        var answer = await service.AskAsync(
            new KnowledgeQuestionRequest { Question = "When does the library close?", UserId = 42 }, CancellationToken.None);

        var arguments = clients.McpRequests[0].GetProperty("arguments");

        Assert.Multiple(() =>
        {
            Assert.That(clients.McpPaths, Is.EqualTo(new[] { "/api/mcp/tools/call", "/api/mcp/tools/call" }));
            Assert.That(clients.McpRequests.Select(item => item.GetProperty("tool").GetString()),
                Is.EqualTo(new[] { "get_student_enrollments", "calculate_student_academic_statistics" }));
            Assert.That(arguments.GetProperty("user_id").GetInt32(), Is.EqualTo(42));
            Assert.That(clients.McpRequests[1].GetProperty("arguments").GetProperty("student_id").GetString(), Is.EqualTo("STU042"));
            Assert.That(clients.PipelineOrder, Is.EqualTo(new[]
            {
                "get_student_enrollments", "calculate_student_academic_statistics", "rag-ingest", "rag-answer"
            }));
            Assert.That(rag.LastIngest!.Documents.Any(document => document.Id.StartsWith("enrollment-", StringComparison.Ordinal)), Is.False);
            Assert.That(rag.LastAnswer!.SupplementalContext, Does.Contain("STU042"));
            Assert.That(rag.LastAnswer.SupplementalContext, Does.Contain("CS101"));
            Assert.That(rag.LastAnswer.SupplementalContext, Does.Contain("academicStatistics"));
            Assert.That(rag.LastAnswer.SupplementalContext, Does.Contain("weightedAverageMark"));
            Assert.That(answer.Sources.Select(source => source.DocumentId), Does.Contain("mcp:get_student_enrollments"));
            Assert.That(answer.Sources.Select(source => source.DocumentId), Does.Contain("mcp:calculate_student_academic_statistics"));
        });
    }

    [Test]
    public async Task AskAsync_WithoutUserId_StillCallsMcpBeforeRagWithoutMcpContext()
    {
        using var clients = new TestHttpClientFactory(Courses);
        var rag = new CapturingRagServerClient(clients.PipelineOrder);
        var service = CreateService(clients, rag);

        await service.AskAsync(new KnowledgeQuestionRequest { Question = "What is enrolment?" }, CancellationToken.None);

        var documents = rag.LastIngest!.Documents;

        Assert.Multiple(() =>
        {
            Assert.That(clients.McpPaths, Is.EqualTo(new[] { "/api/mcp/tools/call" }));
            Assert.That(clients.McpRequests[0].GetProperty("arguments").GetProperty("user_id").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(clients.PipelineOrder, Is.EqualTo(new[] { "get_student_enrollments", "rag-ingest", "rag-answer" }));
            Assert.That(rag.LastAnswer!.SupplementalContext, Is.Null);
            Assert.That(documents.Any(document => document.Id.StartsWith("enrollment-", StringComparison.Ordinal)), Is.False);
            Assert.That(documents.Any(document => document.Id == "policy-enrolment"), Is.True);
            Assert.That(documents.Any(document => document.Id == "course-CS101"), Is.True);
            Assert.That(rag.LastAnswer.Question, Is.EqualTo("What is enrolment?"));
        });
    }

    [Test]
    public async Task AskAsync_WhenMcpToolReturnsAnError_StillCallsRag()
    {
        using var clients = new TestHttpClientFactory(Courses, mcpError: true);
        var rag = new CapturingRagServerClient(clients.PipelineOrder);
        var service = CreateService(clients, rag);

        await service.AskAsync(
            new KnowledgeQuestionRequest { Question = "What courses have I completed?", UserId = 42 }, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(clients.PipelineOrder, Is.EqualTo(new[] { "get_student_enrollments", "rag-ingest", "rag-answer" }));
            Assert.That(rag.LastIngest, Is.Not.Null);
            Assert.That(rag.LastAnswer!.SupplementalContext, Is.Null);
        });
    }

    [Test]
    public async Task AskAsync_WhenMcpIsUnavailable_StillCallsRag()
    {
        using var clients = new TestHttpClientFactory(Courses, mcpUnavailable: true);
        var rag = new CapturingRagServerClient(clients.PipelineOrder);
        var service = CreateService(clients, rag);

        await service.AskAsync(
            new KnowledgeQuestionRequest { Question = "What courses have I completed?", UserId = 42 }, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(clients.PipelineOrder, Is.EqualTo(new[] { "get_student_enrollments", "rag-ingest", "rag-answer" }));
            Assert.That(rag.LastIngest, Is.Not.Null);
            Assert.That(rag.LastAnswer!.SupplementalContext, Is.Null);
        });
    }

    [Test]
    public async Task AskAsync_WhenStatisticsToolReturnsAnError_UsesEnrollmentContextAndCallsRag()
    {
        using var clients = new TestHttpClientFactory(Courses, statisticsMcpError: true);
        var rag = new CapturingRagServerClient(clients.PipelineOrder);
        var service = CreateService(clients, rag);

        var answer = await service.AskAsync(
            new KnowledgeQuestionRequest { Question = "How am I progressing?", UserId = 42 }, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(clients.PipelineOrder, Is.EqualTo(new[]
            {
                "get_student_enrollments", "calculate_student_academic_statistics", "rag-ingest", "rag-answer"
            }));
            Assert.That(rag.LastAnswer!.SupplementalContext, Does.Contain("CS101"));
            Assert.That(rag.LastAnswer.SupplementalContext, Does.Not.Contain("academicStatistics"));
            Assert.That(answer.Sources.Select(source => source.DocumentId), Does.Not.Contain("mcp:calculate_student_academic_statistics"));
        });
    }

    private static AcademicKnowledgeService CreateService(
        IHttpClientFactory clients,
        CapturingRagServerClient rag) =>
        new(clients, new RagKnowledgeService(rag, new RagCitationParser()), NullLogger<AcademicKnowledgeService>.Instance);

    private sealed class CapturingRagServerClient(List<string> pipelineOrder) : IRagServerClient
    {
        public RagIngestRequest? LastIngest { get; private set; }
        public RagAnswerRequest? LastAnswer { get; private set; }

        public Task<RagIngestResponse?> IngestAsync(
            string collection, RagIngestRequest request, CancellationToken cancellationToken)
        {
            pipelineOrder.Add("rag-ingest");
            LastIngest = request;
            return Task.FromResult<RagIngestResponse?>(
                new RagIngestResponse(collection, request.Documents.Count, request.Documents.Count, "test-embedder"));
        }

        public Task<RagAnswerResponse?> AnswerAsync(
            string collection, RagAnswerRequest request, CancellationToken cancellationToken)
        {
            pipelineOrder.Add("rag-answer");
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
        public List<string> PipelineOrder { get; } = [];
        public List<JsonElement> McpRequests { get; } = [];

        public TestHttpClientFactory(
            IReadOnlyList<CourseMetadataResponse> courses,
            bool mcpError = false,
            bool mcpUnavailable = false,
            bool statisticsMcpError = false)
        {
            courseManagementClient = CreateHttpClient(request =>
                request.RequestUri!.AbsolutePath == "/api/courses"
                    ? JsonResponse(courses)
                    : new HttpResponseMessage(HttpStatusCode.NotFound));
            mcpClient = CreateHttpClient(async (request, cancellationToken) =>
            {
                McpPaths.Add(request.RequestUri!.AbsolutePath);
                var mcpRequest = await request.Content!.ReadFromJsonAsync<JsonElement>(cancellationToken);
                McpRequests.Add(mcpRequest);
                var tool = mcpRequest.GetProperty("tool").GetString()!;
                PipelineOrder.Add(tool);
                if (mcpUnavailable)
                {
                    return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                }

                if (tool == "get_student_enrollments")
                {
                    var hasUserId = mcpRequest.GetProperty("arguments").GetProperty("user_id").ValueKind == JsonValueKind.Number;
                    if (mcpError || !hasUserId)
                    {
                        return JsonResponse(new
                        {
                            tool,
                            isError = true,
                            result = new { error = "A positive user_id argument is required." }
                        });
                    }

                    return JsonResponse(new
                    {
                        tool,
                        isError = false,
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
                        }});
                }

                if (tool == "calculate_student_academic_statistics")
                {
                    if (statisticsMcpError)
                    {
                        return JsonResponse(new
                        {
                            tool,
                            isError = true,
                            result = new { error = "Statistics are unavailable." }
                        });
                    }

                    return JsonResponse(new
                    {
                        tool,
                        isError = false,
                        result = new
                        {
                            weightedAverageMark = 82.0m,
                            gradePointAverage = 6.0m,
                            totalCreditPointsGained = 12
                        }
                    });
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
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
