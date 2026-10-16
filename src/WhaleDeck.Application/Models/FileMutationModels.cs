namespace WhaleDeck.Application.Models;

public sealed record FileMutationCommand(string Kind, FileLocationDto? Source, FileLocationDto? Target, string ExpectedVersion, string Mode);
public sealed record FileParentIdentity(ulong Device, ulong Inode);
public sealed record FileObjectIdentityDto(ulong Device, ulong Inode, uint Mode, uint OwnerUid, uint OwnerGid, ulong LinkCount, long SizeBytes, long ModifiedAtUnixNs);
public sealed record FileMutationPlan(FileMutationCommand Command, FileObjectIdentityDto? SourceIdentity, FileParentIdentity? SourceParent,
    FileParentIdentity? TargetParent, long SizeBytes, string Sha256, string Fingerprint);
public sealed record FileMutationPlanClaims(string Actor, string LoginBinding, FileMutationPlan Plan, DateTimeOffset ExpiresAtUtc);
public sealed record FileMutationBinding(Guid Id, string Actor, string LoginBinding, FileMutationPlan Plan, DateTimeOffset ExpiresAtUtc);
public sealed record FileMutationPlanDto(string PlanHash, DateTimeOffset ExpiresAtUtc, FileMutationCommand Command, long SizeBytes, string Warning);
public sealed record SubmitFileMutationCommand(string PlanHash, bool Confirmed, string IdempotencyKey);
public sealed record AcknowledgeFileMutationCommand(long Version, bool Confirmed);
public sealed record FileMutationIntentDto(Guid Id, Guid JobId, FileMutationCommand Command, long SizeBytes, string State,
    DateTimeOffset ExpiresAtUtc, string? ResultCode, long Version, string? PublishedVersion, bool SourceRetained);
public sealed record FileMutationReceipt(string Kind, string State, string Fingerprint, string Version, long SizeBytes, string Sha256, bool SourceRetained, bool BindingCurrent);
