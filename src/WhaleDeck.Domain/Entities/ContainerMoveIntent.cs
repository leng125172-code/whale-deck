namespace WhaleDeck.Domain.Entities;

/// <summary>One permanent login-bound move request, with distinct planning and execution jobs.</summary>
public sealed class ContainerMoveIntent
{
    public Guid Id { get; init; }
    public Guid IdempotencyKey { get; init; }
    public required string ActorSubject { get; init; }
    public required string LoginBinding { get; init; }
    public required string TraceId { get; init; }
    public required string OwnerHash { get; init; }
    public required string ContainerId { get; init; }
    public required string InstanceId { get; init; }
    public required string SourcePath { get; init; }
    public required string TargetPath { get; init; }
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
    public string? PublishedVersion { get; set; }
    public bool PrivateMayExist { get; set; }
    // Null means unobserved. Never infer removal/retention from a missing reply.
    public bool? DestinationPublished { get; set; }
    public bool? SourceRemoved { get; set; }
    public bool? SourceRetained { get; set; }
    public string? AcknowledgedBy { get; set; }
    public string? AcknowledgementJson { get; set; }
    public long Version { get; set; }
}
