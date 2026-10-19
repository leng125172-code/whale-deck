namespace WhaleDeck.Domain.Entities;

/// <summary>One login-bound directory recycle with distinct planning and execution jobs.</summary>
public sealed class FileTreeRecyclingOperation
{
    public Guid Id { get; init; }
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
    public Guid? CaptureId { get; set; }
    public string? CaptureHash { get; set; }
    public DateTimeOffset? CaptureCreatedAtUtc { get; set; }
    public long? ArchiveBytes { get; set; }
    public string? ArchiveSha256 { get; set; }
    public bool CaptureVerified { get; set; }
    public bool CaptureMayBeRetained { get; set; }
    public bool SourceMayHaveChanged { get; set; }
    public bool SourceRemoved { get; set; }
    public bool OriginalsRetained { get; set; }
    public long Version { get; set; }
}
