using System.Text.Json;
using CampusHousing.Backend.Models;
using CampusHousing.Backend.Services;
using CampusPulse.Shared.Ai;
using CampusPulse.Shared.Mcp.Client;
using CampusPulse.Shared.Mcp.Contracts;
using CampusPulse.Shared.Rag.Assistant;
using CampusPulse.Shared.Rag.Contracts;
using Microsoft.Extensions.Configuration;

namespace CampusHousing.Tests;

public sealed class HousingKnowledgeOrchestratorTests
{
    [Test]
    public async Task AskUsesOnlyCampusHousingToolsAndMergesMcpSource()
    {
        var gateway = new RecordingGateway(
            Plan("mcp", null, [new { tool = "room_status", arguments = new { } }]),
            new AiGenerationResponse("Room 101 is available.", "test-model"));
        var mcp = new StubMcpClient();
        var service = new HousingKnowledgeOrchestrator(
            gateway,
            mcp,
            new StubRagService(),
            new ConfigurationManager());

        var answer = await service.AskAsync(
            new KnowledgeQuestionRequest { Question = "Is room 101 available?" },
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(mcp.CalledTools, Is.EqualTo(new[] { "room_status" }));
            Assert.That(gateway.Calls[0].Context, Does.Contain("room_status"));
            Assert.That(gateway.Calls[0].Context, Does.Not.Contain("repository_tool"));
            Assert.That(answer.Reply, Is.EqualTo("Room 101 is available."));
            Assert.That(answer.Sources.Select(source => source.DocumentId), Does.Contain("mcp:room_status"));
        });
    }

    [Test]
    public async Task AskRejectsMcpRouteWhenMcpIsDisabled()
    {
        var gateway = new RecordingGateway(
            Plan("mcp", null, [new { tool = "room_status", arguments = new { } }]));
        var mcp = new StubMcpClient();
        var configuration = new ConfigurationManager { ["Mcp:Enabled"] = "false" };
        var service = new HousingKnowledgeOrchestrator(
            gateway,
            mcp,
            new StubRagService(),
            configuration);

        var exception = Assert.ThrowsAsync<HttpRequestException>(() => service.AskAsync(
            new KnowledgeQuestionRequest { Question = "Is room 101 available?" },
            CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.BadGateway));
            Assert.That(mcp.DiscoveryCalls, Is.Zero);
            Assert.That(gateway.Calls[0].Prompt, Does.Contain("direct|rag"));
        });
    }

    private static AiGenerationResponse Plan(string route, string? ragQuestion, object[] calls) =>
        new(JsonSerializer.Serialize(new { route, ragQuestion, mcpCalls = calls }), "planner-model");

    private sealed class RecordingGateway(params AiGenerationResponse[] responses) : IAiGatewayClient
    {
        private readonly Queue<AiGenerationResponse> responses = new(responses);
        public List<(string Prompt, string Context)> Calls { get; } = [];

        public Task<AiGenerationResponse> GenerateAsync(string prompt, string context, CancellationToken cancellationToken = default)
        {
            Calls.Add((prompt, context));
            return Task.FromResult(responses.Dequeue());
        }

        public Task<AiEmbeddingResponse> EmbedAsync(IReadOnlyList<string> texts, bool isQuery, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AiGatewayStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubMcpClient : IMcpClient
    {
        public int DiscoveryCalls { get; private set; }
        public List<string> CalledTools { get; } = [];

        public Task<IReadOnlyList<McpToolDefinition>> GetToolsAsync(CancellationToken cancellationToken)
        {
            DiscoveryCalls++;
            return Task.FromResult<IReadOnlyList<McpToolDefinition>>(
            [
                new("room_status", "Return current room status.", new Dictionary<string, string>(), "Campus Housing"),
                new("repository_tool", "Return repository data.", new Dictionary<string, string>(), "Repository")
            ]);
        }

        public Task<McpToolCallResponse> CallAsync(string tool, IReadOnlyDictionary<string, JsonElement> arguments, CancellationToken cancellationToken)
        {
            CalledTools.Add(tool);
            return Task.FromResult(new McpToolCallResponse(
                tool,
                false,
                JsonSerializer.SerializeToElement(new { available = true }),
                DateTimeOffset.UtcNow));
        }
    }

    private sealed class StubRagService : IHousingRagService
    {
        public Task<RagAssistantAnswer> AskAsync(KnowledgeQuestionRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new RagAssistantAnswer(
                request.Question,
                RagStatus.Answered,
                "RAG result",
                0.8,
                RagConfidenceCategory.High,
                [],
                "test-embedder"));
    }
}
