namespace WhaleDeck.Application.Models;

public sealed record FileTreeRecyclingCommand(FileLocationDto Source, string ExpectedVersion);
public sealed record FileTreeRecyclingPlan(FileTreeRecyclingCommand Command, FileParentIdentity SourceParent, string SourcePolicyHash,
    string ManifestSha256, int EntryCount, long TotalBytes, string Fingerprint);
public sealed record FileTreeRecyclingReceipt(Guid OperationId, string State, string Fingerprint, Guid CaptureId, string ManifestSha256,
    int EntryCount, long TotalBytes, bool CaptureVerified, bool CaptureMayBeRetained, long ArchiveBytes, string ArchiveSha256,
    bool SourceMayHaveChanged, bool SourceRemoved, bool OriginalsRetained);
public sealed record FileTreeRecyclingRequestBinding(Guid Id, string Actor, string LoginBinding, FileTreeRecyclingCommand Command, DateTimeOffset PlanningExpiresAtUtc);
public sealed record FileTreeRecyclingPlanBinding(Guid Id, string Actor, string LoginBinding, FileTreeRecyclingPlan Plan, DateTimeOffset ReadyExpiresAtUtc);
public sealed record FileTreeRecyclingConfirmation(Guid Id, string Actor, string LoginBinding, string Fingerprint, DateTimeOffset ExpiresAtUtc);
public sealed record ConfirmFileTreeRecyclingCommand(string PlanHash, bool Confirmed, long Version);
public sealed record FileTreeRecyclingConfirmationDto(string PlanHash, DateTimeOffset ExpiresAtUtc, string Warning);
public sealed record FileTreeRecyclingIntentDto(Guid Id, Guid PlanningJobId, Guid? ExecutionJobId, string State, FileTreeRecyclingCommand Command,
    int? EntryCount, long? TotalBytes, DateTimeOffset? ReadyExpiresAtUtc, string? ResultCode, long Version, Guid? CaptureId, long? ArchiveBytes, string? ArchiveSha256,
    bool CaptureVerified, bool? CaptureMayBeRetained, bool? SourceMayHaveChanged, bool? SourceRemoved, bool? OriginalsRetained, FileTreeRecyclingConfirmationDto? Confirmation);
public sealed record FileTreeCaptureBinding(Guid Id, Guid IntentId, string OwnerHash, string SourceHash, FileTreeRecyclingCommand Command,
    string ManifestSha256, int EntryCount, long TotalBytes, long ArchiveBytes, string ArchiveSha256, DateTimeOffset CreatedAtUtc);
public sealed record FileTreeCaptureDto(Guid Id, Guid IntentId, FileTreeRecyclingCommand Source, string ManifestSha256, int EntryCount,
    long TotalBytes, long ArchiveBytes, string ArchiveSha256, DateTimeOffset CreatedAtUtc, string Kind = "Trash", string? ContentSha256 = null,
    string ArchiveState = "Retained", string OriginalsState = "Retained", bool CleanupActive = false);
public sealed record FileTreeCapturePage(IReadOnlyList<FileTreeCaptureDto> Items, Guid? Next);
public sealed class FileTreeRecyclingRejectedException(string code) : InvalidOperationException(code);
