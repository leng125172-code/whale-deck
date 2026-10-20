using System.Text.Json.Serialization;

namespace WhaleDeck.Application.Models;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateFileTreePurgingCommand(Guid CaptureId, string Scope);
// Owner, historical location/version and authenticated catalogue proof are resolved server-side.
public sealed record FileTreePurgingCommand(FileLocationDto Source, string ExpectedVersion, Guid CaptureId, string CaptureOwner,
    string CaptureKind, string ManifestSha256, string Scope, string CatalogueProofHash);
public sealed record FileTreePurgingCommandDto(Guid CaptureId, FileLocationDto Source, string CaptureKind, string Scope);
public sealed record FileTreePurgingPlan(FileTreePurgingCommand Command, string SourcePolicyHash, FileParentIdentity? SourceParent,
    string StorageHash, FileParentIdentity StoreIdentity, string CaptureMetadataHash, string OriginReceiptHash, string ManifestFileSha256,
    FileParentIdentity ArchiveIdentity, long ArchiveBytes, string ArchiveSha256, string ContentSha256, int EntryCount, long TotalBytes,
    FileParentIdentity? RetainedIdentity, FileParentIdentity? StagingIdentity, string? RetainedTreeSha256, string Fingerprint);
public sealed record FileTreePurgingReceipt(Guid OperationId, string Kind, string State, string Fingerprint, Guid CaptureId, string Scope,
    int EntryCount, long TotalBytes, long ArchiveBytes, bool DeletionStarted, bool ArchiveRemoved, bool OriginalsRemoved);
public sealed record FileTreePurgingRequestBinding(Guid Id, string Actor, string LoginBinding, FileTreePurgingCommand Command,
    DateTimeOffset CreatedAtUtc, DateTimeOffset PlanningExpiresAtUtc);
public sealed record FileTreePurgingPlanBinding(Guid Id, string Actor, string LoginBinding, FileTreePurgingPlan Plan, DateTimeOffset ReadyExpiresAtUtc);
public sealed record FileTreePurgingReceiptBinding(Guid Id, string Actor, string LoginBinding, FileTreePurgingReceipt Receipt, DateTimeOffset ObservedAtUtc);
public sealed record FileTreePurgingConfirmation(Guid Id, string Actor, string LoginBinding, Guid CaptureId, string Scope, string Fingerprint, DateTimeOffset ExpiresAtUtc);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConfirmFileTreePurgingCommand(string PlanHash, bool Confirmed, long Version);
public sealed record FileTreePurgingConfirmationDto(string PlanHash, DateTimeOffset ExpiresAtUtc, string Warning);
public sealed record FileTreePurgingIntentDto(Guid Id, Guid PlanningJobId, Guid? ExecutionJobId, string State, FileTreePurgingCommandDto Command,
    int? EntryCount, long? TotalBytes, long? ArchiveBytes, DateTimeOffset? ReadyExpiresAtUtc, string? ResultCode, long Version,
    bool? DeletionStarted, bool? ArchiveRemoved, bool? OriginalsRemoved, FileTreePurgingConfirmationDto? Confirmation);
public sealed class FileTreePurgingRejectedException(string code) : InvalidOperationException(code);
