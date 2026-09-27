using System.Net;
using System.Text;
using System.Text.Json;
using CampusPulse.Shared.Mcp.Client;
using CampusPulse.Shared.Mcp.Contracts;
using CampusPulse.Shared.Rag.Assistant;
using CourseManagement.Backend.Contracts.Ai;
using CourseManagement.Backend.Services;
using Microsoft.Extensions.Configuration;

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

    static LocalCourseAiOrchestrator CreateService(
        HttpMessageHandler gemini,
        IMcpClient mcp,
        ICourseRagService rag)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GEMINI_API_KEY"] = "test-key",
                ["Gemini:Model"] = "gemini-test"
            })
            .Build();
        var client = new HttpClient(gemini)
        {
            BaseAddress = new Uri("https://gemini.test/")
        };

        return new LocalCourseAiOrchestrator(
            new StubHttpClientFactory(client),
            configuration,
            mcp,
            rag);
    }

    static KnowledgeQuestionRequest Request(string question) =>
        new() { Question = question };

    static HttpResponseMessage GeminiResponse(string text)
    {
        var json = JsonSerializer.Serialize(new
        {
            candidates = new[]
            {
                new
                {
                    content = new
                    {
                        parts = new[] { new { text } }
                    }
                }
            }
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

    sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    sealed class QueuedHttpMessageHandler(
        params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        readonly Queue<HttpResponseMessage> responses = new(responses);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responses.Dequeue());
    }
}
