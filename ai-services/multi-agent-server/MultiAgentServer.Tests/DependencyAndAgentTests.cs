using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CampusPulse.Shared.Ai;
using CampusPulse.Shared.Mcp.Client;
using CampusPulse.Shared.Mcp.Contracts;
using CampusPulse.Shared.MultiAgent;
using Microsoft.Extensions.Logging.Abstractions;

namespace MultiAgentServer.Tests;

public sealed class DependencyAndAgentTests
{
    private static Workflow Workflow(bool fallback = true) => new(
        Guid.NewGuid(), "coursemanagement", "Summarise ASD101", WorkflowStatus.Running, 1, 1,
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
        [new("catalogue", "Live API", "ASD101 is available.", DateTimeOffset.UtcNow)], [], [], [],
        AllowFallback: fallback);

    [Test]
    public async Task MissingMcpAndRagUseExplicitFallbackWithoutInventedEvidence()
    {
        using var factory = new HttpFactory(HttpStatusCode.ServiceUnavailable);
        var collector = new WorkflowEvidenceCollector(new McpStub(false), factory, NullLogger<WorkflowEvidenceCollector>.Instance);
        var result = await collector.CollectAsync(Workflow(), CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(result.Dependencies.Select(item => item.Status), Is.EqualTo(new[] { "fallback", "fallback" }));
            Assert.That(result.Evidence, Has.Count.EqualTo(1));
            Assert.That(result.Evidence[0].Id, Is.EqualTo("catalogue"));
            Assert.That(result.Dependencies.All(item => item.Detail.Contains("unavailable", StringComparison.Ordinal)), Is.True);
        });
    }

    [Test]
    public void StrictDependencyPolicyFailsInsteadOfFallingBack()
    {
        using var factory = new HttpFactory(HttpStatusCode.ServiceUnavailable);
        var collector = new WorkflowEvidenceCollector(new McpStub(false), factory, NullLogger<WorkflowEvidenceCollector>.Instance);
        Assert.ThrowsAsync<InvalidDataException>(() => collector.CollectAsync(Workflow(false), CancellationToken.None));
    }

    [Test]
    public async Task DisabledDependenciesMakeNoExternalCalls()
    {
        using var factory = new HttpFactory(HttpStatusCode.OK);
        var mcp = new McpStub(false);
        var collector = new WorkflowEvidenceCollector(mcp, factory, NullLogger<WorkflowEvidenceCollector>.Instance);
        var result = await collector.CollectAsync(Workflow() with { UseMcp = false, UseRag = false }, CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(result.Dependencies.Select(item => item.Status), Is.EqualTo(new[] { "disabled", "disabled" }));
            Assert.That(factory.Calls, Is.Zero);
            Assert.That(mcp.Calls, Is.Zero);
        });
    }

    [Test]
    public async Task RagOnlyIncludesSourcesAtOrAboveTheRequiredThreshold()
    {
        using var factory = new HttpFactory(HttpStatusCode.OK, new
        {
            collection = "coursemanagement",
            sources = new[]
            {
                new { documentId = "course-policy", chunkIndex = 0, score = 0.70, text = "ASD101 policy" },
                new { documentId = "unrelated", chunkIndex = 1, score = 0.6999, text = "Other policy" }
            }
        });
        var collector = new WorkflowEvidenceCollector(new McpStub(true), factory, NullLogger<WorkflowEvidenceCollector>.Instance);
        var result = await collector.CollectAsync(Workflow(), CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(result.Dependencies.Select(item => item.Status), Is.EqualTo(new[] { "available", "available" }));
            Assert.That(result.Evidence, Has.Count.EqualTo(3));
            Assert.That(result.Evidence.Any(item => item.Content == "ASD101 policy"), Is.True);
            Assert.That(result.Evidence.Any(item => item.Content == "Other policy"), Is.False);
        });
    }

    [Test]
    public async Task EmptyRagCorpusIsAnExplicitFallback()
    {
        using var factory = new HttpFactory(HttpStatusCode.OK, new { collection = "coursemanagement", sources = Array.Empty<object>() });
        var collector = new WorkflowEvidenceCollector(new McpStub(true), factory, NullLogger<WorkflowEvidenceCollector>.Instance);
        var result = await collector.CollectAsync(Workflow(), CancellationToken.None);
        Assert.That(result.Dependencies.Single(item => item.Service == "rag").Status, Is.EqualTo("fallback"));
    }

    [Test]
    public async Task MalformedRagSourcesAreAnExplicitFallback()
    {
        using var factory = new HttpFactory(HttpStatusCode.OK, new { collection = "coursemanagement", sources = new object?[] { null } });
        var collector = new WorkflowEvidenceCollector(new McpStub(true), factory, NullLogger<WorkflowEvidenceCollector>.Instance);
        var result = await collector.CollectAsync(Workflow(), CancellationToken.None);
        Assert.That(result.Dependencies.Single(item => item.Service == "rag").Status, Is.EqualTo("fallback"));
    }

    [TestCase("planner", """{"objective":"Summarise ASD101","steps":["Read catalogue"],"evidenceRequired":["catalogue"]}""")]
    [TestCase("worker", """{"output":"ASD101 [catalogue]","evidenceUsed":["catalogue"],"limitations":[]}""")]
    [TestCase("reviewer", """{"recommendation":"approve","risks":[],"corrections":[],"retest":[],"evidenceChecked":["catalogue"]}""")]
    public async Task RolePromptsReturnValidatedStructuredOutputs(string role, string output)
    {
        var ai = new AiStub(output);
        var result = await new WorkflowAgents(ai).ExecuteAsync(role, Workflow(), CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(result.Agent, Is.EqualTo(role));
            Assert.That(result.Model, Is.EqualTo("test-gemini"));
            Assert.That(ai.Prompt, Does.Contain($"{role} agent"));
            Assert.That(ai.Context, Does.Contain("catalogue"));
        });
    }

    [TestCase("planner", "not JSON")]
    [TestCase("planner", """{"objective":"Plan","steps":[],"evidenceRequired":[]}""")]
    [TestCase("worker", """{"output":"Invented fact","evidenceUsed":["nonexistent"],"limitations":[]}""")]
    [TestCase("reviewer", """{"recommendation":"approved","risks":[],"corrections":[],"retest":[],"evidenceChecked":["catalogue"]}""")]
    public void MalformedOrUngroundedAgentResponsesFailExplicitly(string role, string output)
    {
        var agents = new WorkflowAgents(new AiStub(output));
        Assert.ThrowsAsync<InvalidDataException>(() => agents.ExecuteAsync(role, Workflow(), CancellationToken.None));
    }

    [Test]
    public async Task FencedModelJsonIsAccepted()
    {
        var agents = new WorkflowAgents(new AiStub("```json\n" +
            """{"objective":"Plan","steps":["Read catalogue"],"evidenceRequired":["catalogue"]}""" + "\n```"));
        var result = await agents.ExecuteAsync("planner", Workflow(), CancellationToken.None);
        Assert.That(result.Plan!.Objective, Is.EqualTo("Plan"));
    }

    private sealed class McpStub(bool available) : IMcpClient
    {
        public int Calls { get; private set; }
        public Task<IReadOnlyList<McpToolDefinition>> GetToolsAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<McpToolCallResponse> CallAsync(string tool, IReadOnlyDictionary<string, JsonElement> arguments, CancellationToken ct)
        {
            Calls++;
            if (!available) throw new HttpRequestException("MCP connection refused");
            return Task.FromResult(new McpToolCallResponse(tool, false,
                JsonSerializer.SerializeToElement(new { course = "ASD101" }), DateTimeOffset.UtcNow));
        }
    }

    private sealed class HttpFactory(HttpStatusCode status, object? body = null) : IHttpClientFactory, IDisposable
    {
        public int Calls { get; private set; }
        private readonly List<HttpClient> clients = [];
        public HttpClient CreateClient(string name)
        {
            var client = new HttpClient(new Handler(() =>
            {
                Calls++;
                return new HttpResponseMessage(status) { Content = JsonContent.Create(body ?? new { error = "unavailable" }) };
            })) { BaseAddress = new Uri("http://local.test") };
            clients.Add(client);
            return client;
        }
        public void Dispose() { foreach (var client in clients) client.Dispose(); }
    }

    private sealed class Handler(Func<HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(response());
    }

    private sealed class AiStub(string output) : IAiGatewayClient
    {
        public string? Prompt { get; private set; }
        public string? Context { get; private set; }
        public Task<AiGenerationResponse> GenerateAsync(string prompt, string context, CancellationToken ct = default)
        {
            Prompt = prompt;
            Context = context;
            return Task.FromResult(new AiGenerationResponse(output, "test-gemini"));
        }
        public Task<AiEmbeddingResponse> EmbedAsync(IReadOnlyList<string> texts, bool isQuery, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AiGatewayStatus> GetStatusAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }
}
