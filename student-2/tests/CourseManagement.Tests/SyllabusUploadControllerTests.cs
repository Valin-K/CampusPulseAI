using System.Net;
using System.Text;
using CampusPulse.Shared.Rag.Client;
using CampusPulse.Shared.Rag.Contracts;
using CourseManagement.Backend.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace CourseManagement.Tests;

public sealed class SyllabusUploadControllerTests
{
    [Test]
    public async Task SuccessfulDatabaseUploadIngestsPdfText()
    {
        var rag = new StubRagServerClient();
        var controller = CreateController(rag);

        var result = await controller.UploadOrReplace(
            7,
            CreateUpload(CreatePdf("RAG syllabus data.")),
            CancellationToken.None);

        var document = rag.LastRequest?.Documents.Single();
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.TypeOf<ContentResult>());
            Assert.That(((ContentResult)result).StatusCode, Is.EqualTo(StatusCodes.Status201Created));
            Assert.That(rag.LastCollection, Is.EqualTo("coursemanagement"));
            Assert.That(document?.Id, Is.EqualTo("course-7-syllabus"));
            Assert.That(document?.Text, Does.Contain("RAG syllabus data."));
        });
    }

    [Test]
    public async Task RagFailureDoesNotChangeSuccessfulUploadResponse()
    {
        var rag = new StubRagServerClient
        {
            IngestException = new HttpRequestException("RAG unavailable.")
        };
        var controller = CreateController(rag);

        var result = await controller.UploadOrReplace(
            7,
            CreateUpload(CreatePdf("RAG syllabus data.")),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.TypeOf<ContentResult>());
            Assert.That(((ContentResult)result).StatusCode, Is.EqualTo(StatusCodes.Status201Created));
            Assert.That(((ContentResult)result).Content, Does.Contain("Course Outline.pdf"));
        });
    }

    static SyllabusUploadController CreateController(StubRagServerClient rag)
    {
        var databaseResponse = new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent(
                """{"courseId":7,"fileName":"Course Outline.pdf","contentType":"application/pdf","sizeBytes":100}""",
                Encoding.UTF8,
                "application/json")
        };
        var database = new HttpClient(new StubHttpMessageHandler(databaseResponse))
        {
            BaseAddress = new Uri("http://course-database.test/")
        };

        return new SyllabusUploadController(
            new StubHttpClientFactory(database),
            rag,
            NullLogger<SyllabusUploadController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    static IFormFile CreateUpload(byte[] content)
    {
        var stream = new MemoryStream(content);
        return new FormFile(stream, 0, content.Length, "file", "Course Outline.pdf")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf"
        };
    }

    static byte[] CreatePdf(string content)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        return Document.Create(document => document.Page(page =>
        {
            page.Content().Text(content);
        })).GeneratePdf();
    }

    sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    sealed class StubHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(response);
    }

    sealed class StubRagServerClient : IRagServerClient
    {
        public Exception? IngestException { get; init; }
        public string? LastCollection { get; private set; }
        public RagIngestRequest? LastRequest { get; private set; }

        public Task<RagIngestResponse?> IngestAsync(
            string collection,
            RagIngestRequest request,
            CancellationToken cancellationToken)
        {
            if (IngestException is not null)
            {
                return Task.FromException<RagIngestResponse?>(IngestException);
            }

            LastCollection = collection;
            LastRequest = request;
            return Task.FromResult<RagIngestResponse?>(
                new(collection, request.Documents.Count, request.Documents.Count, "test"));
        }

        public Task<RagAnswerResponse?> AnswerAsync(
            string collection,
            RagAnswerRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
