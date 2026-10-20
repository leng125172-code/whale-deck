using System.Text.Json.Serialization;

namespace WhaleDeck.Application.Models;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateFileTreeRestoringCommand(Guid CaptureId, FileLocationDto Target);
// Internal only: never bind the original resource owner from HTTP or expose it in DTOs.
public sealed record FileTreeRestoringCommand(FileLocationDto Source, string ExpectedVersion, FileLocationDto Target, string Format,
    Guid CaptureId, string CaptureOwner, string CaptureKind, string ManifestSha256);
public sealed record FileTreeRestoringCommandDto(Guid CaptureId, FileLocationDto Source, FileLocationDto Target, string CaptureKind);
public sealed record FileTreeRestoringPlan(FileTreeRestoringCommand Command, string ArchiveSha256, long ArchiveBytes, string ContentSha256, int EntryCount, long TotalBytes,
    FileParentIdentity TargetParent, string SourcePolicyHash, string TargetPolicyHash, string Fingerprint);
public sealed record FileTreeRestoringReceipt(Guid OperationId, string State, string Fingerprint, string Format, int EntryCount, long TotalBytes,
    long ArchiveBytes, string ArchiveSha256, string Version, bool PartialRetained, string Kind, Guid CaptureId);
public sealed record FileTreeRestoringRequestBinding(Guid Id, string Actor, string LoginBinding, FileTreeRestoringCommand Command, DateTimeOffset PlanningExpiresAtUtc);
public sealed record FileTreeRestoringPlanBinding(Guid Id, string Actor, string LoginBinding, FileTreeRestoringPlan Plan, DateTimeOffset ReadyExpiresAtUtc);
public sealed record FileTreeRestoringConfirmation(Guid Id, string Actor, string LoginBinding, string Fingerprint, DateTimeOffset ExpiresAtUtc);
public sealed record ConfirmFileTreeRestoringCommand(string PlanHash, bool Confirmed, long Version);
public sealed record FileTreeRestoringConfirmationDto(string PlanHash, DateTimeOffset ExpiresAtUtc, string Warning);
public sealed record FileTreeRestoringIntentDto(Guid Id, Guid PlanningJobId, Guid? ExecutionJobId, string State, FileTreeRestoringCommandDto Command,
    int? EntryCount, long? TotalBytes, DateTimeOffset? ReadyExpiresAtUtc, string? ResultCode, long Version, string? PublishedVersion,
    bool PartialRetained, FileTreeRestoringConfirmationDto? Confirmation, long? ArchiveBytes, string? ArchiveSha256);
public sealed class FileTreeRestoringRejectedException(string code) : InvalidOperationException(code);
