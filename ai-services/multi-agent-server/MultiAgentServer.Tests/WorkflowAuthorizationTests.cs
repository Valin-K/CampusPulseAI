using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CampusPulse.Shared.Mcp.Contracts;
using CampusPulse.Shared.MultiAgent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MultiAgentServer.Tests;

public sealed class WorkflowAuthorizationTests
{
    private readonly ConcurrentQueue<(string Path, string? Authorization)> requests = new();
    private readonly ConcurrentQueue<string> modelInputs = new();
    private string directory = null!;
    private WebApplication app = null!;
    private HttpClient http = null!;

    [SetUp]
    public async Task Setup()
    {
        requests.Clear();
        modelInputs.Clear();
        directory = Path.Combine(Path.GetTempPath(), $"campus-workflow-auth-{Guid.NewGuid():N}");
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Configuration["MultiAgent:Enabled"] = "true";
        foreach (var name in new[] { "workflowMcp", "workflowRag" })
            builder.Services.AddHttpClient(name, client => client.BaseAddress = new Uri("http://dependency.test/"))
                .ConfigurePrimaryHttpMessageHandler(() => new DependencyHandler(requests));
        builder.Services.AddSingleton(new WorkflowStore(Path.Combine(directory, "workflow.db")));
        builder.Services.AddSingleton<IWorkflowEvidenceCollector, WorkflowEvidenceCollector>();
        builder.Services.AddSingleton<IWorkflowAgents>(new RecordingAgents(modelInputs));
        builder.Services.AddSingleton<WorkflowCoordinator>();
        builder.Services.AddHostedService(provider => provider.GetRequiredService<WorkflowCoordinator>());
        app = builder.Build();
        app.MapWorkflowApi();
        await app.StartAsync();
        http = new HttpClient
        {
            BaseAddress = new Uri(app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!.Addresses.Single())
        };
    }

    [TearDown]
    public async Task Cleanup()
    {
        http.Dispose();
        await app.DisposeAsync();
        Directory.Delete(directory, true);
    }

    [Test]
    public async Task QueuedJobsAndCorrectionsUseOnlyTheirOwnTokenWithoutPersistingOrSendingItToModelsOrRag()
    {
        const string firstToken = "Bearer first-caller-token";
        const string secondToken = "Bearer second-caller-token";
        const string correctionToken = "Bearer refreshed-reviewer-token";
        var first = await StartAsync(firstToken);
        var second = await StartAsync(secondToken);
        var anonymous = await StartAsync(null);
        var firstReady = await WaitAsync(first.Id);
        var secondReady = await WaitAsync(second.Id);
        var anonymousReady = await WaitAsync(anonymous.Id);

        using var correction = new HttpRequestMessage(HttpMethod.Post,
            $"api/workflows/{first.Id}/review?feature=coursemanagement")
        {
            Content = JsonContent.Create(new HumanReviewRequest("correct", "Tutor", "Recheck evidence", firstReady.Version))
        };
        correction.Headers.Authorization = System.Net.Http.Headers.AuthenticationHeaderValue.Parse(correctionToken);
        using var response = await http.SendAsync(correction);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
        var corrected = await WaitAsync(first.Id);
        var export = await http.GetStringAsync($"api/workflows/{first.Id}/evidence?feature=coursemanagement");

        Assert.Multiple(() =>
        {
            Assert.That(firstReady.Status, Is.EqualTo(WorkflowStatus.AwaitingReview));
            Assert.That(secondReady.Status, Is.EqualTo(WorkflowStatus.AwaitingReview));
            Assert.That(anonymousReady.Status, Is.EqualTo(WorkflowStatus.Failed));
            Assert.That(anonymousReady.Error, Is.EqualTo("MCP evidence is required but unavailable."));
            Assert.That(corrected.Status, Is.EqualTo(WorkflowStatus.AwaitingReview));
            Assert.That(corrected.Round, Is.EqualTo(2));
            Assert.That(corrected.Agents, Has.Count.EqualTo(6));
            Assert.That(requests.Where(item => item.Path == "/api/mcp/tools/call").Select(item => item.Authorization),
                Is.EqualTo(new string?[] { firstToken, secondToken, null, correctionToken }));
            Assert.That(requests.Where(item => item.Path.EndsWith("/retrieve", StringComparison.Ordinal))
                .Select(item => item.Authorization), Is.EqualTo(new string?[] { null, null, null }));
            Assert.That(modelInputs, Has.Count.EqualTo(9));
            foreach (var token in new[] { "first-caller-token", "second-caller-token", "refreshed-reviewer-token" })
            {
                Assert.That(export, Does.Not.Contain(token));
                Assert.That(string.Join("\n", modelInputs), Does.Not.Contain(token));
                Assert.That(JsonSerializer.Serialize(app.Services.GetRequiredService<WorkflowStore>().List()), Does.Not.Contain(token));
            }
        });
    }

