using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CampusPulse.Shared.Ai;
using CampusPulse.Shared.Mcp.Client;
using CampusPulse.Shared.Mcp.Contracts;
using CampusPulse.Shared.MultiAgent;
using CampusPulse.Shared.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MultiAgentServer.Tests;

public sealed class WorkflowTests
{
    private string directory = null!;
    private WorkflowStore store = null!;
    private RecordingAgents agents = null!;
    private PassthroughCollector collector = null!;
    private AuthorizationRequestContext requestAuthorization = null!;
    private WorkflowCoordinator coordinator = null!;

    [SetUp]
    public void Setup()
    {
        directory = Path.Combine(Path.GetTempPath(), $"campus-workflow-{Guid.NewGuid():N}");
        store = new WorkflowStore(Path.Combine(directory, "workflow.db"));
        agents = new RecordingAgents();
        requestAuthorization = new AuthorizationRequestContext();
        collector = new PassthroughCollector(requestAuthorization);
        coordinator = new WorkflowCoordinator(store, agents, collector, NullLogger<WorkflowCoordinator>.Instance, requestAuthorization);
    }

    [Test]
    public async Task WorkflowRoundsForwardTheCallerSessionWithoutPersistingIt()
    {
        var created = coordinator.Start(Request(), "Bearer student-session");
        await coordinator.ProcessAsync(created.Id, CancellationToken.None);
        var ready = store.Get(created.Id)!;
        var corrected = coordinator.Review(
            ready,
            new("correct", "Tutor", "Include course codes.", ready.Version),
            "Bearer staff-session");
        await coordinator.ProcessAsync(corrected.Id, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(collector.Authorizations, Is.EqualTo(new[] { "Bearer student-session", "Bearer staff-session" }));
            Assert.That(JsonSerializer.Serialize(store.Get(created.Id)), Does.Not.Contain("Bearer "));
        });
    }

    [TearDown]
    public void Cleanup()
    {
        coordinator.Dispose();
        Directory.Delete(directory, true);
    }

    private static WorkflowRequest Request(string feature = "coursemanagement") => new(
        feature, "Summarise available courses",
        [new("catalogue", "live API", """[{"course":"ASD101"}]""", DateTimeOffset.UtcNow)], false, false);

    [Test]
    public async Task WorkflowPassesPlanAndWorkSequentiallyAndNeverAutoApproves()
    {
        var created = coordinator.Start(Request());
        await coordinator.ProcessAsync(created.Id, CancellationToken.None);
        var result = store.Get(created.Id)!;
        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(WorkflowStatus.AwaitingReview));
            Assert.That(agents.Roles, Is.EqualTo(new[] { "planner", "worker", "reviewer" }));
            Assert.That(agents.InputAgentCounts, Is.EqualTo(new[] { 0, 1, 2 }));
            Assert.That(result.HumanReviews, Is.Empty);
            Assert.That(result.Agents, Has.Count.EqualTo(3));
            Assert.That(store.Audit(created.Id).Select(item => item.Event),
                Is.EqualTo(new[] { "workflow_created", "evidence_collected", "planner_started", "planner_completed",
                    "worker_started", "worker_completed", "reviewer_started", "reviewer_completed", "human_review_required" }));
        });
    }

    [TestCase("approve", WorkflowStatus.Approved)]
    [TestCase("reject", WorkflowStatus.Rejected)]
    public async Task HumanDecisionIsDurableAndCannotBeRepeated(string decision, string expected)
    {
        var created = coordinator.Start(Request());
        await coordinator.ProcessAsync(created.Id, CancellationToken.None);
        var ready = store.Get(created.Id)!;
        var reviewed = coordinator.Review(ready, new(decision, "Human reviewer", "Checked evidence", ready.Version));
        var reopened = new WorkflowStore(Path.Combine(directory, "workflow.db"));
        Assert.Multiple(() =>
        {
            Assert.That(reopened.Get(created.Id)!.Status, Is.EqualTo(expected));
            Assert.That(reviewed.HumanReviews.Single().Reviewer, Is.EqualTo("Human reviewer"));
            Assert.Throws<WorkflowConflictException>(() => coordinator.Review(
                ready, new(decision, "Another reviewer", "Stale", ready.Version)));
            Assert.Throws<WorkflowConflictException>(() => coordinator.Review(
                reviewed, new(decision, "Another reviewer", "Repeated", reviewed.Version)));
        });
    }

    [Test]
    public async Task CorrectionPreservesHistoryAndPassesFeedbackThroughANewRound()
    {
        var created = coordinator.Start(Request());
        await coordinator.ProcessAsync(created.Id, CancellationToken.None);
        var ready = store.Get(created.Id)!;
        var corrected = coordinator.Review(ready, new("correct", "Tutor", "Include course codes.", ready.Version));
        await coordinator.ProcessAsync(corrected.Id, CancellationToken.None);
        var result = store.Get(created.Id)!;
        Assert.Multiple(() =>
        {
            Assert.That(result.Round, Is.EqualTo(2));
            Assert.That(result.Agents, Has.Count.EqualTo(6));
            Assert.That(result.Status, Is.EqualTo(WorkflowStatus.AwaitingReview));
            Assert.That(agents.LastFeedback, Is.EqualTo("Include course codes."));
            Assert.That(store.Audit(result.Id).Count(item => item.Event == "human_correct"), Is.EqualTo(1));
        });
    }

    [TestCase("planner", 0)]
    [TestCase("worker", 1)]
    [TestCase("reviewer", 2)]
    public async Task FailedAgentIsRecordedWithoutFabricatedResultsOrHumanApproval(string failedRole, int completed)
    {
        agents.FailRole = failedRole;
        var created = coordinator.Start(Request());
        await coordinator.ProcessAsync(created.Id, CancellationToken.None);
        var result = store.Get(created.Id)!;
        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(WorkflowStatus.Failed));
            Assert.That(result.Agents, Has.Count.EqualTo(completed));
            Assert.That(result.Error, Does.Contain("provider unavailable"));
            Assert.That(result.HumanReviews, Is.Empty);
            Assert.That(store.Audit(result.Id).Last().Event, Is.EqualTo("workflow_failed"));
            Assert.Throws<WorkflowConflictException>(() => coordinator.Review(result, new("approve", "Human", "", result.Version)));
        });
    }

    [Test]
    public void InterruptedWorkflowsAreRecoveredAsFailures()
    {
        var created = coordinator.Start(Request());
        store.RecoverInterrupted();
        Assert.That(store.Get(created.Id)!.Status, Is.EqualTo(WorkflowStatus.Failed));
        Assert.That(store.Audit(created.Id).Last().Event, Is.EqualTo("interrupted"));
    }

    [Test]
    public void RejectsMissingDuplicateOrOversizedEvidence()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentException>(() => coordinator.Start(Request() with { Evidence = [] }));
            Assert.Throws<ArgumentException>(() => coordinator.Start(Request() with { Feature = "unknown" }));
            Assert.Throws<ArgumentException>(() => coordinator.Start(Request() with { Task = "" }));
            Assert.Throws<ArgumentException>(() => coordinator.Start(Request() with { Evidence = [Request().Evidence[0], Request().Evidence[0]] }));
            Assert.Throws<ArgumentException>(() => coordinator.Start(Request() with
            { Evidence = [new("catalogue", "API", new string('x', 120001), DateTimeOffset.UtcNow)] }));
        });
    }

    [Test]
    public async Task ServerHttpEndpointsReturnAcceptedConflictNotFoundAndExportAudit()
    {
        await using var server = await StartServerAsync(true);
        using var http = CreateClient(server);
        using var response = await http.PostAsJsonAsync("/api/workflows", Request());
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
        var workflow = (await response.Content.ReadFromJsonAsync<Workflow>())!;
        await coordinator.ProcessAsync(workflow.Id, CancellationToken.None);
        var ready = store.Get(workflow.Id)!;
        using var invalid = await http.PostAsJsonAsync(
            $"/api/workflows/{workflow.Id}/review?feature=coursemanagement", new HumanReviewRequest("approve", "Human", "", 1));
        Assert.That(invalid.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        using var approved = await http.PostAsJsonAsync(
            $"/api/workflows/{workflow.Id}/review?feature=coursemanagement", new HumanReviewRequest("approve", "Human", "", ready.Version));
        Assert.That(approved.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        using var crossFeature = await http.GetAsync($"/api/workflows/{workflow.Id}?feature=campushousing");
        Assert.That(crossFeature.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        var export = await http.GetFromJsonAsync<WorkflowEvidenceExport>($"/api/workflows/{workflow.Id}/evidence?feature=coursemanagement");
        Assert.That(export!.Workflow.Status, Is.EqualTo(WorkflowStatus.Approved));
        Assert.That(export.CoordinationAudit.Last().Event, Is.EqualTo("human_approve"));
        using var bad = await http.PostAsJsonAsync("/api/workflows", Request() with { Evidence = [] });
        Assert.That(bad.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task ServerIsDisabledByDefault()
    {
        await using var server = await StartServerAsync(false);
        using var http = CreateClient(server);
        using var response = await http.PostAsJsonAsync("/api/workflows", Request());
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That(store.List(), Is.Empty);
    }

    [TestCase("academicrecords")]
    [TestCase("coursemanagement")]
    [TestCase("campushousing")]
    [TestCase("financialaidbilling")]
    [TestCase("eventscheduling")]
    public async Task BackendCallerCollectsLiveEvidenceAndScopesFeature(string feature)
    {
        await using var server = await StartServerAsync(true);
        await using var backend = await StartCallerAsync(feature, server, true);
        using var http = CreateClient(backend);
        using var response = await http.PostAsJsonAsync("/api/multi-agent/workflows",
            new WorkflowStartRequest("Summarise the catalogue", "Human supplied note", false, false));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
        var created = (await response.Content.ReadFromJsonAsync<Workflow>())!;
        Assert.Multiple(() =>
        {
            Assert.That(created.Feature, Is.EqualTo(feature));
            Assert.That(created.Evidence.Select(item => item.Id), Is.EqualTo(new[] { "application-catalogue", "user-evidence" }));
            Assert.That(created.Evidence[0].Content, Does.Contain("ASD101"));
        });
        await coordinator.ProcessAsync(created.Id, CancellationToken.None);
        var ready = store.Get(created.Id)!;
        using var reviewed = await http.PostAsJsonAsync($"/api/multi-agent/workflows/{created.Id}/review",
            new HumanReviewRequest("approve", "Caller human", "", ready.Version));
        Assert.That(reviewed.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var history = await http.GetFromJsonAsync<List<Workflow>>("/api/multi-agent/workflows");
        Assert.That(history!.Single().Feature, Is.EqualTo(feature));
        var export = await http.GetFromJsonAsync<WorkflowEvidenceExport>($"/api/multi-agent/workflows/{created.Id}/evidence");
        Assert.That(export!.CoordinationAudit.Last().Event, Is.EqualTo("human_approve"));
    }

    [Test]
    public async Task DisabledCallerDoesNotCollectEvidenceOrStartWorkflows()
    {
        await using var server = await StartServerAsync(true);
        await using var backend = await StartCallerAsync("coursemanagement", server, false);
        using var http = CreateClient(backend);
        using var response = await http.PostAsJsonAsync("/api/multi-agent/workflows", new WorkflowStartRequest("Task"));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That(store.List(), Is.Empty);
    }

    private async Task<WebApplication> StartServerAsync(bool enabled)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        if (enabled) builder.Configuration["MultiAgent:Enabled"] = "true";
        builder.Services.AddSingleton(store);
        builder.Services.AddSingleton(coordinator);
        var app = builder.Build();
        app.MapWorkflowApi();
        app.MapGet("/catalogue", () => new[] { new { course = "ASD101" } });
        await app.StartAsync();
        return app;
    }

    private static async Task<WebApplication> StartCallerAsync(string feature, WebApplication server, bool enabled)
    {
        using var serverClient = CreateClient(server);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Configuration["Services:MultiAgentServer"] = serverClient.BaseAddress!.ToString();
        builder.Configuration["MultiAgent:Enabled"] = enabled.ToString();
        builder.Services.AddMultiAgentCaller(builder.Configuration);
        builder.Services.AddHttpClient("catalogue", client => client.BaseAddress = serverClient.BaseAddress);
        var app = builder.Build();
        app.MapMultiAgentCaller(feature, "catalogue", "/catalogue");
        await app.StartAsync();
        return app;
    }

    private static HttpClient CreateClient(WebApplication app) => new()
    {
        BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single())
    };

    private sealed class PassthroughCollector(AuthorizationRequestContext requestAuthorization) : IWorkflowEvidenceCollector
    {
        public List<string?> Authorizations { get; } = [];

        public Task<Workflow> CollectAsync(Workflow workflow, CancellationToken ct)
        {
            Authorizations.Add(requestAuthorization.CurrentAuthorization);
            return Task.FromResult(workflow);
        }
    }

    private sealed class RecordingAgents : IWorkflowAgents
    {
        public List<string> Roles { get; } = [];
        public List<int> InputAgentCounts { get; } = [];
        public string? LastFeedback { get; private set; }
        public string? FailRole { get; set; }
        public Task<AgentExecution> ExecuteAsync(string role, Workflow workflow, CancellationToken ct)
        {
            Roles.Add(role);
            InputAgentCounts.Add(workflow.Agents.Count);
            LastFeedback = workflow.HumanReviews.LastOrDefault()?.Comments;
            if (FailRole == role) throw new HttpRequestException("provider unavailable");
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(new AgentExecution(role, workflow.Round, "test-model", now, now,
                Plan: role == "planner" ? new("Summarise", ["Read catalogue"], ["catalogue"]) : null,
                Work: role == "worker" ? new("ASD101 [catalogue]", [workflow.Evidence[0].Id], []) : null,
                Review: role == "reviewer" ? new("approve", [], [], [], [workflow.Evidence[0].Id]) : null));
        }
    }
}
