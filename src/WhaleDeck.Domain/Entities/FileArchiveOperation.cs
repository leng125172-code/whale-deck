namespace WhaleDeck.Domain.Entities;

/// <summary>Permanent login-bound trash/restore/purge intent, separate from old signed mutation payloads.</summary>
public sealed class FileArchiveOperation
{
    public Guid Id { get; init; }
    public required string ActorSubject { get; init; }
    public required string LoginBinding { get; init; }
    public required string OwnerHash { get; init; }
    public required string RequestHash { get; init; }
    public required string Kind { get; init; }
    public required string RootId { get; init; }
    public required string RelativePath { get; init; }
    public required string TargetHash { get; init; }
    public Guid? ArchiveVersionId { get; init; }
    public required string ExpectedVersion { get; init; }
    public required string Sha256 { get; init; }
    public long SizeBytes { get; init; }
    public required string PlanJson { get; init; }
    public string State { get; set; } = "Pending";
    public Guid? LeaseToken { get; set; }
    public DateTimeOffset? LeaseExpiresAtUtc { get; set; }
    public DateTimeOffset? RootStartedAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAtUtc { get; init; }
    public DateTimeOffset NextAttemptAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public string? ResultCode { get; set; }
    public string? PublishedVersion { get; set; }
    public bool SourceRetained { get; set; }
    public long Version { get; set; }
}
