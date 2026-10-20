using System.Text.Json.Serialization;

namespace WhaleDeck.Application.Models;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateFileTreeOverwriteRestoringCommand(Guid CaptureId, FileLocationDto Target, string TargetExpectedVersion);
// Historical owner/source facts come from the authenticated catalogue, never HTTP.
public sealed record FileTreeOverwriteRestoringCommand(FileLocationDto Source, string ExpectedVersion, FileLocationDto Target, string Format,
    Guid CaptureId, string CaptureOwner, string CaptureKind, string ManifestSha256, string TargetExpectedVersion);
public sealed record FileTreeOverwriteRestoringCommandDto(Guid CaptureId, FileLocationDto Source, FileLocationDto Target, string CaptureKind, string TargetExpectedVersion);
public sealed record FileTreeOverwriteRestoringPlan(FileTreeOverwriteRestoringCommand Command, string ArchiveSha256, long ArchiveBytes, string ContentSha256,
    int EntryCount, long TotalBytes, FileParentIdentity TargetParent, string SourcePolicyHash, string TargetPolicyHash, string Fingerprint,
    string TargetManifestSha256, string TargetTreeSha256, int TargetEntryCount, long TargetTotalBytes, FileParentIdentity TargetIdentity, string TargetContentSha256);
public sealed record FileTreeOverwriteRestoringReceipt(Guid OperationId, string State, string Fingerprint, string Format, int EntryCount, long TotalBytes,
    long ArchiveBytes, string ArchiveSha256, string Version, bool PartialRetained, string Kind, Guid CaptureId, Guid? OverwrittenCaptureId,
    bool BackupMayBeRetained, bool BackupVerified, bool OriginalsRetained, bool TargetMayHaveChanged, long BackupArchiveBytes, string BackupArchiveSha256);
public sealed record FileTreeOverwriteRestoringRequestBinding(Guid Id, string Actor, string LoginBinding, FileTreeOverwriteRestoringCommand Command, DateTimeOffset PlanningExpiresAtUtc);
public sealed record FileTreeOverwriteRestoringPlanBinding(Guid Id, string Actor, string LoginBinding, FileTreeOverwriteRestoringPlan Plan, DateTimeOffset ReadyExpiresAtUtc);
public sealed record FileTreeOverwriteRestoringConfirmation(Guid Id, string Actor, string LoginBinding, string Fingerprint, DateTimeOffset ExpiresAtUtc);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConfirmFileTreeOverwriteRestoringCommand(string PlanHash, bool Confirmed, long Version);
public sealed record FileTreeOverwriteRestoringConfirmationDto(string PlanHash, DateTimeOffset ExpiresAtUtc, string Warning);
public sealed record FileTreeOverwriteRestoringIntentDto(Guid Id, Guid PlanningJobId, Guid? ExecutionJobId, string State, FileTreeOverwriteRestoringCommandDto Command,
    int? EntryCount, long? TotalBytes, int? TargetEntryCount, long? TargetTotalBytes, DateTimeOffset? ReadyExpiresAtUtc, string? ResultCode, long Version,
    string? PublishedVersion, bool PartialRetained, FileTreeOverwriteRestoringConfirmationDto? Confirmation, long? ArchiveBytes, string? ArchiveSha256,
    Guid? OverwrittenCaptureId, bool BackupMayBeRetained, bool BackupVerified, bool OriginalsRetained, bool TargetMayHaveChanged,
    long? BackupArchiveBytes, string? BackupArchiveSha256);
public sealed record FileTreeOverwrittenCaptureBinding(Guid Id, Guid IntentId, string OwnerHash, string SourceHash, FileTreeRecyclingCommand Command,
    string ManifestSha256, string ContentSha256, int EntryCount, long TotalBytes, long ArchiveBytes, string ArchiveSha256, DateTimeOffset CreatedAtUtc);
public sealed class FileTreeOverwriteRestoringRejectedException(string code) : InvalidOperationException(code);
