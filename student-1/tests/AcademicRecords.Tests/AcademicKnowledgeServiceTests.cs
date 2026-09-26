using System.Net;
using System.Net.Http.Json;
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

    private static readonly StudentEnrollmentProfile StudentProfile = new(
        "STU042",
        [new StudentEnrollmentResponse(321, "CS101", "2026-T1", "Term 1", 2026, "Passed", 82m, true)]);

    [Test]
    public async Task AskAsync_WithUserId_IncludesEnrolmentEnrichedWithCourseMetadata()
    {
        using var clients = new TestHttpClientFactory(Courses, StudentProfile);
        var rag = new CapturingRagServerClient();
        var service = CreateService(clients, rag);

        await service.AskAsync(new KnowledgeQuestionRequest { Question = "What courses have I completed?", UserId = 42 }, CancellationToken.None);

        var documents = rag.LastIngest!.Documents;
        var enrollment = documents.Single(document => document.Id == "enrollment-321");

        Assert.Multiple(() =>
        {
            Assert.That(clients.AcademicRecordsPaths, Is.EqualTo(new[] { "/api/students/by-user/42" }));
            Assert.That(documents.Any(document => document.Id == "policy-enrolment"), Is.True);
            Assert.That(documents.Any(document => document.Id == "course-CS101"), Is.True);
            Assert.That(enrollment.Text, Does.Contain("STU042"));
            Assert.That(enrollment.Text, Does.Contain("Introduction to Computing (CS101)"));
            Assert.That(enrollment.Text, Does.Contain("Term 1 2026"));
            Assert.That(enrollment.Text, Does.Contain("Current status: Passed"));
            Assert.That(enrollment.Text, Does.Contain("Finalized."));
            Assert.That(enrollment.Text, Does.Contain("12 credit points"));
            Assert.That(enrollment.Text, Does.Contain("Recorded mark 82"));
            Assert.That(enrollment.Metadata!["studentId"], Is.EqualTo("STU042"));
            Assert.That(rag.LastAnswer!.Question, Is.EqualTo("What courses have I completed?"));
        });
    }

    [Test]
    public async Task AskAsync_WithoutUserId_DoesNotFetchOrIncludeStudentEnrollments()
    {
        using var clients = new TestHttpClientFactory(Courses, StudentProfile);
        var rag = new CapturingRagServerClient();
        var service = CreateService(clients, rag);

        await service.AskAsync(new KnowledgeQuestionRequest { Question = "What is enrolment?" }, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(clients.AcademicRecordsPaths, Is.Empty);
            Assert.That(rag.LastIngest!.Documents.Any(document => document.Id.StartsWith("enrollment-", StringComparison.Ordinal)), Is.False);
            Assert.That(rag.LastIngest!.Documents.Any(document => document.Id == "policy-enrolment"), Is.True);
        });
    }

    [Test]
    public async Task AskAsync_WhenStudentProfileIsUnavailable_StillIngestsPolicyAndCatalogueDocuments()
    {
        using var clients = new TestHttpClientFactory(Courses, null);
        var rag = new CapturingRagServerClient();
        var service = CreateService(clients, rag);

        await service.AskAsync(new KnowledgeQuestionRequest { Question = "What courses do I have?", UserId = 42 }, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(clients.AcademicRecordsPaths, Is.EqualTo(new[] { "/api/students/by-user/42" }));
            Assert.That(rag.LastIngest!.Documents.Any(document => document.Id == "policy-enrolment"), Is.True);
            Assert.That(rag.LastIngest!.Documents.Any(document => document.Id == "course-CS101"), Is.True);
            Assert.That(rag.LastIngest!.Documents.Any(document => document.Id.StartsWith("enrollment-", StringComparison.Ordinal)), Is.False);
        });
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
        private readonly HttpClient academicRecordsClient;
        private readonly StudentEnrollmentProfile? profile;

        public List<string> AcademicRecordsPaths { get; } = [];

        public TestHttpClientFactory(
            IReadOnlyList<CourseMetadataResponse> courses,
            StudentEnrollmentProfile? profile)
        {
            this.profile = profile;
            courseManagementClient = CreateHttpClient(request =>
                request.RequestUri!.AbsolutePath == "/api/courses"
                    ? JsonResponse(courses)
                    : new HttpResponseMessage(HttpStatusCode.NotFound));
            academicRecordsClient = CreateHttpClient(request =>
            {
                var path = request.RequestUri!.AbsolutePath;
                AcademicRecordsPaths.Add(path);
                return path.StartsWith("/api/students/by-user/", StringComparison.Ordinal) && this.profile is not null
                    ? JsonResponse(this.profile)
                    : new HttpResponseMessage(HttpStatusCode.NotFound);
            });
        }

        public HttpClient CreateClient(string name) => name switch
        {
            "courseManagementDatabase" => courseManagementClient,
            "academicRecordsDatabase" => academicRecordsClient,
            _ => throw new ArgumentException($"Unexpected HttpClient name: {name}", nameof(name))
        };

        public void Dispose()
        {
            courseManagementClient.Dispose();
            academicRecordsClient.Dispose();
        }

        private static HttpClient CreateHttpClient(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
            new(new StubHttpMessageHandler(responder)) { BaseAddress = new Uri("http://localhost/") };

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
}
