using CampusHousing.Backend.Services;
using CampusPulse.Shared.Ai;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Net.Http.Json;
using CampusPulse.Shared.Mcp.Client;
using CampusPulse.Shared.Mcp.Contracts;
using CampusPulse.Shared.Rag.Client;
using CampusPulse.Shared.Rag.Contracts;

namespace CampusHousing.Tests;

public class MaintenanceClassifierTests
{
    [TestCase("There is a gas leak in the kitchen", "Emergency", "Plumbing")]
    [TestCase("The hallway light is broken", "Low", "Electrical")]
    [TestCase("The door lock is not working", "Low", "Security")]
    public async Task ClassifyAsync_WithoutGemini_UsesRuleBasedClassification(string description, string severity, string queue)
    {
        var result = await CreateClassifier().ClassifyAsync(description, CancellationToken.None);

        Assert.That(result.Severity, Is.EqualTo(severity));
        Assert.That(result.RepairQueue, Is.EqualTo(queue));
    }

    [TestCase("There is a flood and no heat", "Emergency", "Heating")]
    [TestCase("The broken heater", "High", "Heating")]
    [TestCase("The water is leaking", "High", "Plumbing")]
    [TestCase("The power is out", "Low", "Electrical")]
    [TestCase("The security camera is broken", "Low", "Security")]
    [TestCase("The window is stuck", "Low", "General Maintenance")]
    public async Task ClassifyAsync_FallbackRecognizesAllRuleCategories(string description, string severity, string queue)
    {
        var result = await CreateClassifier().ClassifyAsync(description, CancellationToken.None);

        Assert.That(result.Severity, Is.EqualTo(severity));
        Assert.That(result.RepairQueue, Is.EqualTo(queue));
    }

    [Test]
    public async Task ClassifyAsync_GeminiResponseIsNormalized()
    {
        var responseText = "```json\n{\"severity\":\" emergency \",\"repairQueue\":\" invalid \"}\n```";
        var classifier = CreateClassifier(new StubHandler(HttpStatusCode.OK, new
        {
            text = responseText, model = "gateway-test"
        }));

        var result = await classifier.ClassifyAsync("The issue is described", CancellationToken.None);

        Assert.That(result, Is.EqualTo(new MaintenanceClassification("emergency", "General Maintenance")));
    }

    [TestCase(HttpStatusCode.BadGateway)]
    [TestCase(HttpStatusCode.OK)]
    public async Task ClassifyAsync_InvalidGeminiResponseUsesFallback(HttpStatusCode status)
    {
        var body = status == HttpStatusCode.OK ? new { text = "", model = "gateway-test" } : null;
        var classifier = CreateClassifier(new StubHandler(status, body));

        var result = await classifier.ClassifyAsync("A gas leak", CancellationToken.None);

        Assert.That(result, Is.EqualTo(new MaintenanceClassification("Emergency", "Plumbing")));
    }

    [Test]
    public async Task ClassifyAsync_GeminiJsonExceptionUsesFallback()
    {
        var classifier = CreateClassifier(new StubHandler(HttpStatusCode.OK, "not json"));

        var result = await classifier.ClassifyAsync("A broken light", CancellationToken.None);

        Assert.That(result, Is.EqualTo(new MaintenanceClassification("Low", "Electrical")));
    }

