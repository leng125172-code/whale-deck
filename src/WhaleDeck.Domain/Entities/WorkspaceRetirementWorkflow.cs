namespace WhaleDeck.Domain.Entities;

// Independent execution workflow. A legacy preparation acknowledgement is
// deliberately not an execution confirmation or a dispatch admission.
public sealed class WorkspaceRetirementWorkflow
{
    public Guid Id { get; init; }
    public Guid WorkspaceId { get; init; }
    public Guid ArchiveId { get; init; }
    public long ExpectedEpoch { get; init; }
    public required string SnapshotJson { get; init; }
    public required string ArchivePlanSha256 { get; init; }
    public required string CaptureReceiptSha256 { get; init; }
    public required string ExecutionSubject { get; set; }
    public required string LoginBinding { get; set; }
    public string Phase { get; set; } = "plan";
    public string? PlanJson { get; set; }
    public DateTimeOffset? PlanExpiresAtUtc { get; set; }
    public DateTimeOffset? ConfirmedAtUtc { get; set; }
    public string? ExecutionJson { get; set; }
    public bool AuthorizationRequired { get; set; }
    public bool CancelRequested { get; set; }
    public bool AttentionRequired { get; set; }
    public int ExecutionAttempt { get; set; }
    public Guid? ExecuteStepId { get; set; }
    public Guid? Lease { get; set; }
    public DateTimeOffset? LeaseUntilUtc { get; set; }
    public DateTimeOffset AvailableAtUtc { get; set; }
    public DateTimeOffset? FinishedAtUtc { get; set; }
}

public sealed class WorkspaceRetirementStep
{
    public Guid Id { get; init; }
    public Guid RetirementId { get; init; }
    public required string Phase { get; init; }
    public int Attempt { get; init; }
    public required string RequestJson { get; init; }
    public DateTimeOffset? DispatchedAtUtc { get; set; }
    public DateTimeOffset? SettledAtUtc { get; set; }
    public string? ProofJson { get; set; }
}

// A recovery is a separate explicit authorization. The previous terminal
// task and proof remain immutable; the host engine binding stays unchanged.
public sealed class WorkspaceRetirementRecovery
{
    public Guid Id { get; init; }
    public Guid RetirementId { get; init; }
    public Guid PreviousStepId { get; init; }
    public Guid IdempotencyKey { get; init; }
    public int Attempt { get; init; }
    public required string ActorSubject { get; init; }
    public required string LoginBinding { get; init; }
    public required string PreviousProofSha256 { get; init; }
    public required string PlanSha256 { get; init; }
    public DateTimeOffset ConfirmedAtUtc { get; init; }
}

// The original workspace, requested budget and all audit remain historical
// facts. Only this paired, immutable proof releases application capacity.
public sealed class WorkspaceRetirementCompletion
{
    public Guid WorkspaceId { get; init; }
    public Guid RetirementId { get; init; }
    public Guid ExecuteStepId { get; init; }
    public DateTimeOffset CompletedAtUtc { get; init; }
}
