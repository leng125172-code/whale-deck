namespace WhaleDeck.Application.Models;

public sealed record FileTreeUnpackingCommand(FileLocationDto Source, string ExpectedVersion, FileLocationDto Target, string Format);
public sealed record FileTreeUnpackingPlan(FileTreeUnpackingCommand Command, string ArchiveSha256, long ArchiveBytes, string ContentSha256, int EntryCount, long TotalBytes,
    FileParentIdentity TargetParent, string SourcePolicyHash, string TargetPolicyHash, string Fingerprint);
public sealed record FileTreeUnpackingReceipt(Guid OperationId, string State, string Fingerprint, string Format, int EntryCount, long TotalBytes,
    long ArchiveBytes, string ArchiveSha256, string Version, bool PartialRetained);
public sealed record FileTreeUnpackingRequestBinding(Guid Id, string Actor, string LoginBinding, FileTreeUnpackingCommand Command, DateTimeOffset PlanningExpiresAtUtc);
public sealed record FileTreeUnpackingPlanBinding(Guid Id, string Actor, string LoginBinding, FileTreeUnpackingPlan Plan, DateTimeOffset ReadyExpiresAtUtc);
public sealed record FileTreeUnpackingConfirmation(Guid Id, string Actor, string LoginBinding, string Fingerprint, DateTimeOffset ExpiresAtUtc);
public sealed record ConfirmFileTreeUnpackingCommand(string PlanHash, bool Confirmed, long Version);
public sealed record FileTreeUnpackingConfirmationDto(string PlanHash, DateTimeOffset ExpiresAtUtc, string Warning);
public sealed record FileTreeUnpackingIntentDto(Guid Id, Guid PlanningJobId, Guid? ExecutionJobId, string State, FileTreeUnpackingCommand Command,
    int? EntryCount, long? TotalBytes, DateTimeOffset? ReadyExpiresAtUtc, string? ResultCode, long Version, string? PublishedVersion,
    bool PartialRetained, FileTreeUnpackingConfirmationDto? Confirmation, long? ArchiveBytes, string? ArchiveSha256);
public sealed class FileTreeUnpackingRejectedException(string code) : InvalidOperationException(code);
