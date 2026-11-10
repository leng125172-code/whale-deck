namespace WhaleDeck.Domain.Entities;

public sealed class WorkspaceArchiveIntent
{
    public Guid Id { get; init; }
    public Guid WorkspaceId { get; init; }
    public long ExpectedEpoch { get; init; }
    public required string SnapshotJson { get; init; }
    public string Phase { get; set; } = "plan";
    public bool AwaitingConfirmation { get; set; }
    public bool AuthorizationRequired { get; set; }
    public bool CancelRequested { get; set; }
    public required string ExecutionSubject { get; set; }
    public required string LoginBinding { get; set; }
    public string? PlanJson { get; set; }
    public string? CaptureJson { get; set; }
    public string? VerifyJson { get; set; }
    public Guid? Lease { get; set; }
    public DateTimeOffset? LeaseUntilUtc { get; set; }
    public DateTimeOffset AvailableAtUtc { get; set; }
    public DateTimeOffset? FinishedAtUtc { get; set; }
}

public sealed class WorkspaceArchiveStep
{
    public Guid Id { get; init; }
    public Guid ArchiveId { get; init; }
    public required string Phase { get; init; }
    public required string RequestJson { get; init; }
    public DateTimeOffset? DispatchedAtUtc { get; set; }
    public string? ProofJson { get; set; }
    public DateTimeOffset? SettledAtUtc { get; set; }
}
