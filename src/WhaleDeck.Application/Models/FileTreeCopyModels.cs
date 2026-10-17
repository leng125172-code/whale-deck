namespace WhaleDeck.Application.Models;

public sealed record FileTreeCopyCommand(FileLocationDto Source, string ExpectedVersion, FileLocationDto Target);
public sealed record FileTreeCopyPlan(FileTreeCopyCommand Command, string ManifestSha256, int EntryCount, long TotalBytes,
    FileParentIdentity TargetParent, string SourcePolicyHash, string TargetPolicyHash, string Fingerprint);
public sealed record FileTreeCopyReceipt(Guid OperationId, string State, string Fingerprint, int EntryCount, long TotalBytes,
    string Version, bool PartialRetained);
public sealed record FileTreeCopyRequestBinding(Guid Id, string Actor, string LoginBinding, FileTreeCopyCommand Command, DateTimeOffset PlanningExpiresAtUtc);
public sealed record FileTreeCopyPlanBinding(Guid Id, string Actor, string LoginBinding, FileTreeCopyPlan Plan, DateTimeOffset ReadyExpiresAtUtc);
public sealed record FileTreeCopyConfirmation(Guid Id, string Actor, string LoginBinding, string Fingerprint, DateTimeOffset ExpiresAtUtc);
public sealed record ConfirmFileTreeCopyCommand(string PlanHash, bool Confirmed, long Version);
public sealed record FileTreeCopyConfirmationDto(string PlanHash, DateTimeOffset ExpiresAtUtc, string Warning);
public sealed record FileTreeCopyIntentDto(Guid Id, Guid PlanningJobId, Guid? ExecutionJobId, string State, FileTreeCopyCommand Command,
    int? EntryCount, long? TotalBytes, DateTimeOffset? ReadyExpiresAtUtc, string? ResultCode, long Version, string? PublishedVersion,
    bool PartialRetained, FileTreeCopyConfirmationDto? Confirmation);
public sealed class FileTreeCopyRejectedException(string code) : InvalidOperationException(code);
