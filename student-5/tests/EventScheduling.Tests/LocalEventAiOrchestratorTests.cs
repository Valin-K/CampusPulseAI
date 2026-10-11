using System.Net;
using System.Text;
using System.Text.Json;
using CampusPulse.Shared.Mcp.Client;
using CampusPulse.Shared.Mcp.Contracts;
using CampusPulse.Shared.Rag.Assistant;
using EventScheduling.Backend.Models;
using EventScheduling.Backend.Services;
using CampusPulse.Shared.Ai;
using Microsoft.Extensions.Configuration;

namespace EventScheduling.Tests;

public sealed class LocalEventAiOrchestratorTests
{
    [Test]
    public async Task AskExecutesMcpIntegrationToolAndReturnsSource()
    {
        var gemini = new QueuedHttpMessageHandler(
            GeminiResponse("""{"route":"tools","ragQuestion":null,"toolCalls":[{"tool":"check_mcp_integration","arguments":{}}]}"""),
            GeminiResponse("The Event Scheduling feature is integrated with the MCP server."));
        var mcp = new StubMcpClient();
        var rag = new StubEventRagService();
        var service = CreateService(gemini, mcp, rag, database: null);

        var response = await service.AskAsync(Request("Is the MCP integrated?"), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(response.Reply, Is.EqualTo("The Event Scheduling feature is integrated with the MCP server."));
            Assert.That(response.Embedder, Is.EqualTo("gateway-test"));
            using var plan = JsonDocument.Parse(gemini.RequestBodies[0]);
            using var answer = JsonDocument.Parse(gemini.RequestBodies[1]);
            Assert.That(plan.RootElement.GetProperty("prompt").GetString(), Does.Not.Contain("Is the MCP integrated?"));
            Assert.That(plan.RootElement.GetProperty("context").GetString(), Does.Contain("Is the MCP integrated?"));
            Assert.That(answer.RootElement.GetProperty("context").GetString(), Does.Contain("feature-allowlisted"));
            Assert.That(answer.RootElement.GetProperty("prompt").GetString(), Does.Not.Contain("feature-allowlisted"));
            Assert.That(mcp.CalledTools, Is.EqualTo(new[] { "validate_integration" }));
            Assert.That(rag.CallCount, Is.Zero);
            Assert.That(response.Sources, Has.Count.EqualTo(1));
            Assert.That(response.Sources[0].DocumentId, Is.EqualTo("tool:check_mcp_integration"));
            // No RAG leg → no confidence category (deterministic MCP lookup).
            Assert.That(response.ConfidenceCategory, Is.Empty);
        });
    }

    [Test]
    public async Task AskExecutesDatabaseToolAgainstTheEventDatabase()
    {
        var gemini = new QueuedHttpMessageHandler(
            GeminiResponse("""{"route":"tools","ragQuestion":null,"toolCalls":[{"tool":"list_rooms","arguments":{}}]}"""),
            GeminiResponse("There is one study room."));
        var database = new RecordingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""[{"id":1,"name":"Study Room 101"}]""", Encoding.UTF8, "application/json")
        });
        var rag = new StubEventRagService();
        var service = CreateService(gemini, new StubMcpClient(), rag, database);

        var response = await service.AskAsync(Request("What rooms are there?"), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(database.RequestedPaths, Is.EqualTo(new[] { "/api/rooms" }));
            Assert.That(rag.CallCount, Is.Zero);
            Assert.That(response.Sources[0].DocumentId, Is.EqualTo("tool:list_rooms"));
            // No RAG leg → no confidence category (deterministic DB lookup).
            Assert.That(response.ConfidenceCategory, Is.Empty);
        });
    }

    [Test]
    public async Task AskCombinesRagAndTools()
    {
        var gemini = new QueuedHttpMessageHandler(
            GeminiResponse("""{"route":"rag_and_tools","ragQuestion":"What are the hire rules?","toolCalls":[{"tool":"list_equipment","arguments":{}}]}"""),
            GeminiResponse("Equipment hire lasts seven days; three tripods are available."));
        var database = new RecordingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""[{"id":5,"name":"Camera Tripod","availableCount":3}]""", Encoding.UTF8, "application/json")
        });
        var rag = new StubEventRagService();
        var service = CreateService(gemini, new StubMcpClient(), rag, database);

        var response = await service.AskAsync(
            Request("How long can I hire a tripod and how many are free?"), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(rag.CallCount, Is.EqualTo(1));
            Assert.That(database.RequestedPaths, Is.EqualTo(new[] { "/api/refequipment" }));
            Assert.That(response.Sources, Has.Count.EqualTo(2));
            Assert.That(response.Confidence, Is.EqualTo(0.8));
            // RAG leg ran → the RAG retrieval confidence category is surfaced.
            Assert.That(response.ConfidenceCategory, Is.EqualTo(RagConfidenceCategory.High));
        });
    }

    [Test]
    public void AskRejectsToolOutsideTheAllowlist()
    {
        var gemini = new QueuedHttpMessageHandler(
            GeminiResponse("""{"route":"tools","ragQuestion":null,"toolCalls":[{"tool":"project_files","arguments":{}}]}"""));
        var mcp = new StubMcpClient();
        var service = CreateService(gemini, mcp, new StubEventRagService(), database: null);

        Assert.Multiple(() =>
        {
            Assert.That(
                async () => await service.AskAsync(Request("List repository files."), CancellationToken.None),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(mcp.CalledTools, Is.Empty);
        });
    }

    [Test]
    public void AskPropagatesGatewayFailureWithoutCallingTools()
    {
        var mcp = new StubMcpClient();
        var service = CreateService(new QueuedHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)),
            mcp, new StubEventRagService(), database: null);

        Assert.That(async () => await service.AskAsync(Request("Question"), CancellationToken.None),
            Throws.TypeOf<HttpRequestException>());
        Assert.That(mcp.CalledTools, Is.Empty);
    }

    [Test]
    public void AskPropagatesCallerCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var service = CreateService(new QueuedHttpMessageHandler(), new StubMcpClient(), new StubEventRagService(), database: null);

        Assert.That(async () => await service.AskAsync(Request("Question"), cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }

    private static LocalEventAiOrchestrator CreateService(
        HttpMessageHandler gemini, IMcpClient mcp, IEventRagService rag, HttpMessageHandler? database,
        IConfiguration? configuration = null)
    {
        var factory = new NamedHttpClientFactory(new Dictionary<string, HttpMessageHandler>
        {
            ["eventDatabase"] = database ?? new RecordingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json")
            })
        });

        var gateway = new AiGatewayClient(new HttpClient(gemini) { BaseAddress = new Uri("https://gateway.test/") });
        return new LocalEventAiOrchestrator(factory, gateway, mcp, rag, configuration ?? new ConfigurationManager());
    }

    private static KnowledgeQuestionRequest Request(string question) => new() { Question = question };

    private static HttpResponseMessage GeminiResponse(string text)
    {
        var json = JsonSerializer.Serialize(new
        {
            text, model = "gateway-test"
        });
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class StubMcpClient : IMcpClient
    {
        public List<string> CalledTools { get; } = [];

        public Task<IReadOnlyList<McpToolDefinition>> GetToolsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<McpToolDefinition>>([]);

        public Task<McpToolCallResponse> CallAsync(
            string tool, IReadOnlyDictionary<string, JsonElement> arguments, CancellationToken cancellationToken)
        {
            CalledTools.Add(tool);
            return Task.FromResult(new McpToolCallResponse(
                tool,
                IsError: false,
                JsonSerializer.SerializeToElement(new { feature = "student-5", valid = true, checks = new[] { "feature-allowlisted" } }),
                DateTimeOffset.UtcNow));
        }
    }

    private sealed class StubEventRagService : IEventRagService
    {
        public int CallCount { get; private set; }

        public Task<RagAssistantAnswer> AskAsync(KnowledgeQuestionRequest request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new RagAssistantAnswer(
                request.Question,
                "answered",
                "Equipment hire lasts seven days [1].",
                0.8,
                RagConfidenceCategory.High,
                [new RagKnowledgeSource(1, "Equipment hire rules", "policy-equipment-hire", 0.8)],
                "test-embedder"));
        }
    }

    private sealed class NamedHttpClientFactory(IReadOnlyDictionary<string, HttpMessageHandler> handlers) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handlers[name]) { BaseAddress = new Uri("https://service.test/") };
    }

    private sealed class RecordingHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public List<string> RequestedPaths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestedPaths.Add(request.RequestUri!.AbsolutePath);
            return Task.FromResult(response);
        }
    }

    private sealed class QueuedHttpMessageHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> responses = new(responses);
        public List<string> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestBodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return responses.Dequeue();
        }
    }
}
