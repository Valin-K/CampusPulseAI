using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using CampusPulse.Shared.Components;
using CampusPulse.Shared.Components.Services;
using CampusPulse.Shared.MultiAgent;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace AcademicRecords.Tests;

public sealed class CampusMultiAgentEvidenceTests
{
    private static readonly Workflow Workflow = new(
        Guid.Parse("de17c333-e4ea-4496-8c92-ec7f86a8d999"), "academicrecords", "Summarise courses",
        WorkflowStatus.Approved, 1, 4, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
        [], [], [], []);
    private static readonly WorkflowEvidenceExport Export = new(Workflow,
        [new(1, Workflow.Id, Workflow.Feature, 4, 1, "human-approved", Workflow.Status, DateTimeOffset.UnixEpoch)]);

    [TestCase("http://localhost:8081/")]
    [TestCase("https://academicrecords-web.example.azurecontainerapps.io/")]
    public async Task EvidenceUsesTheAuthenticatedClientAndDisplaysTheCompleteExport(string baseUrl)
    {
        using var handler = new EvidenceHandler();
        using var client = CreateClient(baseUrl, handler);
        var activator = new ComponentActivator();
        await using var provider = CreateServices(client, activator);
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var root = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<CampusMultiAgent>());

        await ClickAsync(renderer, activator.Component, "LoadEvidenceAsync");
        var html = await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);

        Assert.Multiple(() =>
        {
            Assert.That(handler.Requests, Is.EqualTo(new[]
            {
                (new Uri(new Uri(baseUrl), $"/api/multi-agent/workflows/{Workflow.Id}/evidence"), "Bearer evidence-test-token")
            }));
            Assert.That(html, Does.Contain("Workflow and coordination audit evidence"));
            Assert.That(html, Does.Contain("human-approved"));
            Assert.That(html, Does.Contain(Workflow.Id.ToString()));
            Assert.That(html, Does.Not.Contain("target=\"_blank\""));
            Assert.That(html, Does.Not.Contain("evidence-test-token"));
        });
        var json = (string)typeof(CampusMultiAgent).GetField("evidenceJson", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(activator.Component)!;
        var result = JsonSerializer.Deserialize<WorkflowEvidenceExport>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.That(result!.Workflow.Id, Is.EqualTo(Workflow.Id));
        Assert.That(result.CoordinationAudit.Single(), Is.EqualTo(Export.CoordinationAudit.Single()));
    }

    [TestCase(HttpStatusCode.Unauthorized, "Sign in again")]
    [TestCase(HttpStatusCode.Forbidden, "Multi-agent workflows are disabled in this deployment.")]
    [TestCase(HttpStatusCode.ServiceUnavailable, "The multi-agent server is unreachable.")]
    public async Task FailedEvidenceReloadClearsThePreviousExportAndShowsTheError(HttpStatusCode status, string message)
    {
        using var handler = new EvidenceHandler();
        using var client = CreateClient("https://academicrecords-web.example.azurecontainerapps.io/", handler);
        var activator = new ComponentActivator();
        await using var provider = CreateServices(client, activator);
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var root = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<CampusMultiAgent>());
        await ClickAsync(renderer, activator.Component, "LoadEvidenceAsync");
        Assert.That(await renderer.Dispatcher.InvokeAsync(root.ToHtmlString), Does.Contain("human-approved"));

        handler.Status = status;
        handler.Error = message;
        await ClickAsync(renderer, activator.Component, "LoadEvidenceAsync");
        var html = await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("role=\"alert\""));
            Assert.That(html, Does.Contain(message));
            Assert.That(html, Does.Not.Contain("human-approved"));
            Assert.That(html, Does.Not.Contain("Close evidence"));
        });
    }

    [Test]
    public async Task RefreshingTheWorkflowClearsThePreviouslyViewedEvidence()
    {
        using var handler = new EvidenceHandler();
        using var client = CreateClient("http://localhost:8081/", handler);
        var activator = new ComponentActivator();
        await using var provider = CreateServices(client, activator);
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var root = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<CampusMultiAgent>());
        await ClickAsync(renderer, activator.Component, "LoadEvidenceAsync");
        Assert.That(await renderer.Dispatcher.InvokeAsync(root.ToHtmlString), Does.Contain("human-approved"));

        await ClickAsync(renderer, activator.Component, "ResumeAsync");
        var html = await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);

        Assert.That(html, Does.Not.Contain("human-approved"));
        Assert.That(html, Does.Not.Contain("Close evidence"));
        Assert.That(html, Does.Contain(Workflow.Id.ToString()));
    }

    private static HttpClient CreateClient(string baseUrl, EvidenceHandler handler) => new(new TokenHandler(new SessionJsRuntime())
    {
        InnerHandler = handler
    }) { BaseAddress = new Uri(baseUrl) };

    private static ServiceProvider CreateServices(HttpClient client, ComponentActivator activator) => new ServiceCollection()
        .AddLogging().AddSingleton(client).AddSingleton<IComponentActivator>(activator).BuildServiceProvider();

    private static Task ClickAsync(HtmlRenderer renderer, CampusMultiAgent component, string method) =>
        renderer.Dispatcher.InvokeAsync(() => EventCallback.Factory.Create(component,
            (Func<Task>)typeof(CampusMultiAgent).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
                .CreateDelegate(typeof(Func<Task>), component)).InvokeAsync());

    private sealed class ComponentActivator : IComponentActivator
    {
        public CampusMultiAgent Component { get; private set; } = null!;

        public IComponent CreateInstance(Type componentType)
        {
            Component = new CampusMultiAgent();
            typeof(CampusMultiAgent).GetField("workflow", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(Component, Workflow);
            return Component;
        }
    }

    private sealed class SessionJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            Assert.That(identifier, Is.EqualTo("sessionStorage.getItem"));
            Assert.That(args, Is.EqualTo(new object[] { TokenHandler.SessionStorageKey }));
            return ValueTask.FromResult((TValue)(object)"{\"accessToken\":\"evidence-test-token\"}");
        }
    }

    private sealed class EvidenceHandler : HttpMessageHandler
    {
        public List<(Uri Uri, string? Authorization)> Requests { get; } = [];
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public string Error { get; set; } = "";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.RequestUri!, request.Headers.Authorization?.ToString()));
            return Task.FromResult(new HttpResponseMessage(Status)
            {
                Content = Status == HttpStatusCode.Unauthorized ? new StringContent("")
                    : Status != HttpStatusCode.OK ? JsonContent.Create(new { title = Error })
                    : request.RequestUri!.AbsolutePath.EndsWith("/evidence", StringComparison.Ordinal)
                        ? JsonContent.Create(Export) : JsonContent.Create(Workflow)
            });
        }
    }
}