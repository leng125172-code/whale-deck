namespace WhaleDeck.Domain.Entities;

/// <summary>Login-bound cleanup intent; original catalogue entries are immutable and never deleted by it.</summary>
public sealed class FileTreePurgingOperation
{
    public Guid Id { get; init; }
    public Guid CaptureId { get; init; }
    public required string ActorSubject { get; init; }
    public required string LoginBinding { get; init; }
    public required string IdempotencyKey { get; init; }
    public required string OwnerHash { get; init; }
    public required string RequestHash { get; init; }
    public required string SourceHash { get; init; }
    public required string CommandJson { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset PlanningExpiresAtUtc { get; init; }
    public string? PlanJson { get; set; }
    public string? PlanHash { get; set; }
    public DateTimeOffset? ReadyExpiresAtUtc { get; set; }
    public Guid? ExecutionJobId { get; set; }
    public DateTimeOffset? ExecutionExpiresAtUtc { get; set; }
    public string State { get; set; } = "Queued";
    public Guid? LeaseToken { get; set; }
    public DateTimeOffset? LeaseExpiresAtUtc { get; set; }
    public DateTimeOffset? RootStartedAtUtc { get; set; }
    public DateTimeOffset? CancelRequestedAtUtc { get; set; }
    public DateTimeOffset NextAttemptAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public string? ResultCode { get; set; }
    public string? ReceiptJson { get; set; }
    public string? ReceiptHash { get; set; }
    public DateTimeOffset? ReceiptObservedAtUtc { get; set; }
    public bool DeletionStarted { get; set; }
    public bool ArchiveRemoved { get; set; }
    public bool OriginalsRemoved { get; set; }
    public long Version { get; set; }
}
