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
        var controller = CreateController(rag, out _);

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
        var controller = CreateController(rag, out _);

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

    [Test]
    public async Task SuccessfulDatabaseDeleteRemovesSyllabusFromRag()
    {
        var rag = new StubRagServerClient();
        var controller = CreateController(
            rag,
            out var ragHandler,
            HttpStatusCode.NoContent,
            string.Empty);

        var result = await controller.Delete(7, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.TypeOf<NoContentResult>());
            Assert.That(ragHandler.Method, Is.EqualTo(HttpMethod.Delete));
            Assert.That(
                ragHandler.RequestUri?.AbsolutePath,
                Is.EqualTo("/api/rag/coursemanagement/documents/course-7-syllabus"));
        });
    }

    [Test]
    public async Task RagDeleteFailureDoesNotChangeSuccessfulDatabaseResponse()
    {
        var rag = new StubRagServerClient();
        var controller = CreateController(
            rag,
            out _,
            HttpStatusCode.NoContent,
            string.Empty,
            new HttpRequestException("RAG unavailable."));

        var result = await controller.Delete(7, CancellationToken.None);

        Assert.That(result, Is.TypeOf<NoContentResult>());
    }

    [Test]
    public async Task DatabaseUploadFailureReturnsServiceUnavailable()
    {
        var controller = CreateController(
            new StubRagServerClient(),
            out _,
            databaseFailure: new HttpRequestException("Database unavailable."));

        var result = await controller.UploadOrReplace(
            7,
            CreateUpload(CreatePdf("RAG syllabus data.")),
            CancellationToken.None);

        Assert.That(
            ((ObjectResult)result).StatusCode,
            Is.EqualTo(StatusCodes.Status503ServiceUnavailable));
    }

    [Test]
    public async Task DatabaseDeleteFailureReturnsServiceUnavailable()
    {
        var controller = CreateController(
            new StubRagServerClient(),
            out _,
            databaseFailure: new HttpRequestException("Database unavailable."));

        var result = await controller.Delete(7, CancellationToken.None);

        Assert.That(
            ((ObjectResult)result).StatusCode,
            Is.EqualTo(StatusCodes.Status503ServiceUnavailable));
    }

    static SyllabusUploadController CreateController(
        StubRagServerClient rag,
        out StubHttpMessageHandler ragHandler,
        HttpStatusCode statusCode = HttpStatusCode.Created,
        string responseBody =
            """{"courseId":7,"fileName":"Course Outline.pdf","contentType":"application/pdf","sizeBytes":100}""",
        Exception? ragDeleteFailure = null,
        Exception? databaseFailure = null)
    {
        var databaseResponse = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(
                responseBody,
                Encoding.UTF8,
                "application/json")
        };
        var database = new HttpClient(
            new StubHttpMessageHandler(databaseResponse, databaseFailure))
        {
            BaseAddress = new Uri("http://course-database.test/")
        };
        ragHandler = new StubHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"collection":"coursemanagement","documentId":"course-7-syllabus","deleted":true}""",
                    Encoding.UTF8,
                    "application/json")
            },
            ragDeleteFailure);
        var ragDeleteClient = new HttpClient(ragHandler)
        {
            BaseAddress = new Uri("http://rag.test/")
        };

        return new SyllabusUploadController(
            new StubHttpClientFactory(database, ragDeleteClient),
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

    sealed class StubHttpClientFactory(
        HttpClient database,
        HttpClient ragDelete) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            name == SyllabusUploadController.RagClientName
                ? ragDelete
                : database;
    }

    sealed class StubHttpMessageHandler(
        HttpResponseMessage response,
        Exception? exception = null) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            RequestUri = request.RequestUri;

            return exception is null
                ? Task.FromResult(response)
                : Task.FromException<HttpResponseMessage>(exception);
        }
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
