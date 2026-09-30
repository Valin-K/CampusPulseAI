using System.Net;
using System.Text.Json;
using AcademicRecords.Backend.Models;
using AcademicRecords.Backend.Services;
using CampusPulse.Shared.Ai;
using CampusPulse.Shared.Mcp.Client;
using CampusPulse.Shared.Mcp.Contracts;
using CampusPulse.Shared.Rag.Assistant;
using CampusPulse.Shared.Rag.Contracts;
using Microsoft.Extensions.Configuration;

namespace AcademicRecords.Tests;

public sealed class AcademicKnowledgeOrchestratorTests
{
    [Test]
    public async Task AskLoadsOwnerScopedToolsAndUsesDynamicAcademicTool()
    {
        var gateway = new RecordingAiGateway(
            PlanResponse("mcp", null, [new { tool = "student_count", arguments = new { } }]),
            new AiGenerationResponse("There are 12 student profiles.", "shared-model"));
        var mcp = new StubMcpClient();
        var rag = new StubAcademicRagService();
        var service = CreateService(gateway, mcp, rag);

        var answer = await service.AskAsync(
            new KnowledgeQuestionRequest { Question = "How many students are there?" },
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(gateway.Calls[0].Context, Does.Contain("student_count"));
            Assert.That(gateway.Calls[0].Context, Does.Contain("students_by_subject"));
            Assert.That(gateway.Calls[0].Context, Does.Not.Contain("get_student_enrollments"));
            Assert.That(gateway.Calls[0].Context, Does.Not.Contain("project_files"));
            Assert.That(gateway.Calls[0].Context, Does.Not.Contain("course.list_courses"));
            Assert.That(mcp.ToolDiscoveryCalls, Is.EqualTo(1));
            Assert.That(mcp.CalledTools, Is.EqualTo(new[] { "student_count" }));
            Assert.That(answer.Sources.Select(source => source.DocumentId), Does.Contain("mcp:student_count"));
            Assert.That(answer.Reply, Is.EqualTo("There are 12 student profiles."));
            Assert.That(rag.LastRequest, Is.Null);
        });
    }

    [Test]
    public void AskRejectsMcpRouteWhenMcpIsDisabled()
    {
        var gateway = new RecordingAiGateway(
            PlanResponse("mcp", null, [new { tool = "student_count", arguments = new { } }]));
        var mcp = new StubMcpClient();
        var rag = new StubAcademicRagService();
        var configuration = new ConfigurationManager
        {
            ["Mcp:Enabled"] = "false"
        };
        var service = CreateService(gateway, mcp, rag, configuration);

        var exception = Assert.ThrowsAsync<HttpRequestException>(() => service.AskAsync(
            new KnowledgeQuestionRequest { Question = "How many students are there?" },
            CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.BadGateway));
            Assert.That(mcp.ToolDiscoveryCalls, Is.Zero);
            Assert.That(mcp.CalledTools, Is.Empty);
            Assert.That(rag.LastRequest, Is.Null);
            Assert.That(gateway.Calls[0].Prompt, Does.Contain("direct|rag"));
            Assert.That(gateway.Calls[0].Prompt, Does.Not.Contain("- mcp: current structured"));
            Assert.That(gateway.Calls[0].Context, Does.Contain("MCP tools are disabled."));
        });
    }

    [Test]
    public async Task AskCombinesRagAndMcpAndBindsIdentityArgumentsFromToolSchemas()
    {
        var gateway = new RecordingAiGateway(
            PlanResponse(
                "rag_and_mcp",
                "What does my progression policy say?",
                [
                    new { tool = "get_student_enrollments", arguments = new { user_id = 999 } },
                    new { tool = "calculate_student_academic_statistics", arguments = new { student_id = "STU999" } },
                    new { tool = "student_count", arguments = new { } }
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
                "get_student_enrollments", "calculate_student_academic_statistics", "student_count"
            }));
            Assert.That(mcp.Calls[0].Arguments["user_id"].GetInt32(), Is.EqualTo(42));
            Assert.That(mcp.Calls[1].Arguments["student_id"].GetString(), Is.EqualTo("STU042"));
            Assert.That(rag.LastRequest!.Question, Is.EqualTo("What does my progression policy say?"));
            Assert.That(answer.Reply, Is.EqualTo("Your academic statistics are available."));
            Assert.That(answer.Sources.Select(source => source.DocumentId), Does.Contain("mcp:get_student_enrollments"));
            Assert.That(answer.Sources.Select(source => source.DocumentId), Does.Contain("mcp:calculate_student_academic_statistics"));
            Assert.That(answer.Sources.Select(source => source.DocumentId), Does.Contain("mcp:student_count"));
            Assert.That(answer.Sources.Select(source => source.DocumentId), Does.Contain("policy-graduation"));
            Assert.That(answer.Embedder, Is.EqualTo("test-embedder"));
            Assert.That(gateway.Calls[1].Context, Does.Contain("calculate_student_academic_statistics"));
            Assert.That(gateway.Calls[0].Prompt, Does.Contain("direct|rag|mcp|rag_and_mcp"));
        });
    }

    [Test]
    public void AskRejectsRagRouteWhenRagIsDisabled()
    {
        var gateway = new RecordingAiGateway(PlanResponse("rag", "What is the policy?", []));
        var mcp = new StubMcpClient();
        var rag = new StubAcademicRagService();
        var configuration = new ConfigurationManager
        {
            ["Rag:Enabled"] = "false"
        };
        var service = CreateService(gateway, mcp, rag, configuration);

        var exception = Assert.ThrowsAsync<HttpRequestException>(() => service.AskAsync(
            new KnowledgeQuestionRequest { Question = "What is the policy?" },
            CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.BadGateway));
            Assert.That(rag.LastRequest, Is.Null);
            Assert.That(gateway.Calls[0].Prompt, Does.Not.Contain("academic policies"));
            Assert.That(gateway.Calls[0].Prompt, Does.Contain("direct|mcp"));
        });
    }

    [Test]
    public void AskRejectsToolsOutsideTheFetchedAcademicOwnerCatalogue()
    {
        var gateway = new RecordingAiGateway(
            PlanResponse("mcp", null, [new { tool = "project_files", arguments = new { } }]));
        var mcp = new StubMcpClient();
        var service = CreateService(gateway, mcp, new StubAcademicRagService());

        var exception = Assert.ThrowsAsync<HttpRequestException>(() => service.AskAsync(
            new KnowledgeQuestionRequest { Question = "List repository files." },
            CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.BadGateway));
            Assert.That(mcp.CalledTools, Is.Empty);
        });
    }

    [Test]
    public void AskRejectsIdentityToolsWithoutRequestUserId()
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

    [Test]
    public void AskRequiresAnEarlierIdentityLookupForStudentIdTools()
    {
        var gateway = new RecordingAiGateway(
            PlanResponse("mcp", null,
            [
                new { tool = "calculate_student_academic_statistics", arguments = new { student_id = "STU999" } },
                new { tool = "get_student_enrollments", arguments = new { user_id = 42 } }
            ]));
        var mcp = new StubMcpClient();
        var service = CreateService(gateway, mcp, new StubAcademicRagService());

        var exception = Assert.ThrowsAsync<HttpRequestException>(() => service.AskAsync(
            new KnowledgeQuestionRequest { Question = "What is my GPA?", UserId = 42 },
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
        StubAcademicRagService rag,
        IConfiguration? configuration = null) =>
        new(gateway, mcp, rag, configuration ?? new ConfigurationManager());

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
        public int ToolDiscoveryCalls { get; private set; }
        public List<string> CalledTools { get; } = [];
        public List<(string Tool, IReadOnlyDictionary<string, JsonElement> Arguments)> Calls { get; } = [];

        public Task<IReadOnlyList<McpToolDefinition>> GetToolsAsync(CancellationToken cancellationToken)
        {
            ToolDiscoveryCalls++;
            return Task.FromResult<IReadOnlyList<McpToolDefinition>>(
            [
                new("get_student_enrollments", "Return a student's enrollments.",
                    new Dictionary<string, string> { ["user_id"] = "Required numeric user ID." }, "Academic Records"),
                new("calculate_student_academic_statistics", "Return WAM and GPA.",
                    new Dictionary<string, string> { ["student_id"] = "Required student identifier." }, "Academic Records"),
                new("student_count", "Return the number of student profiles.",
                    new Dictionary<string, string>(), "Academic Records"),
                new("students_by_subject", "Return student count for a subject.",
                    new Dictionary<string, string> { ["subject_code"] = "Required subject code." }, "Academic Records"),
                new("project_files", "List repository files.",
                    new Dictionary<string, string>(), "Repository"),
                new("course.list_courses", "List available courses.",
                    new Dictionary<string, string>(), "Course Management")
            ]);
        }

        public Task<McpToolCallResponse> CallAsync(
            string tool,
            IReadOnlyDictionary<string, JsonElement> arguments,
            CancellationToken cancellationToken)
        {
            CalledTools.Add(tool);
            Calls.Add((tool, arguments));
            var result = tool switch
            {
                "get_student_enrollments" => JsonSerializer.SerializeToElement(
                    new { studentId = "STU042", enrollments = Array.Empty<object>() }),
                "calculate_student_academic_statistics" => JsonSerializer.SerializeToElement(
                    new { weightedAverageMark = 82.0, gradePointAverage = 6.0 }),
                "student_count" => JsonSerializer.SerializeToElement(new { student_count = 12 }),
                _ => JsonSerializer.SerializeToElement(new { })
            };
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