    [Test]
    public async Task ClassifyAsync_ValidGeminiValuesAreTrimmedAndCaseInsensitive()
    {
        var classifier = CreateClassifier(new StubHandler(HttpStatusCode.OK, new
        {
            text = "{\"Severity\":\" Medium \",\"RepairQueue\":\" security \"}", model = "gateway-test"
        }));

        var result = await classifier.ClassifyAsync("A camera issue", CancellationToken.None);

        Assert.That(result, Is.EqualTo(new MaintenanceClassification("Medium", "security")));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public async Task ClassifyAsync_MissingGeminiTextUsesFallback(string? text)
    {
        var classifier = CreateClassifier(new StubHandler(HttpStatusCode.OK, new
        {
            text, model = "gateway-test"
        }));

        var result = await classifier.ClassifyAsync("A broken light", CancellationToken.None);

        Assert.That(result, Is.EqualTo(new MaintenanceClassification("Low", "Electrical")));
    }

    [TestCase(typeof(HttpRequestException))]
    [TestCase(typeof(TaskCanceledException))]
    public async Task ClassifyAsync_GeminiTransportFailureUsesFallback(Type exceptionType)
    {
        var classifier = CreateClassifier(new ThrowingHandler(exceptionType));

        var result = await classifier.ClassifyAsync("A gas leak", CancellationToken.None);

        Assert.That(result, Is.EqualTo(new MaintenanceClassification("Emergency", "Plumbing")));
    }

    [Test]
    public async Task ClassifyAsync_SeparatesInstructionsFromMaintenanceContext()
    {
        var handler = new StubHandler(HttpStatusCode.OK, new
        {
            text = "{\"severity\":\"Low\",\"repairQueue\":\"Security\"}", model = "gateway-test"
        });
        var classifier = CreateClassifier(handler);

        await classifier.ClassifyAsync("A lock issue", CancellationToken.None);

        using var body = JsonDocument.Parse(handler.RequestBody!);
        Assert.That(body.RootElement.GetProperty("prompt").GetString(), Does.Contain("Classify"));
        Assert.That(body.RootElement.GetProperty("prompt").GetString(), Does.Not.Contain("A lock issue"));
        Assert.That(body.RootElement.GetProperty("context").GetString(), Does.Contain("A lock issue"));
    }

    [Test]
    public async Task ClassifyAsync_IncludesSimilarHistoricalIncidentWithoutStudentIdentity()
    {
        var handler = new StubHandler(HttpStatusCode.OK, new
        {
            text = "{\"severity\":\"High\",\"repairQueue\":\"Plumbing\"}", model = "gateway-test"
        });
        var historyHandler = new StubHandler(HttpStatusCode.OK, new object[]
        {
            new
            {
                id = 12,
                studentId = "STU-PRIVATE",
                dormRoomId = 4,
                description = "Water is leaking beneath the bathroom sink",
                severity = "High",
                repairQueue = "Plumbing",
                status = "Resolved",
                createdAt = DateTime.UtcNow,
                resolvedAt = DateTime.UtcNow
            },
            new
            {
                id = 13,
                studentId = "STU-OTHER",
                dormRoomId = 2,
                description = "The hallway light is broken",
                severity = "Low",
                repairQueue = "Electrical",
                status = "Open",
                createdAt = DateTime.UtcNow.AddDays(-1),
                resolvedAt = (DateTime?)null
            }
        });
        var classifier = CreateClassifier(handler, historyHandler);

        await classifier.ClassifyAsync("There is water leaking under the sink", CancellationToken.None);

        using var body = JsonDocument.Parse(handler.RequestBody!);
        var context = body.RootElement.GetProperty("context").GetString()!;
        Assert.Multiple(() =>
        {
            Assert.That(context, Does.Contain("Water is leaking beneath the bathroom sink"));
            Assert.That(context, Does.Contain("Severity=High"));
            Assert.That(context, Does.Not.Contain("STU-PRIVATE"));
            Assert.That(context, Does.Not.Contain("STU-OTHER"));
        });
    }

    [Test]
    public async Task ClassifyAsync_UsesRagAndMcpEvidence()
    {
        var handler = new StubHandler(HttpStatusCode.OK, new
        {
            text = "{\"severity\":\"High\",\"repairQueue\":\"Plumbing\"}", model = "gateway-test"
        });
        var historyHandler = new StubHandler(HttpStatusCode.OK, new[]
        {
            new
            {
                id = 12,
                studentId = "STU-PRIVATE",
                dormRoomId = 4,
                description = "The sink is leaking",
                severity = "High",
                repairQueue = "Plumbing",
                status = "Resolved",
                createdAt = DateTime.UtcNow,
                resolvedAt = DateTime.UtcNow
            }
        });
        var classifier = CreateClassifier(
            handler,
            historyHandler,
            new StubRagServerClient(),
            new StubMcpClient());

        await classifier.ClassifyAsync("Water is leaking under the sink", CancellationToken.None);

        using var body = JsonDocument.Parse(handler.RequestBody!);
        var context = body.RootElement.GetProperty("context").GetString()!;
        Assert.Multiple(() =>
        {
            Assert.That(context, Does.Contain("RAG precedent: sink leak"));
            Assert.That(context, Does.Contain("Live MCP housing context"));
            Assert.That(context, Does.Contain("open ticket"));
        });
    }

    [Test]
    public void ClassifyAsync_CallerCancellationIsNotSwallowed()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var classifier = CreateClassifier(new ThrowingHandler(typeof(TaskCanceledException)));
        Assert.That(async () => await classifier.ClassifyAsync("A gas leak", cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }

    private static MaintenanceClassifier CreateClassifier(
        HttpMessageHandler? handler = null,
        HttpMessageHandler? historyHandler = null,
        IRagServerClient? rag = null,
        IMcpClient? mcp = null)
    {
        return new MaintenanceClassifier(
            new AiGatewayClient(new HttpClient(handler ?? new StubHandler(HttpStatusCode.ServiceUnavailable, null))
                { BaseAddress = new Uri("https://gateway.test/") }),
            new StubHttpClientFactory(new HttpClient(historyHandler ?? new StubHandler(HttpStatusCode.ServiceUnavailable, null))
                { BaseAddress = new Uri("https://housing-database.test/") }),
            NullLogger<MaintenanceClassifier>.Instance,
            rag,
            mcp);
    }

    private sealed class StubRagServerClient : IRagServerClient
    {
        public Task<RagIngestResponse?> IngestAsync(string collection, RagIngestRequest request, CancellationToken cancellationToken) =>
            Task.FromResult<RagIngestResponse?>(new(collection, request.Documents.Count, request.Documents.Count, "test-embedder"));

        public Task<RagAnswerResponse?> AnswerAsync(string collection, RagAnswerRequest request, CancellationToken cancellationToken) =>
            Task.FromResult<RagAnswerResponse?>(new(
                collection,
                request.Question,
                RagStatus.Answered,
                null,
                0.9,
                [new RagSource(
                    1,
                    "maintenance-ticket-12",
                    0,
                    0.9,
                    "RAG precedent: sink leak",
                    new Dictionary<string, string>
                    {
                        ["severity"] = "High",
                        ["repairQueue"] = "Plumbing",
                        ["status"] = "Resolved"
                    })],
                "test-embedder",
                null,
                DateTimeOffset.UtcNow));
    }

    private sealed class StubMcpClient : IMcpClient
    {
        public Task<IReadOnlyList<McpToolDefinition>> GetToolsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<McpToolDefinition>>([]);

        public Task<McpToolCallResponse> CallAsync(
            string tool,
            IReadOnlyDictionary<string, JsonElement> arguments,
            CancellationToken cancellationToken) =>
            Task.FromResult(new McpToolCallResponse(
                tool,
                false,
                JsonSerializer.SerializeToElement(new { relatedOpenTickets = new[] { "open ticket" } }),
                DateTimeOffset.UtcNow));
    }

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubHandler(HttpStatusCode status, object? body) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = JsonContent.Create(body) };
        }
    }

    private sealed class ThrowingHandler(Type exceptionType) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>((Exception)Activator.CreateInstance(exceptionType)!);
    }
}
