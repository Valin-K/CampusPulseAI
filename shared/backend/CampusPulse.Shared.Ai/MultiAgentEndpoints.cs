using System.Net.Http.Json;
using System.Text.Json;
using CampusPulse.Shared.MultiAgent;
using CampusPulse.Shared.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;

namespace CampusPulse.Shared.Ai;

public static class MultiAgentEndpoints
{
    public static IServiceCollection AddMultiAgentCaller(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddCampusPulseAuthorizationForwarding();
        services.AddHttpClient("multiAgent", client =>
        {
            client.BaseAddress = new Uri((configuration["Services:MultiAgentServer"] ?? "http://localhost:5270").TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromMinutes(6);
        }).ForwardCampusPulseAuthorization();
        return services;
    }

    public static IEndpointRouteBuilder MapMultiAgentCaller(
        this IEndpointRouteBuilder endpoints, string feature, string evidenceClient, string evidencePath)
    {
        if (!WorkflowFeatures.All.Contains(feature))
            throw new ArgumentException("Unknown multi-agent feature.", nameof(feature));

        var group = endpoints.MapGroup("/api/multi-agent");
        group.AddEndpointFilter(async (context, next) =>
        {
            var config = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
            var environment = context.HttpContext.RequestServices.GetRequiredService<IHostEnvironment>();
            if (!config.GetValue("MultiAgent:Enabled", environment.IsDevelopment()))
                return Results.Problem(statusCode: 403, title: "Multi-agent workflows are disabled in this deployment.");
            return await next(context);
        });
        group.MapGet("/workflows", (IHttpClientFactory clients, ILoggerFactory logs, CancellationToken ct) =>
            ForwardAsync(clients, logs, new HttpRequestMessage(HttpMethod.Get, $"api/workflows?feature={feature}"), ct));
        group.MapGet("/workflows/{id:guid}", (Guid id, IHttpClientFactory clients, ILoggerFactory logs, CancellationToken ct) =>
            ForwardAsync(clients, logs, new HttpRequestMessage(HttpMethod.Get, $"api/workflows/{id}?feature={feature}"), ct));
        group.MapGet("/workflows/{id:guid}/evidence", (Guid id, IHttpClientFactory clients, ILoggerFactory logs, CancellationToken ct) =>
            ForwardAsync(clients, logs, new HttpRequestMessage(HttpMethod.Get, $"api/workflows/{id}/evidence?feature={feature}"), ct));
        group.MapPost("/workflows/{id:guid}/review", (
            Guid id, HumanReviewRequest review, IHttpClientFactory clients, ILoggerFactory logs, CancellationToken ct) =>
            ForwardAsync(clients, logs, new HttpRequestMessage(HttpMethod.Post, $"api/workflows/{id}/review?feature={feature}")
            { Content = JsonContent.Create(review) }, ct));
        group.MapPost("/workflows", async (
            WorkflowStartRequest request, IHttpClientFactory clients, ILoggerFactory logs, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Task) || request.Task.Length > 4000
                || request.AdditionalEvidence?.Length > 20000)
                return Results.BadRequest(new { error = "A task of 1-4000 characters and optional evidence of at most 20000 characters are required." });
            try
            {
                using var response = await clients.CreateClient(evidenceClient).GetAsync(evidencePath, ct);
                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync(ct);
                using var document = JsonDocument.Parse(content);
                if (document.RootElement.ValueKind != JsonValueKind.Array)
                    throw new JsonException("The live catalogue API did not return an array.");
                if (content.Length > 100000)
                    return Results.Problem(statusCode: 502, title: "Application evidence exceeds the 100000-character workflow limit. Narrow the catalogue before retrying.");
                var evidence = new List<WorkflowEvidence>
                {
                    new("application-catalogue", $"{feature} live API {evidencePath}", content, DateTimeOffset.UtcNow)
                };
                if (!string.IsNullOrWhiteSpace(request.AdditionalEvidence))
                    evidence.Add(new("user-evidence", "User-supplied evidence (unverified)", request.AdditionalEvidence, DateTimeOffset.UtcNow));
                return await ForwardAsync(clients, logs, new HttpRequestMessage(HttpMethod.Post, "api/workflows")
                {
                    Content = JsonContent.Create(new WorkflowRequest(
                        feature, request.Task.Trim(), evidence, request.UseMcp, request.UseRag, request.AllowFallback))
                }, ct);
            }
            catch (Exception exception) when (exception is HttpRequestException or JsonException
                || exception is OperationCanceledException && !ct.IsCancellationRequested)
            {
                logs.CreateLogger("MultiAgentCaller").LogWarning(exception, "Live evidence collection failed for {Feature}.", feature);
                return Results.Problem(statusCode: 502, title: "Live application evidence could not be collected. No workflow was started.");
            }
        });
        return endpoints;
    }

    private static async Task<IResult> ForwardAsync(
        IHttpClientFactory clients, ILoggerFactory logs, HttpRequestMessage request, CancellationToken ct)
    {
        using (request)
        {
            try
            {
                using var response = await clients.CreateClient("multiAgent").SendAsync(request, ct);
                return Results.Content(await response.Content.ReadAsStringAsync(ct),
                    response.Content.Headers.ContentType?.ToString() ?? "application/json",
                    statusCode: (int)response.StatusCode);
            }
            catch (Exception exception) when (exception is HttpRequestException
                || exception is OperationCanceledException && !ct.IsCancellationRequested)
            {
                logs.CreateLogger("MultiAgentCaller").LogWarning(exception, "Multi-agent server call failed.");
                return Results.Problem(statusCode: 503, title: "The local multi-agent server is unreachable or timed out. Check workflow history before retrying.");
            }
        }
    }

}
