namespace WhaleDeck.Domain.Entities;

/// <summary>One login-bound directory intent, with separate planning and confirmed execution jobs.</summary>
public sealed class FileTreePackingOperation
{
    public Guid Id { get; init; }
    public required string ActorSubject { get; init; }
    public required string LoginBinding { get; init; }
    public required string IdempotencyKey { get; init; }
    public required string OwnerHash { get; init; }
    public required string RequestHash { get; init; }
    public required string SourceHash { get; init; }
    public required string TargetHash { get; init; }
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
    public string? PublishedVersion { get; set; }
    public long? ArchiveBytes { get; set; }
    public string? ArchiveSha256 { get; set; }
    public bool PartialRetained { get; set; }
    public long Version { get; set; }
}
