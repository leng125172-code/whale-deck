namespace WhaleDeck.Application.Models;

public sealed record FileTreePackingCommand(FileLocationDto Source, string ExpectedVersion, FileLocationDto Target, string Format);
public sealed record FileTreePackingPlan(FileTreePackingCommand Command, string ManifestSha256, int EntryCount, long TotalBytes,
    FileParentIdentity TargetParent, string SourcePolicyHash, string TargetPolicyHash, string Fingerprint);
public sealed record FileTreePackingReceipt(Guid OperationId, string State, string Fingerprint, string Format, int EntryCount, long TotalBytes,
    long ArchiveBytes, string ArchiveSha256, string Version, bool PartialRetained);
public sealed record FileTreePackingRequestBinding(Guid Id, string Actor, string LoginBinding, FileTreePackingCommand Command, DateTimeOffset PlanningExpiresAtUtc);
public sealed record FileTreePackingPlanBinding(Guid Id, string Actor, string LoginBinding, FileTreePackingPlan Plan, DateTimeOffset ReadyExpiresAtUtc);
public sealed record FileTreePackingConfirmation(Guid Id, string Actor, string LoginBinding, string Fingerprint, DateTimeOffset ExpiresAtUtc);
public sealed record ConfirmFileTreePackingCommand(string PlanHash, bool Confirmed, long Version);
public sealed record FileTreePackingConfirmationDto(string PlanHash, DateTimeOffset ExpiresAtUtc, string Warning);
public sealed record FileTreePackingIntentDto(Guid Id, Guid PlanningJobId, Guid? ExecutionJobId, string State, FileTreePackingCommand Command,
    int? EntryCount, long? TotalBytes, DateTimeOffset? ReadyExpiresAtUtc, string? ResultCode, long Version, string? PublishedVersion,
    bool PartialRetained, FileTreePackingConfirmationDto? Confirmation, long? ArchiveBytes, string? ArchiveSha256);
public sealed class FileTreePackingRejectedException(string code) : InvalidOperationException(code);
