namespace WhaleDeck.Domain.Entities;

/// <summary>Permanent public intent/receipt; no endpoint, credential, ticket or private input.</summary>
public sealed class NotificationPrivateOperation
{
    public Guid Id { get; init; }
    public Guid ChannelId { get; init; }
    public required string ActorSubject { get; init; }
    public required string RequestHash { get; init; }
    public required string InputFingerprint { get; init; }
    public required string RegistryRevision { get; init; }
    public required string Provider { get; init; }
    public required string Action { get; init; }
    public long ChannelVersion { get; init; }
    public string State { get; set; } = "Pending";
    public Guid? LeaseToken { get; set; }
    public DateTimeOffset? LeaseExpiresAtUtc { get; set; }
    public DateTimeOffset? RootStartedAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset InputExpiresAtUtc { get; init; }
    public DateTimeOffset NextAttemptAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public string? ResultCode { get; set; }
    public long Version { get; set; }
}
