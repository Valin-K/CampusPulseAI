using System.Net;
using System.Text;
using System.Text.Json;
using CampusPulse.Shared.Mcp.Client;
using CampusPulse.Shared.Mcp.Contracts;
using CampusPulse.Shared.Rag.Assistant;
using CourseManagement.Backend.Contracts.Ai;
using CourseManagement.Backend.Services;
using CampusPulse.Shared.Ai;

namespace CourseManagement.Tests;

public sealed class LocalCourseAiOrchestratorTests
{
    [Test]
    public async Task AskExecutesSelectedMcpToolAndReturnsSource()
    {
        var gemini = new QueuedHttpMessageHandler(
            GeminiResponse("""
                {
                  "route": "mcp",
                  "ragQuestion": null,
                  "mcpCalls": [{
                    "tool": "course.list_courses",
                    "arguments": {}
                  }]
                }
                """),
            GeminiResponse("There are ten Courses available."));
        var mcp = new StubMcpClient();
        var rag = new StubCourseRagService();
        var service = CreateService(gemini, mcp, rag);

        var response = await service.AskAsync(
            Request("Which Courses are available?"),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(response.Reply, Is.EqualTo("There are ten Courses available."));
            Assert.That(response.Embedder, Is.EqualTo("gateway-test"));
            using var plan = JsonDocument.Parse(gemini.RequestBodies[0]);
            using var answer = JsonDocument.Parse(gemini.RequestBodies[1]);
            Assert.That(plan.RootElement.GetProperty("prompt").GetString(), Does.Not.Contain("Which Courses are available?"));
            Assert.That(plan.RootElement.GetProperty("context").GetString(), Does.Contain("Which Courses are available?"));
            Assert.That(answer.RootElement.GetProperty("context").GetString(), Does.Contain("CPAI1001"));
            Assert.That(answer.RootElement.GetProperty("prompt").GetString(), Does.Not.Contain("CPAI1001"));
            Assert.That(mcp.CalledTools, Is.EqualTo(new[] { "course.list_courses" }));
            Assert.That(rag.CallCount, Is.Zero);
            Assert.That(response.Sources, Has.Count.EqualTo(1));
            Assert.That(
                response.Sources[0].DocumentId,
                Is.EqualTo("mcp:course.list_courses"));
        });
    }

    [Test]
    public async Task AskCanCombineRagAndMcp()
    {
        var gemini = new QueuedHttpMessageHandler(
            GeminiResponse("""
                {
                  "route": "rag_and_mcp",
                  "ragQuestion": "When is Assignment 1 due?",
                  "mcpCalls": [{
                    "tool": "course.get_details",
                    "arguments": { "courseId": 1 }
                  }]
                }
                """),
            GeminiResponse("Assignment 1 is due at the end of Week 5."));
        var mcp = new StubMcpClient();
        var rag = new StubCourseRagService();
        var service = CreateService(gemini, mcp, rag);

        var response = await service.AskAsync(
            Request("For Programming Fundamentals, when is Assignment 1 due?"),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(rag.CallCount, Is.EqualTo(1));
            Assert.That(mcp.CalledTools, Is.EqualTo(new[] { "course.get_details" }));
            Assert.That(response.Sources, Has.Count.EqualTo(2));
            Assert.That(response.Confidence, Is.EqualTo(0.8));
        });
    }

    [Test]
    public void AskRejectsToolOutsideCourseAllowlist()
    {
        var gemini = new QueuedHttpMessageHandler(
            GeminiResponse("""
                {
                  "route": "mcp",
                  "ragQuestion": null,
                  "mcpCalls": [{
                    "tool": "project_files",
                    "arguments": {}
                  }]
                }
                """));
        var mcp = new StubMcpClient();
        var service = CreateService(
            gemini,
            mcp,
            new StubCourseRagService());

        Assert.Multiple(() =>
        {
            Assert.That(
                async () => await service.AskAsync(
                    Request("List repository files."),
                    CancellationToken.None),
                Throws.TypeOf<GeminiResponseException>());
            Assert.That(mcp.CalledTools, Is.Empty);
        });
    }

    [Test]
    public void AskPropagatesGatewayFailureWithoutCallingTools()
    {
        var mcp = new StubMcpClient();
        var service = CreateService(new QueuedHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)),
            mcp, new StubCourseRagService());

        Assert.That(async () => await service.AskAsync(Request("Question"), CancellationToken.None),
            Throws.TypeOf<HttpRequestException>());
        Assert.That(mcp.CalledTools, Is.Empty);
    }

    [Test]
    public void AskPropagatesCallerCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var service = CreateService(new QueuedHttpMessageHandler(), new StubMcpClient(), new StubCourseRagService());

        Assert.That(async () => await service.AskAsync(Request("Question"), cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }

    static LocalCourseAiOrchestrator CreateService(
        HttpMessageHandler gemini,
        IMcpClient mcp,
        ICourseRagService rag)
    {
        var client = new HttpClient(gemini)
        {
            BaseAddress = new Uri("https://gateway.test/")
        };

        return new LocalCourseAiOrchestrator(
            new AiGatewayClient(client),
            mcp,
            rag);
    }

    static KnowledgeQuestionRequest Request(string question) =>
        new() { Question = question };

    static HttpResponseMessage GeminiResponse(string text)
    {
        var json = JsonSerializer.Serialize(new
        {
            text, model = "gateway-test"
        });
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                json,
                Encoding.UTF8,
                "application/json")
        };
    }

    sealed class StubMcpClient : IMcpClient
    {
        public List<string> CalledTools { get; } = [];

        public Task<IReadOnlyList<McpToolDefinition>> GetToolsAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<McpToolDefinition>>(
            [
                new(
                    "course.list_courses",
                    "List Courses.",
                    new Dictionary<string, string>()),
                new(
                    "course.get_details",
                    "Get Course details.",
                    new Dictionary<string, string>
                    {
                        ["courseId"] = "Required positive Course ID."
                    })
            ]);

        public Task<McpToolCallResponse> CallAsync(
            string tool,
            IReadOnlyDictionary<string, JsonElement> arguments,
            CancellationToken cancellationToken)
        {
            CalledTools.Add(tool);
            return Task.FromResult(new McpToolCallResponse(
                tool,
                IsError: false,
                JsonSerializer.SerializeToElement(new
                {
                    id = 1,
                    code = "CPAI1001",
                    name = "Programming Fundamentals"
                }),
                DateTimeOffset.UtcNow));
        }
    }

    sealed class StubCourseRagService : ICourseRagService
    {
        public int CallCount { get; private set; }

        public Task<RagAssistantAnswer> AskAsync(
            KnowledgeQuestionRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new RagAssistantAnswer(
                request.Question,
                "answered",
                "Assignment 1 is due at the end of Week 5 [1].",
                0.8,
                RagConfidenceCategory.High,
                [
                    new RagKnowledgeSource(
                        1,
                        "Programming Fundamentals syllabus",
                        "course-1-syllabus",
                        0.8)
                ],
                "test-embedder"));
        }
    }

    sealed class QueuedHttpMessageHandler(
        params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        readonly Queue<HttpResponseMessage> responses = new(responses);
        public List<string> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestBodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return responses.Dequeue();
        }
    }
}
