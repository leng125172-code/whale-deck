namespace WhaleDeck.Domain.Entities;

/// <summary>Instance-bound container upload; bytes and private root receipts never enter SQL.</summary>
public sealed class ContainerFileUploadOperation
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
    public required string TargetProof { get; init; }
    public required string ExpectedParentVersion { get; init; }
    public required string ExpectedVersion { get; init; }
    public long PreviousSizeBytes { get; init; }
    public required string PlanHash { get; init; }
    public required string RequestHash { get; init; }
    public required string Sha256 { get; init; }
    public long SizeBytes { get; init; }
    public string State { get; set; } = "AwaitingUpload";
    public Guid? ReceiveToken { get; set; }
    public Guid? LeaseToken { get; set; }
    public DateTimeOffset? LeaseExpiresAtUtc { get; set; }
    public DateTimeOffset? RootStartedAtUtc { get; set; }
    // Shared with every host write reservation under advisory gate 41307341.
    // This remains held through Unknown; elapsed leases are not publication proof.
    public bool PublishBarrier { get; set; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset ExpiresAtUtc { get; init; }
    public DateTimeOffset NextAttemptAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public string? ResultCode { get; set; }
    public string? PublishedVersion { get; set; }
    public bool PreviousRetained { get; set; }
    public long Version { get; set; }
}