    [TestCase("Bearer")]
    [TestCase("Basic abc")]
    public async Task InvalidCredentialsReturnBadRequestWithoutEnqueuingAJob(string authorization)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/workflows") { Content = JsonContent.Create(Request()) };
        request.Headers.TryAddWithoutValidation("Authorization", authorization);
        using var response = await http.SendAsync(request);
        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(app.Services.GetRequiredService<WorkflowStore>().List(), Is.Empty);
            Assert.That(requests, Is.Empty);
        });
    }

    private async Task<Workflow> StartAsync(string? authorization)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/workflows") { Content = JsonContent.Create(Request()) };
        if (authorization is not null)
            request.Headers.Authorization = System.Net.Http.Headers.AuthenticationHeaderValue.Parse(authorization);
        using var response = await http.SendAsync(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
        return (await response.Content.ReadFromJsonAsync<Workflow>())!;
    }

    private async Task<Workflow> WaitAsync(Guid id)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            var workflow = (await http.GetFromJsonAsync<Workflow>(
                $"api/workflows/{id}?feature=coursemanagement", timeout.Token))!;
            if (workflow.Status != WorkflowStatus.Running) return workflow;
            await Task.Delay(10, timeout.Token);
        }
    }

    private static WorkflowRequest Request() => new("coursemanagement", "Summarise ASD101",
        [new("catalogue", "Live API", "ASD101 is available.", DateTimeOffset.UtcNow)], AllowFallback: false);

    private sealed class DependencyHandler(ConcurrentQueue<(string Path, string? Authorization)> requests) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            requests.Enqueue((path, request.Headers.Authorization?.ToString()));
            if (path == "/api/mcp/tools/call")
            {
                if (request.Headers.Authorization is null) return new HttpResponseMessage(HttpStatusCode.Unauthorized);
                var call = await request.Content!.ReadFromJsonAsync<JsonElement>(ct);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new McpToolCallResponse(call.GetProperty("tool").GetString()!, false,
                        JsonSerializer.SerializeToElement(new[] { new { code = "ASD101" } }), DateTimeOffset.UtcNow))
                };
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    collection = "coursemanagement",
                    sources = new[] { new { documentId = "policy", chunkIndex = 0, score = 0.9, text = "ASD101 policy" } }
                })
            };
        }
    }

    private sealed class RecordingAgents(ConcurrentQueue<string> inputs) : IWorkflowAgents
    {
        public Task<AgentExecution> ExecuteAsync(string role, Workflow workflow, CancellationToken ct)
        {
            inputs.Enqueue(JsonSerializer.Serialize(workflow));
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(new AgentExecution(role, workflow.Round, "test-model", now, now,
                Plan: role == "planner" ? new("Summarise", ["Read catalogue"], ["catalogue"]) : null,
                Work: role == "worker" ? new("ASD101 [catalogue]", ["catalogue"], []) : null,
                Review: role == "reviewer" ? new("approve", [], [], [], ["catalogue"]) : null));
        }
    }
}