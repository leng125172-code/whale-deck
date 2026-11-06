namespace WhaleDeck.Domain.Entities;

/// <summary>One explicitly confirmed Originals purge of a durable source. Reservation and irreversible Apply markers are distinct; source history is never overwritten.</summary>
public sealed class ContainerRecyclePurgeIntent
{
    public Guid Id { get; init; }
    public Guid SourceIntentId { get; init; }
    public required string SourceProofHash { get; init; }
    public Guid IdempotencyKey { get; init; }
    public required string ActorSubject { get; init; }
    public required string LoginBinding { get; init; }
    public required string TraceId { get; init; }
    public required string OwnerHash { get; init; }
    public required string ContainerId { get; init; }
    public required string InstanceId { get; init; }
    public required string RelativePath { get; init; }
    public required string CommandJson { get; init; }
    public required string RequestHash { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset PlanningExpiresAtUtc { get; init; }
    public string? PlanJson { get; set; }
    public string? PlanHash { get; set; }
    public DateTimeOffset? ReadyExpiresAtUtc { get; set; }
    public Guid? ExecutionJobId { get; set; }
    public DateTimeOffset? ConfirmedAtUtc { get; set; }
    public DateTimeOffset? ExecutionExpiresAtUtc { get; set; }
    public string? ExecutionHash { get; set; }
    public string State { get; set; } = "Queued";
    public Guid? LeaseToken { get; set; }
    public DateTimeOffset? LeaseExpiresAtUtc { get; set; }
    public DateTimeOffset? RootStartedAtUtc { get; set; }
    public bool PublishBarrier { get; set; }
    public DateTimeOffset? CancelRequestedAtUtc { get; set; }
    public DateTimeOffset NextAttemptAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public string? ResultCode { get; set; }
    public string? ReceiptJson { get; set; }
    public string? ReceiptHash { get; set; }
    public string? RootState { get; set; }
    public DateTimeOffset? ApplyStartedAtUtc { get; set; }
    public string? ReservationJson { get; set; }
    public string? ReservationHash { get; set; }
    public bool? PurgeApplied { get; set; }
    public bool? SourceFenced { get; set; }
    public bool? OwnedFence { get; set; }
    public string? AcknowledgedBy { get; set; }
    public string? AcknowledgementJson { get; set; }
    public long Version { get; set; }
}
