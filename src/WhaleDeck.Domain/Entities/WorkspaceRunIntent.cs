namespace WhaleDeck.Domain.Entities;

public sealed class WorkspaceRunIntent
{
    public Guid Id { get; init; }
    public Guid WorkspaceId { get; init; }
    public required string Runtime { get; init; }
    public required string Task { get; init; }
    public required string SnapshotJson { get; init; }
    public required string LoginBinding { get; init; }
    public DateTimeOffset AvailableAtUtc { get; set; }
    public DateTimeOffset? FinishedAtUtc { get; set; }
    public Guid? Lease { get; set; }
    public DateTimeOffset? LeaseUntilUtc { get; set; }
    public string? ReportStage { get; set; }
}
