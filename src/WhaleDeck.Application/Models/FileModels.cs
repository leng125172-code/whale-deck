namespace WhaleDeck.Application.Models;

public sealed record FileRootDto(string Id, string Name, IReadOnlyCollection<string> Actions);
public sealed record FileEntryDto(string Name, string Kind, long SizeBytes, long ModifiedAtUnixMs, string Version, string Mode, bool IsProtected);
public sealed record FilePageDto(IReadOnlyCollection<FileEntryDto> Entries, string NextCursor, string Version);
public sealed record FileLocationDto(string RootId, string Path);
public sealed record FileUploadPlanCommand(string RootId, string Path, long SizeBytes, string Sha256, string ExpectedVersion);
public sealed record FileUploadPlanClaims(string Actor, string LoginBinding, FileUploadPlanCommand File, DateTimeOffset ExpiresAtUtc);
public sealed record FileUploadBinding(Guid Id, string Actor, string LoginBinding, FileUploadPlanCommand File, DateTimeOffset ExpiresAtUtc);
public sealed record FileUploadPlanDto(string PlanHash, DateTimeOffset ExpiresAtUtc, bool Overwrites, string Warning);
public sealed record SubmitFileUploadCommand(string PlanHash, bool Confirmed, string IdempotencyKey);
public sealed record FileCancelDto(Guid JobId, string State);
public sealed record FileUploadIntentDto(Guid Id, Guid JobId, string RootId, string Path, long SizeBytes, string Sha256,
    string State, DateTimeOffset ExpiresAtUtc, string? ResultCode, long Version, string? PublishedVersion, bool PreviousRetained);
public sealed record FileHddBackupDto(string OperationId, string StorageId, string SourceVersion, long SizeBytes, string Sha256, long CreatedAtUnixMs, bool DifferentSourceDevice);
public sealed record FileUploadReceiptDto(string State, long SizeBytes, string Sha256, string Version, bool PreviousRetained, bool BindingCurrent, bool IsInProgress, FileHddBackupDto? HddBackup = null, string? ResultCode = null);
public sealed record FileDownloadPart(string Kind, FileEntryDto? Metadata, ReadOnlyMemory<byte> Data, long Offset, string Sha256, string Version, long SizeBytes);
public enum FileReadPurpose { Download, TextPreview }
public sealed record FileTextDto(string RootId, string Path, string Version, long SizeBytes, string Sha256, string Text, bool Utf8Bom, string LineEnding);
public enum FileUploadStartResult { Started, LeaseLost, Canceled, Expired }
public sealed class FileTransferRejectedException : InvalidOperationException { }
