namespace CampusPulse.Shared.MultiAgent;

public static class WorkflowFeatures
{
    public static readonly IReadOnlyList<string> All =
        ["academicrecords", "coursemanagement", "campushousing", "financialaidbilling", "eventscheduling"];
}

public static class WorkflowStatus
{
    public const string Running = "running";
    public const string AwaitingReview = "awaiting_human_review";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Failed = "failed";
}

public sealed record WorkflowEvidence(string Id, string Source, string Content, DateTimeOffset CollectedAtUtc);
public sealed record WorkflowRequest(
    string Feature, string Task, IReadOnlyList<WorkflowEvidence> Evidence,
    bool UseMcp = true, bool UseRag = true, bool AllowFallback = true);
public sealed record WorkflowStartRequest(
    string Task, string? AdditionalEvidence = null,
    bool UseMcp = true, bool UseRag = true, bool AllowFallback = true);
public sealed record HumanReviewRequest(string Decision, string Reviewer, string Comments, int ExpectedVersion);
public sealed record HumanReview(string Decision, string Reviewer, string Comments, int Round, DateTimeOffset AtUtc);
public sealed record DependencyResult(string Service, string Status, string Detail);
public sealed record PlannerOutput(string Objective, IReadOnlyList<string> Steps, IReadOnlyList<string> EvidenceRequired);
public sealed record WorkerOutput(string Output, IReadOnlyList<string> EvidenceUsed, IReadOnlyList<string> Limitations);
public sealed record ReviewerOutput(
    string Recommendation, IReadOnlyList<string> Risks, IReadOnlyList<string> Corrections,
    IReadOnlyList<string> Retest, IReadOnlyList<string> EvidenceChecked);
public sealed record AgentExecution(
    string Agent, int Round, string Model, DateTimeOffset StartedAtUtc, DateTimeOffset CompletedAtUtc,
    PlannerOutput? Plan = null, WorkerOutput? Work = null, ReviewerOutput? Review = null);
public sealed record Workflow(
    Guid Id, string Feature, string Task, string Status, int Round, int Version,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<WorkflowEvidence> Evidence, IReadOnlyList<DependencyResult> Dependencies,
    IReadOnlyList<AgentExecution> Agents, IReadOnlyList<HumanReview> HumanReviews, string? Error = null,
    bool UseMcp = true, bool UseRag = true, bool AllowFallback = true);
public sealed record WorkflowAudit(
    long Sequence, Guid WorkflowId, string Feature, int Version, int Round,
    string Event, string Status, DateTimeOffset AtUtc);
public sealed record WorkflowEvidenceExport(
    Workflow Workflow, IReadOnlyList<WorkflowAudit> CoordinationAudit);
