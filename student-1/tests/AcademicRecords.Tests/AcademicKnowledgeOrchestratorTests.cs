using System.Net;
using System.Text.Json;
using AcademicRecords.Backend.Models;
using AcademicRecords.Backend.Services;
using CampusPulse.Shared.Ai;
using CampusPulse.Shared.Mcp.Client;
using CampusPulse.Shared.Mcp.Contracts;
using CampusPulse.Shared.Rag.Assistant;
using CampusPulse.Shared.Rag.Contracts;

namespace AcademicRecords.Tests;

public sealed class AcademicKnowledgeOrchestratorTests
{
    [Test]
    public async Task AskUsesDirectRouteWithoutCallingRagOrMcpToolsAndUsesGatewayModel()
    {
        var gateway = new RecordingAiGateway(
            PlanResponse("direct", null, []),
            new AiGenerationResponse("A general academic explanation.", "shared-model"));
        var mcp = new StubMcpClient();
        var rag = new StubAcademicRagService();
        var service = CreateService(gateway, mcp, rag);

        var answer = await service.AskAsync(
            new KnowledgeQuestionRequest { Question = "What is a GPA?" },
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(answer.Reply, Is.EqualTo("A general academic explanation."));
            Assert.That(answer.Embedder, Is.EqualTo("shared-model"));
            Assert.That(rag.LastRequest, Is.Null);
            Assert.That(mcp.CalledTools, Is.Empty);
            Assert.That(gateway.Calls, Has.Count.EqualTo(2));
            Assert.That(gateway.Calls[0].Context, Does.Contain("What is a GPA?"));
            Assert.That(gateway.Calls[0].Context, Does.Contain("[]"));
            Assert.That(gateway.Calls[0].Context, Does.Not.Contain("get_student_enrollments"));
            Assert.That(gateway.Calls[1].Prompt, Does.Contain("Use only the supplied RAG and MCP results"));
            Assert.That(gateway.Calls[1].Context, Does.Contain("What is a GPA?"));
        });
    }

    [Test]
    public async Task AskCombinesRagAndMcpAndBindsStudentIdsFromTrustedInputs()
    {
        var gateway = new RecordingAiGateway(
            PlanResponse(
                "rag_and_mcp",
                "What does my progression policy say?",
                [
                    new { tool = "get_student_enrollments", arguments = new { user_id = 999 } },
                    new { tool = "calculate_student_academic_statistics", arguments = new { student_id = "STU999" } }
                ]),
            new AiGenerationResponse("Your academic statistics are available.", "shared-model"));
        var mcp = new StubMcpClient();
        var rag = new StubAcademicRagService();
        var service = CreateService(gateway, mcp, rag);

        var answer = await service.AskAsync(
            new KnowledgeQuestionRequest { Question = "How am I progressing?", UserId = 42 },
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(mcp.CalledTools, Is.EqualTo(new[]
            {
                "get_student_enrollments", "calculate_student_academic_statistics"
            }));
            Assert.That(mcp.Calls[0].Arguments["user_id"].GetInt32(), Is.EqualTo(42));
            Assert.That(mcp.Calls[1].Arguments["student_id"].GetString(), Is.EqualTo("STU042"));
            Assert.That(rag.LastRequest!.Question, Is.EqualTo("What does my progression policy say?"));
            Assert.That(answer.Reply, Is.EqualTo("Your academic statistics are available."));
            Assert.That(answer.Sources.Select(source => source.DocumentId), Does.Contain("mcp:get_student_enrollments"));
            Assert.That(answer.Sources.Select(source => source.DocumentId), Does.Contain("mcp:calculate_student_academic_statistics"));
            Assert.That(answer.Sources.Select(source => source.DocumentId), Does.Contain("policy-graduation"));
            Assert.That(answer.Embedder, Is.EqualTo("test-embedder"));
            Assert.That(gateway.Calls[1].Context, Does.Contain("get_student_enrollments"));
        });
    }

    [Test]
    public void AskRejectsToolsOutsideTheAcademicAllowlist()
    {
        var gateway = new RecordingAiGateway(
            PlanResponse("mcp", null, [new { tool = "project_files", arguments = new { } }]));
        var mcp = new StubMcpClient();
        var service = CreateService(gateway, mcp, new StubAcademicRagService());

        var exception = Assert.ThrowsAsync<HttpRequestException>(() => service.AskAsync(
            new KnowledgeQuestionRequest { Question = "List repository files.", UserId = 42 },
            CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.BadGateway));
            Assert.That(mcp.CalledTools, Is.Empty);
        });
    }

    [Test]
    public void AskRejectsStudentToolsWithoutRequestUserId()
    {
        var gateway = new RecordingAiGateway(
            PlanResponse("mcp", null, [new { tool = "get_student_enrollments", arguments = new { } }]));
        var mcp = new StubMcpClient();
        var service = CreateService(gateway, mcp, new StubAcademicRagService());

        var exception = Assert.ThrowsAsync<HttpRequestException>(() => service.AskAsync(
            new KnowledgeQuestionRequest { Question = "What courses am I taking?" },
            CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.BadGateway));
            Assert.That(mcp.CalledTools, Is.Empty);
        });
    }

    private static AcademicKnowledgeOrchestrator CreateService(
        RecordingAiGateway gateway,
        StubMcpClient mcp,
        StubAcademicRagService rag) =>
        new(gateway, mcp, rag);

    private static AiGenerationResponse PlanResponse(
        string route,
        string? ragQuestion,
        object[] calls)
    {
        var text = JsonSerializer.Serialize(new
        {
            route,
            ragQuestion,
            mcpCalls = calls
        });
        return new AiGenerationResponse(text, "shared-model");
    }

    private sealed class RecordingAiGateway(params AiGenerationResponse[] responses) : IAiGatewayClient
    {
        private readonly Queue<AiGenerationResponse> queue = new(responses);
        public List<GatewayCall> Calls { get; } = [];

        public Task<AiGenerationResponse> GenerateAsync(
            string prompt,
            string context,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(new GatewayCall(prompt, context, cancellationToken));
            return Task.FromResult(queue.Dequeue());
        }

        public Task<AiEmbeddingResponse> EmbedAsync(
            IReadOnlyList<string> texts,
            bool isQuery,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AiGatewayStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed record GatewayCall(string Prompt, string Context, CancellationToken CancellationToken);

    private sealed class StubMcpClient : IMcpClient
    {
        public List<string> CalledTools { get; } = [];
        public List<(string Tool, IReadOnlyDictionary<string, JsonElement> Arguments)> Calls { get; } = [];

        public Task<IReadOnlyList<McpToolDefinition>> GetToolsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<McpToolDefinition>>(
            [
                new("get_student_enrollments", "Get a student's enrollments.", new Dictionary<string, string>()),
                new("calculate_student_academic_statistics", "Get WAM and GPA.", new Dictionary<string, string>()),
                new("project_files", "List repository files.", new Dictionary<string, string>())
            ]);

        public Task<McpToolCallResponse> CallAsync(
            string tool,
            IReadOnlyDictionary<string, JsonElement> arguments,
            CancellationToken cancellationToken)
        {
            CalledTools.Add(tool);
            Calls.Add((tool, arguments));
            var result = tool == "get_student_enrollments"
                ? JsonSerializer.SerializeToElement(new { studentId = "STU042", enrollments = Array.Empty<object>() })
                : JsonSerializer.SerializeToElement(new { weightedAverageMark = 82.0, gradePointAverage = 6.0 });
            return Task.FromResult(new McpToolCallResponse(tool, false, result, DateTimeOffset.UtcNow));
        }
    }

    private sealed class StubAcademicRagService : IAcademicRagService
    {
        public KnowledgeQuestionRequest? LastRequest { get; private set; }

        public Task<RagAssistantAnswer> AskAsync(
            KnowledgeQuestionRequest request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new RagAssistantAnswer(
                request.Question,
                "answered",
                "RAG policy result",
                0.8,
                RagConfidenceCategory.From(RagStatus.Answered, 0.8),
                [new RagKnowledgeSource(1, "Graduation eligibility", "policy-graduation", 0.8)],
                "test-embedder"));
        }
    }
}
