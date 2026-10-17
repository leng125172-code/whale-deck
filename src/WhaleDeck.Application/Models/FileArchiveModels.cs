namespace WhaleDeck.Application.Models;

public sealed record FileHistoryCommand(string RootId, string Path, string After, int Take, string ExpectedVersion);
public sealed record FileVersionDto(Guid Id, string RootId, string Path, string SourceVersion, long SizeBytes, string Sha256,
    DateTimeOffset CapturedAtUtc, string CaptureKind, string CaptureState, string Availability, bool DifferentSourceDevice);
/// <summary>Present is metadata/stat availability, NOT a fresh full-checksum verification.</summary>
public sealed record FileHistoryPage(IReadOnlyCollection<FileVersionDto> Entries, string NextCursor, string Version, int Scanned, int InvalidRecords, bool Complete);
public sealed record FileArchiveCommand(string Kind, FileLocationDto Location, Guid? VersionId, string ExpectedVersion);
public sealed record FileArchiveIdentity(string MetadataHash, FileObjectIdentityDto DataIdentity);
public sealed record FileArchivePlan(FileArchiveCommand Command, FileParentIdentity? ParentIdentity,
    FileObjectIdentityDto? SourceIdentity, string SourceSha256, FileArchiveIdentity? ArchiveIdentity,
    FileObjectIdentityDto? RestoreMetadata, long SizeBytes, string Sha256, string Fingerprint);
public sealed record FileArchivePlanClaims(string Actor, string LoginBinding, FileArchivePlan Plan, DateTimeOffset ExpiresAtUtc);
public sealed record FileArchiveBinding(Guid Id, string Actor, string LoginBinding, FileArchivePlan Plan, DateTimeOffset ExpiresAtUtc);
public sealed record FileArchivePlanDto(string PlanHash, DateTimeOffset ExpiresAtUtc, FileArchiveCommand Command, long SizeBytes, string Warning);
public sealed record SubmitFileArchiveCommand(string PlanHash, bool Confirmed, string IdempotencyKey);
public sealed record AcknowledgeFileArchiveCommand(long Version, bool Confirmed);
public sealed record FileArchiveIntentDto(Guid Id, Guid JobId, FileArchiveCommand Command, long SizeBytes, string State,
    DateTimeOffset ExpiresAtUtc, string? ResultCode, long Version, string? PublishedVersion, bool SourceRetained);
public sealed record FileArchiveReceipt(string Kind, string State, string Fingerprint, string Version, long SizeBytes,
    string Sha256, bool SourceRetained, bool BindingCurrent, Guid VersionId, FileHddBackupDto? HddBackup);
