using System.Net;
using System.Net.Http.Json;
using AcademicRecords.Backend.Contracts;
using AcademicRecords.Backend.Models;
using AcademicRecords.Backend.Services;
using CampusPulse.Shared.Rag.Assistant;
using CampusPulse.Shared.Rag.Client;
using CampusPulse.Shared.Rag.Contracts;
using CampusPulse.Shared.Rag.Generation;
using Microsoft.Extensions.Logging.Abstractions;

namespace AcademicRecords.Tests;

public sealed class AcademicKnowledgeServiceTests
{
    [Test]
    public async Task AskAsyncBuildsAcademicCorpusAndDoesNotAttachMcpContext()
    {
        using var clients = new TestHttpClientFactory();
        var ragClient = new CapturingRagServerClient();
        var rag = new RagKnowledgeService(ragClient, new RagCitationParser());
        var service = new AcademicKnowledgeService(
            clients,
            rag,
            NullLogger<AcademicKnowledgeService>.Instance);

        await service.AskAsync(
            new KnowledgeQuestionRequest { Question = "What is enrolment?", UserId = 42 },
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(ragClient.LastIngest!.Documents.Any(document => document.Id == "policy-enrolment"), Is.True);
            Assert.That(ragClient.LastIngest.Documents.Any(document => document.Id == "course-CS101"), Is.True);
            Assert.That(ragClient.LastAnswer!.Question, Is.EqualTo("What is enrolment?"));
            Assert.That(ragClient.LastAnswer.SupplementalContext, Is.Null);
        });
    }

    private sealed class CapturingRagServerClient : IRagServerClient
    {
        public RagIngestRequest? LastIngest { get; private set; }
        public RagAnswerRequest? LastAnswer { get; private set; }

        public Task<RagIngestResponse?> IngestAsync(
            string collection, RagIngestRequest request, CancellationToken cancellationToken)
        {
            LastIngest = request;
            return Task.FromResult<RagIngestResponse?>(new RagIngestResponse(
                collection, request.Documents.Count, request.Documents.Count, "test-embedder"));
        }

        public Task<RagAnswerResponse?> AnswerAsync(
            string collection, RagAnswerRequest request, CancellationToken cancellationToken)
        {
            LastAnswer = request;
            return Task.FromResult<RagAnswerResponse?>(new RagAnswerResponse(
                collection, request.Question, RagStatus.GenerationUnavailable, null, 0, [],
                "test-embedder", null, DateTimeOffset.UtcNow));
        }
    }

    private sealed class TestHttpClientFactory : IHttpClientFactory, IDisposable
    {
        private readonly HttpClient courseClient = new(new CourseHandler())
        {
            BaseAddress = new Uri("http://database.test/")
        };

        public HttpClient CreateClient(string name) => name == "courseManagementDatabase"
            ? courseClient
            : throw new ArgumentException($"Unexpected client name: {name}", nameof(name));

        public void Dispose() => courseClient.Dispose();
    }

    private sealed class CourseHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var response = request.RequestUri!.AbsolutePath == "/api/courses"
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new[]
                    {
                        new CourseMetadataResponse("CS101", "Introduction to Computing", 12, "Mark")
                    })
                }
                : new HttpResponseMessage(HttpStatusCode.NotFound);
            return Task.FromResult(response);
        }
    }
}
