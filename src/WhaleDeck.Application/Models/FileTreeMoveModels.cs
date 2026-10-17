using System.Text.Json.Serialization;

namespace WhaleDeck.Application.Models;

/// <summary>Move reuses only the immutable location command; its plan and confirmation are independent of copy.</summary>
public sealed record FileTreeMovePlan(FileTreeCopyPlan CopyPlan, FileParentIdentity SourceParent, string Fingerprint)
{
    [JsonIgnore] public FileTreeCopyCommand Command => CopyPlan.Command;
    [JsonIgnore] public int EntryCount => CopyPlan.EntryCount;
    [JsonIgnore] public long TotalBytes => CopyPlan.TotalBytes;
}
public sealed record FileTreeMoveReceipt(Guid OperationId, string State, string Fingerprint, int EntryCount, long TotalBytes,
    string Version, bool DestinationPublished, bool SourceRemoved, bool SourceRetained, bool PartialRetained);
public sealed record FileTreeMoveRequestBinding(Guid Id, string Actor, string LoginBinding, FileTreeCopyCommand Command, DateTimeOffset PlanningExpiresAtUtc);
public sealed record FileTreeMovePlanBinding(Guid Id, string Actor, string LoginBinding, FileTreeMovePlan Plan, DateTimeOffset ReadyExpiresAtUtc);
public sealed record FileTreeMoveConfirmation(Guid Id, string Actor, string LoginBinding, string Fingerprint, DateTimeOffset ExpiresAtUtc);
public sealed record ConfirmFileTreeMoveCommand(string PlanHash, bool Confirmed, long Version);
public sealed record FileTreeMoveConfirmationDto(string PlanHash, DateTimeOffset ExpiresAtUtc, string Warning);
public sealed record FileTreeMoveIntentDto(Guid Id, Guid PlanningJobId, Guid? ExecutionJobId, string State, FileTreeCopyCommand Command,
    int? EntryCount, long? TotalBytes, DateTimeOffset? ReadyExpiresAtUtc, string? ResultCode, long Version, string? PublishedVersion,
    bool? DestinationPublished, bool? SourceRemoved, bool? SourceRetained, bool PartialRetained, FileTreeMoveConfirmationDto? Confirmation);
public sealed class FileTreeMoveRejectedException(string code) : InvalidOperationException(code);
