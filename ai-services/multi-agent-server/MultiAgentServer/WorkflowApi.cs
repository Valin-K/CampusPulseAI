using CampusPulse.Shared.MultiAgent;

namespace MultiAgentServer;

public static class WorkflowApi
{
    public static IEndpointRouteBuilder MapWorkflowApi(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api/workflows");
        api.AddEndpointFilter(async (context, next) =>
        {
            if (!context.HttpContext.RequestServices.GetRequiredService<IConfiguration>().GetValue("MultiAgent:Enabled", false))
                return Results.Problem(statusCode: 403, title: "Multi-agent workflows are disabled in this deployment.");
            try { return await next(context); }
            catch (ArgumentException exception) { return Results.BadRequest(new { error = exception.Message }); }
            catch (WorkflowConflictException exception) { return Results.Conflict(new { error = exception.Message }); }
        });
        api.MapPost("/", (WorkflowRequest request, WorkflowCoordinator coordinator, HttpContext context) =>
        {
            var workflow = coordinator.Start(request, context.Request.Headers.Authorization.ToString());
            return Results.Json(workflow, statusCode: workflow.Status == WorkflowStatus.Failed ? 503 : 202);
        });
        api.MapGet("/", (string feature, WorkflowStore repository) =>
            WorkflowFeatures.All.Contains(feature)
                ? Results.Ok(repository.List(feature))
                : Results.BadRequest(new { error = "A supported feature query is required." }));
        api.MapGet("/{id:guid}", (Guid id, string feature, WorkflowStore repository) =>
            Find(repository, id, feature) is { } workflow ? Results.Ok(workflow) : NotFound());
        api.MapGet("/{id:guid}/evidence", (Guid id, string feature, WorkflowStore repository) =>
            Find(repository, id, feature) is { } workflow
                ? Results.Ok(new WorkflowEvidenceExport(workflow, repository.Audit(id))) : NotFound());
        api.MapPost("/{id:guid}/review", (Guid id, string feature, HumanReviewRequest request,
            WorkflowStore repository, WorkflowCoordinator coordinator, HttpContext context) =>
        {
            var workflow = Find(repository, id, feature);
            if (workflow is null) return NotFound();
            var reviewed = coordinator.Review(workflow, request, context.Request.Headers.Authorization.ToString());
            return Results.Json(reviewed, statusCode: reviewed.Status == WorkflowStatus.Failed ? 503
                : reviewed.Status == WorkflowStatus.Running ? 202 : 200);
        });
        return endpoints;
    }

    private static Workflow? Find(WorkflowStore repository, Guid id, string feature) =>
        repository.Get(id) is { } workflow && workflow.Feature == feature ? workflow : null;

    private static IResult NotFound() => Results.Problem(statusCode: 404, title: "This workflow was not found for the requested feature.");
}
