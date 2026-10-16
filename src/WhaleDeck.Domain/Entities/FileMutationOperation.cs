namespace WhaleDeck.Domain.Entities;

/// <summary>Permanent, human/login-bound mutation intent. No file content or capability in SQL.</summary>
public sealed class FileMutationOperation
{
    public Guid Id { get; init; }
    public required string ActorSubject { get; init; }
    public required string LoginBinding { get; init; }
    public required string OwnerHash { get; init; }
    public required string RequestHash { get; init; }
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

/// <summary>Shared upload/mutation logical lock. Unknown outcomes retain ownership.</summary>
public sealed class FilePathLock
{
    public required string PathHash { get; init; }
    public Guid OperationId { get; init; }
}
