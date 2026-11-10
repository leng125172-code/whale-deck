namespace WhaleDeck.Domain.Entities;

// Durable preparation/acknowledgement. No physical dispatch or budget release
// is represented by this record; execution requires a separate future fence.
public sealed class WorkspaceRetirementIntent
{
    public Guid Id { get; init; }
    public Guid WorkspaceId { get; init; }
    public Guid ArchiveId { get; init; }
    public long ExpectedEpoch { get; init; }
    public required string SnapshotJson { get; init; }
    public required string ExecutionSubject { get; set; }
    public required string LoginBinding { get; set; }
    public string? PlanJson { get; set; }
    public DateTimeOffset? PlanExpiresAtUtc { get; set; }
    public DateTimeOffset? ConfirmedAtUtc { get; set; }
    public bool CancelRequested { get; set; }
    public Guid? Lease { get; set; }
    public DateTimeOffset? LeaseUntilUtc { get; set; }
    public DateTimeOffset AvailableAtUtc { get; set; }
    public DateTimeOffset? FinishedAtUtc { get; set; }
}
