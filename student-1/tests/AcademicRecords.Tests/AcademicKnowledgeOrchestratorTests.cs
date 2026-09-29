using System.Net;
using System.Text;
using System.Text.Json;
using AcademicRecords.Backend.Models;
using AcademicRecords.Backend.Services;
using CampusPulse.Shared.Mcp.Client;
using CampusPulse.Shared.Mcp.Contracts;
using CampusPulse.Shared.Rag.Assistant;
using CampusPulse.Shared.Rag.Contracts;
using Microsoft.Extensions.Configuration;

namespace AcademicRecords.Tests;

public sealed class AcademicKnowledgeOrchestratorTests
{
    [Test]
    public async Task AskUsesDirectRouteWithoutCallingRagOrMcpTools()
    {
        var gemini = new QueueHttpMessageHandler(
            GeminiResponse("""
                {
                  "route": "direct",
                  "ragQuestion": null,
                  "mcpCalls": []
                }
                """),
            GeminiResponse("A general academic explanation."));
        var mcp = new StubMcpClient();
        var rag = new StubAcademicRagService();
        var service = CreateService(gemini, mcp, rag);

        var answer = await service.AskAsync(
            new KnowledgeQuestionRequest { Question = "What is a GPA?" },
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(answer.Reply, Is.EqualTo("A general academic explanation."));
            Assert.That(rag.LastRequest, Is.Null);
            Assert.That(mcp.CalledTools, Is.Empty);
        });
    }

    [Test]
    public async Task AskCombinesRagAndMcpAndBindsStudentIdsFromTrustedInputs()
    {
        var gemini = new QueueHttpMessageHandler(
            GeminiResponse("""
                {
                  "route": "rag_and_mcp",
                  "ragQuestion": "What does my progression policy say?",
                  "mcpCalls": [
                    { "tool": "get_student_enrollments", "arguments": { "user_id": 999 } },
                    { "tool": "calculate_student_academic_statistics", "arguments": { "student_id": "STU999" } }
                  ]
                }
                """),
            GeminiResponse("Your academic statistics are available."));
        var mcp = new StubMcpClient();
        var rag = new StubAcademicRagService();
        var service = CreateService(gemini, mcp, rag);

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
        });
    }

    [Test]
    public void AskRejectsToolsOutsideTheAcademicAllowlist()
    {
        var gemini = new QueueHttpMessageHandler(GeminiResponse("""
            {
              "route": "mcp",
              "ragQuestion": null,
              "mcpCalls": [{ "tool": "project_files", "arguments": {} }]
            }
            """));
        var mcp = new StubMcpClient();
        var service = CreateService(gemini, mcp, new StubAcademicRagService());

        Assert.Multiple(() =>
        {
            Assert.That(
                async () => await service.AskAsync(
                    new KnowledgeQuestionRequest { Question = "List repository files.", UserId = 42 },
                    CancellationToken.None),
                Throws.TypeOf<AcademicGeminiResponseException>());
            Assert.That(mcp.CalledTools, Is.Empty);
        });
    }

    [Test]
    public void AskRejectsStudentToolsWithoutRequestUserId()
    {
        var gemini = new QueueHttpMessageHandler(GeminiResponse("""
            {
              "route": "mcp",
              "ragQuestion": null,
              "mcpCalls": [{ "tool": "get_student_enrollments", "arguments": {} }]
            }
            """));
        var mcp = new StubMcpClient();
        var service = CreateService(gemini, mcp, new StubAcademicRagService());

        Assert.Multiple(() =>
        {
            Assert.That(
                async () => await service.AskAsync(
                    new KnowledgeQuestionRequest { Question = "What courses am I taking?" },
                    CancellationToken.None),
                Throws.TypeOf<AcademicGeminiResponseException>());
            Assert.That(mcp.CalledTools, Is.Empty);
        });
    }

    private static AcademicKnowledgeOrchestrator CreateService(
        HttpMessageHandler gemini,
        StubMcpClient mcp,
        StubAcademicRagService rag)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GEMINI_API_KEY"] = "test-key",
                ["Gemini:Model"] = "gemini-test"
            })
            .Build();
        return new AcademicKnowledgeOrchestrator(
            new StubHttpClientFactory(new HttpClient(gemini)
            {
                BaseAddress = new Uri("https://gemini.test/")
            }),
            configuration,
            mcp,
            rag);
    }

    private static HttpResponseMessage GeminiResponse(string text)
    {
        var json = JsonSerializer.Serialize(new
        {
            candidates = new[]
            {
                new { content = new { parts = new[] { new { text } } } }
            }
        });
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => name == "gemini"
            ? client
            : throw new ArgumentException($"Unexpected client name: {name}", nameof(name));
    }

    private sealed class QueueHttpMessageHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> queue = new(responses);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(queue.Dequeue());
        }
    }

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
