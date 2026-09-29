using System.Net;
using System.Text;
using CourseManagement.Backend.Contracts.Ai;
using CourseManagement.Backend.Services;
using CampusPulse.Shared.Ai;
using System.Text.Json;

namespace CourseManagement.Tests;

public sealed class GeminiCourseQuestionServiceTests
{
    [Test]
    public async Task AskReturnsAnswerAndReleaseZeroMetadata()
    {
        const string json = """
            {
              "text": "Review the prerequisite Courses first.",
              "model": "gateway-test"
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
            Assert.That(response.Model, Is.EqualTo("gateway-test"));
            using var body = JsonDocument.Parse(handler.RequestBody!);
            Assert.That(body.RootElement.GetProperty("prompt").GetString(), Does.Not.Contain("CPAI3001"));
            Assert.That(body.RootElement.GetProperty("context").GetString(), Does.Contain("CPAI3001"));
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
              "text": "A catalogue-based answer.",
              "model": "gateway-test"
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
              "text": "A legacy Course answer.",
              "model": "gateway-test"
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
    public void AskPropagatesGatewayFailures()
    {
        var handler = new RecordingHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var service = CreateService(handler);

        Assert.Multiple(() =>
        {
            Assert.That(
                async () => await service.AskAsync(
                    Request("What should I learn first?"),
                    CancellationToken.None),
                Throws.TypeOf<HttpRequestException>());
            Assert.That(handler.CallCount, Is.EqualTo(1));
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
            Throws.TypeOf<HttpRequestException>());
    }

    [Test]
    public void AskRejectsEmptyGeminiAnswer()
    {
        var handler = new RecordingHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"text":"","model":"gateway-test"}""",
                    Encoding.UTF8,
                    "application/json")
            });
        var service = CreateService(handler);

        Assert.That(
            async () => await service.AskAsync(
                Request("What should I learn first?"),
                CancellationToken.None),
            Throws.TypeOf<HttpRequestException>());
    }

    [Test]
    public void AskPropagatesCallerCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var service = CreateService(new RecordingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)));
        Assert.That(async () => await service.AskAsync(Request("Question"), cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }

    static GeminiCourseQuestionService CreateService(
        RecordingHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://gateway.test/")
        };

        return new GeminiCourseQuestionService(new AiGatewayClient(httpClient));
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
