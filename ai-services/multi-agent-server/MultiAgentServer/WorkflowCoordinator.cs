using System.Threading.Channels;
using CampusPulse.Shared.MultiAgent;

namespace MultiAgentServer;

public sealed class WorkflowCoordinator(
    WorkflowStore store, IWorkflowAgents agents, IWorkflowEvidenceCollector collector,
    ILogger<WorkflowCoordinator> logger) : BackgroundService
{
    private readonly Channel<Guid> queue = Channel.CreateBounded<Guid>(100);

    public Workflow Start(WorkflowRequest request)
    {
        Validate(request);
        var now = DateTimeOffset.UtcNow;
        var workflow = store.Save(new Workflow(Guid.NewGuid(), request.Feature, request.Task.Trim(),
            WorkflowStatus.Running, 1, 0, now, now, request.Evidence, [], [], [],
            UseMcp: request.UseMcp, UseRag: request.UseRag, AllowFallback: request.AllowFallback), "workflow_created");
        return Enqueue(workflow);
    }

    public static void Validate(WorkflowRequest request)
    {
        if (!WorkflowFeatures.All.Contains(request.Feature) || string.IsNullOrWhiteSpace(request.Task) || request.Task.Length > 4000
            || request.Evidence is null || request.Evidence.Count is < 1 or > 20
            || request.Evidence.Any(item => item is null || string.IsNullOrWhiteSpace(item.Id)
                || item.Id.Length > 100 || item.Id.StartsWith("mcp-", StringComparison.Ordinal)
                || item.Id.StartsWith("rag-", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(item.Source)
                || item.Source.Length > 1000 || string.IsNullOrWhiteSpace(item.Content))
            || request.Evidence.Sum(item => (long)item.Content.Length) > 120000
            || request.Evidence.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != request.Evidence.Count)
            throw new ArgumentException("A supported feature, task (1-4000 characters), and 1-20 uniquely identified evidence items (at most 120000 characters) are required. mcp-/rag- IDs are reserved.");
    }

    public Workflow Review(Workflow workflow, HumanReviewRequest request)
    {
        if (request.Decision is not ("approve" or "correct" or "reject")
            || string.IsNullOrWhiteSpace(request.Reviewer) || request.Reviewer.Length > 100
            || request.Comments is null || request.Comments.Length > 4000
            || request.Decision != "approve" && string.IsNullOrWhiteSpace(request.Comments))
            throw new ArgumentException("Use approve, correct, or reject; supply a reviewer (1-100 characters) and comments (required for correction/rejection, at most 4000 characters).");
        if (workflow.Status != WorkflowStatus.AwaitingReview || workflow.Version != request.ExpectedVersion)
            throw new WorkflowConflictException("Only the current awaiting-human-review version can be reviewed. Reload the workflow.");
        var decision = new HumanReview(request.Decision, request.Reviewer.Trim(), request.Comments.Trim(), workflow.Round, DateTimeOffset.UtcNow);
        var next = workflow with
        {
            Status = request.Decision switch
            {
                "approve" => WorkflowStatus.Approved,
                "reject" => WorkflowStatus.Rejected,
                _ => WorkflowStatus.Running
            },
            HumanReviews = [.. workflow.HumanReviews, decision],
            Round = request.Decision == "correct" ? workflow.Round + 1 : workflow.Round
        };
        next = store.Save(next, $"human_{request.Decision}");
        return request.Decision == "correct" ? Enqueue(next) : next;
    }

    private Workflow Enqueue(Workflow workflow)
    {
        if (queue.Writer.TryWrite(workflow.Id)) return workflow;
        return store.Save(workflow with { Status = WorkflowStatus.Failed, Error = "The workflow queue is full. Retry later." }, "queue_full");
    }

    public async Task ProcessAsync(Guid id, CancellationToken ct)
    {
        var workflow = store.Get(id) ?? throw new InvalidOperationException("Queued workflow was not found.");
        try
        {
            workflow = store.Save(await collector.CollectAsync(workflow, ct), "evidence_collected");
            foreach (var role in new[] { "planner", "worker", "reviewer" })
            {
                workflow = store.Save(workflow, $"{role}_started");
                var result = await agents.ExecuteAsync(role, workflow, ct);
                workflow = store.Save(workflow with { Agents = [.. workflow.Agents, result] }, $"{role}_completed");
            }
            store.Save(workflow with { Status = WorkflowStatus.AwaitingReview }, "human_review_required");
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidDataException or OperationCanceledException)
        {
            logger.LogWarning(exception, "Workflow {WorkflowId} failed in round {Round}.", id, workflow.Round);
            store.Save(workflow with
            {
                Status = WorkflowStatus.Failed,
                Error = exception is OperationCanceledException
                    ? "The workflow was cancelled or an agent timed out. No human approval was recorded."
                    : exception.Message
            }, "workflow_failed");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var id in queue.Reader.ReadAllAsync(stoppingToken))
            await ProcessAsync(id, stoppingToken);
    }
}
