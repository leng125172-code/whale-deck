namespace WhaleDeck.Domain.Entities;

public sealed class WorkspaceLifecycleIntent
{
    public Guid Id { get; init; }
    public Guid WorkspaceId { get; init; }
    public required string Action { get; init; }
    public long ExpectedVersion { get; init; }
    public required string SnapshotJson { get; init; }
    public required string ExecutionSubject { get; set; }
    public required string LoginBinding { get; set; }
    public Guid? Lease { get; set; }
    public DateTimeOffset? LeaseUntilUtc { get; set; }
    public DateTimeOffset AvailableAtUtc { get; set; }
    public DateTimeOffset? FinishedAtUtc { get; set; }
}
