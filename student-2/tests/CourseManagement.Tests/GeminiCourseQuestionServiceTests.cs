using System.Net;
using System.Text;
using CourseManagement.Backend.Contracts.Ai;
using CourseManagement.Backend.Services;
using Microsoft.Extensions.Configuration;

namespace CourseManagement.Tests;

public sealed class GeminiCourseQuestionServiceTests
{
    [Test]
    public async Task AskReturnsAnswerAndReleaseZeroMetadata()
    {
        const string json = """
            {
              "candidates": [{
                "content": {
                  "parts": [{ "text": "Review the prerequisite Courses first." }]
                }
              }]
            }
            """;
        var handler = new RecordingHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    json,
                    Encoding.UTF8,
                    "application/json")
            });
        var service = CreateService(handler);

        var response = await service.AskAsync(
            Request("What should I learn first?"),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(
                response.Answer,
                Is.EqualTo("Review the prerequisite Courses first."));
            Assert.That(response.Model, Is.EqualTo("gemini-test"));
            Assert.That(response.IsSyllabusGrounded, Is.False);
            Assert.That(handler.RequestBody, Does.Contain("CPAI3001"));
            Assert.That(
                handler.RequestBody,
                Does.Contain("What should I learn first?"));
            Assert.That(
                handler.RequestBody,
                Does.Contain("explicitly selected"));
            Assert.That(
                handler.RequestBody,
                Does.Contain("Credit points: 6"));
            Assert.That(
                handler.RequestBody,
                Does.Contain("not been given the uploaded syllabus PDF"));
            Assert.That(
                handler.RequestBody,
                Does.Contain("Release 1 RAG"));
        });
    }

    [Test]
    public async Task AskTreatsUnselectedCoursesAsReferenceCatalogue()
    {
        const string json = """
            {
              "candidates": [{
                "content": {
                  "parts": [{ "text": "A catalogue-based answer." }]
                }
              }]
            }
            """;
        var handler = new RecordingHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    json,
                    Encoding.UTF8,
                    "application/json")
            });
        var service = CreateService(handler);

        await service.AskAsync(
            Request(
                "What should I know before taking CPAI3001?",
                coursesExplicitlySelected: false),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(
                handler.RequestBody,
                Does.Contain("did not explicitly select any Courses"));
            Assert.That(
                handler.RequestBody,
                Does.Contain("mentions by code or name"));
            Assert.That(
                handler.RequestBody,
                Does.Contain("CPAI3001"));
        });
    }

    [Test]
    public async Task AskSupportsLegacySingleCourseRequest()
    {
        const string json = """
            {
              "candidates": [{
                "content": {
                  "parts": [{ "text": "A legacy Course answer." }]
                }
              }]
            }
            """;
        var handler = new RecordingHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    json,
                    Encoding.UTF8,
                    "application/json")
            });
        var service = CreateService(handler);
        var request = new CourseQuestionRequest
        {
            Question = "What should I understand before taking this Course?",
            CourseCode = "CPAI3001",
            CourseName = "Microservices Architecture"
        };

        await service.AskAsync(request, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(handler.RequestBody, Does.Contain("CPAI3001"));
            Assert.That(
                handler.RequestBody,
                Does.Contain("Microservices Architecture"));
            Assert.That(
                handler.RequestBody,
                Does.Contain("explicitly selected"));
        });
    }

    [Test]
    public void AskRejectsMissingApiKeyBeforeCallingGemini()
    {
        var handler = new RecordingHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK));
        var service = CreateService(handler, apiKey: null);

        Assert.Multiple(() =>
        {
            Assert.That(
                async () => await service.AskAsync(
                    Request("What should I learn first?"),
                    CancellationToken.None),
                Throws.TypeOf<GeminiConfigurationException>());
            Assert.That(handler.CallCount, Is.Zero);
        });
    }

    [Test]
    public void AskRejectsMalformedGeminiResponse()
    {
        var handler = new RecordingHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{not-json",
                    Encoding.UTF8,
                    "application/json")
            });
        var service = CreateService(handler);

        Assert.That(
            async () => await service.AskAsync(
                Request("What should I learn first?"),
                CancellationToken.None),
            Throws.TypeOf<GeminiResponseException>());
    }

    [Test]
    public void AskRejectsEmptyGeminiAnswer()
    {
        var handler = new RecordingHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"candidates":[]}""",
                    Encoding.UTF8,
                    "application/json")
            });
        var service = CreateService(handler);

        Assert.That(
            async () => await service.AskAsync(
                Request("What should I learn first?"),
                CancellationToken.None),
            Throws.TypeOf<GeminiResponseException>());
    }

    static GeminiCourseQuestionService CreateService(
        RecordingHttpMessageHandler handler,
        string? apiKey = "test-key")
    {
        var values = new Dictionary<string, string?>
        {
            ["GEMINI_API_KEY"] = apiKey,
            ["Gemini:Model"] = "gemini-test"
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://gemini.test/")
        };

        return new GeminiCourseQuestionService(
            new StubHttpClientFactory(httpClient),
            configuration);
    }

    static CourseQuestionRequest Request(
        string question,
        bool coursesExplicitlySelected = true) =>
        new()
        {
            Question = question,
            CoursesExplicitlySelected = coursesExplicitlySelected,
            Courses =
            [
                new CourseQuestionContext
                {
                    Code = "CPAI3001",
                    Name = "Microservices Architecture",
                    Description = "Design independently deployable services.",
                    Level = 3,
                    CreditPoints = 6
                }
            ]
        };

    sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    sealed class RecordingHttpMessageHandler(HttpResponseMessage response)
        : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return response;
        }
    }
}
