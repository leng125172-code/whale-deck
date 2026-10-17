namespace WhaleDeck.Domain.Entities;

/// <summary>Independent current-admin disposition. Original identity/intent and Root receipts stay unchanged.</summary>
public sealed class FileRecoveryOperation
{
    public Guid Id { get; init; }
    public required string ActorSubject { get; init; }
    public required string LoginBinding { get; init; }
    public required string IdempotencyKey { get; init; }
    public required string Kind { get; init; }
    public Guid OriginalIntentId { get; init; }
    public Guid OriginalExecutionJobId { get; init; }
    public long OriginalVersion { get; init; }
    public required string OriginalBindingHash { get; init; }
    public required string OriginalJson { get; init; }
    public required string Disposition { get; init; }
    public required string ReasonCode { get; init; }
    public required string RequestHash { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset PlanningExpiresAtUtc { get; init; }
    public string? RootState { get; set; }
    public string? ObservationHash { get; set; }
    public string? ObservationJson { get; set; }
    public bool? ObservationBindingCurrent { get; set; }
    public DateTimeOffset? ReadyAtUtc { get; set; }
    public DateTimeOffset? ReadyExpiresAtUtc { get; set; }
    public string? PlanHash { get; set; }
    public Guid? ExecutionJobId { get; set; }
    public DateTimeOffset? ExecutionExpiresAtUtc { get; set; }
    public DateTimeOffset? FenceStartedAtUtc { get; set; }
    public string State { get; set; } = "Queued";
    public Guid? LeaseToken { get; set; }
    public DateTimeOffset? LeaseExpiresAtUtc { get; set; }
    public DateTimeOffset? CancelRequestedAtUtc { get; set; }
    public DateTimeOffset NextAttemptAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public string? ResultCode { get; set; }
    public string? OriginalResolution { get; set; }
    public long Version { get; set; }
}
