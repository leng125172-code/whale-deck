namespace WhaleDeck.Application.Models;

/// <summary>Move reuses only the immutable location command; its plan and confirmation are independent of copy.</summary>
public sealed record FileTreeMovePlan(FileTreeCopyPlan CopyPlan, FileParentIdentity SourceParent, string Fingerprint);
public sealed record FileTreeMoveReceipt(Guid OperationId, string State, string Fingerprint, int EntryCount, long TotalBytes,
    string Version, bool DestinationPublished, bool SourceRemoved, bool SourceRetained, bool PartialRetained);
public sealed record FileTreeMoveRequestBinding(Guid Id, string Actor, string LoginBinding, FileTreeCopyCommand Command, DateTimeOffset PlanningExpiresAtUtc);
public sealed record FileTreeMovePlanBinding(Guid Id, string Actor, string LoginBinding, FileTreeMovePlan Plan, DateTimeOffset ReadyExpiresAtUtc);
public sealed record FileTreeMoveConfirmation(Guid Id, string Actor, string LoginBinding, string Fingerprint, DateTimeOffset ExpiresAtUtc);
