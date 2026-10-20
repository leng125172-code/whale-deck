namespace WhaleDeck.Application.Models;

/// <summary>Recovery is a new administrator action, never a replacement original login.</summary>
public static class FileRecoveryKinds
{
    public const string Archive = "archive";
    public const string Mutation = "mutation";
    public const string TreeCopy = "tree-copy";
    public const string TreeMove = "tree-move";
    public const string TreePacking = "tree-pack";
    public const string TreeUnpacking = "tree-unpack";
    public const string TreeRecycling = "tree-trash";
    public const string TreeRestoring = "tree-restore";
    public const string Upload = "upload";
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[] { Archive, Mutation, TreeCopy, TreeMove, Upload, TreePacking, TreeUnpacking, TreeRecycling, TreeRestoring });
}

/// <summary>Safe inventory only: no original login, owner key, signed plan or private receipt.</summary>
public sealed record FileRecoveryCandidateDto(string Kind, Guid IntentId, Guid? ExecutionJobId,
    long Version, DateTimeOffset CreatedAtUtc, DateTimeOffset? LeaseExpiresAtUtc, bool RootStartRecorded,
    string Integrity, FileLocationDto? Source, FileLocationDto? Target, bool? HasRetainedObjects);
public sealed record FileRecoveryPageDto(IReadOnlyList<FileRecoveryCandidateDto> Entries, string? NextCursor);
public sealed record FileRecoveryCursor(DateTimeOffset CreatedAtUtc, string Kind, Guid IntentId);

// Server-side claims. A target digest is NOT the original owner credential and
// never authorizes Apply. Current authority must be rechecked by the service.
public sealed record FileRecoveryTargetBinding(string Kind, Guid IntentId, Guid ExecutionJobId,
    long OriginalVersion, string OriginalBindingHash);
public sealed record FileRecoveryPlanClaims(Guid Id, string Actor, string LoginBinding,
    FileRecoveryTargetBinding Target, string ObservationHash, string RootState,
    string Disposition, string ReasonCode, DateTimeOffset IssuedAtUtc, DateTimeOffset ExpiresAtUtc);
