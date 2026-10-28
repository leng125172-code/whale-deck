namespace WhaleDeck.Domain.Entities;

/// <summary>Permanent human/login-bound mkdir intent. Bytes, tokens and credentials never enter SQL.</summary>
public sealed class ContainerDirectoryOperation
{
    public Guid Id { get; init; }
    public Guid IdempotencyKey { get; init; }
    public required string ActorSubject { get; init; }
    public required string LoginBinding { get; init; }
    public required string TraceId { get; init; }
    public required string OwnerHash { get; init; }
    public required string ContainerId { get; init; }
    public required string InstanceId { get; init; }
    public required string RelativePath { get; init; }
    public required string PlanJson { get; init; }
    public required string PlanHash { get; init; }
    public required string RequestHash { get; init; }
    public string State { get; set; } = "Pending";
    public Guid? LeaseToken { get; set; }
    public DateTimeOffset? LeaseExpiresAtUtc { get; set; }
    public DateTimeOffset? RootStartedAtUtc { get; set; }
    public bool PublishBarrier { get; set; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset ExpiresAtUtc { get; init; }
    public DateTimeOffset NextAttemptAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public string? ResultCode { get; set; }
    public string? PublishedVersion { get; set; }
    public string? AcknowledgedBy { get; set; }
    public string? AcknowledgementJson { get; set; }
    public long Version { get; set; }
}
