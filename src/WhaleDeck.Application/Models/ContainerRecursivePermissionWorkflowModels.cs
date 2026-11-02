namespace WhaleDeck.Application.Models;

public sealed record ContainerRecursivePermissionRequestBinding(Guid Id, Guid IdempotencyKey, string Actor, string LoginBinding, string TraceId,
    string Owner, ContainerRecursivePermissionCommandDto Command, DateTimeOffset CreatedAtUtc, DateTimeOffset PlanningExpiresAtUtc)
{ public override string ToString() => nameof(ContainerRecursivePermissionRequestBinding); }
public sealed record ContainerRecursivePermissionPlanBinding(ContainerRecursivePermissionRequestBinding Request, ContainerRecursivePermissionPlanDto Plan, DateTimeOffset ReadyExpiresAtUtc)
{ public override string ToString() => nameof(ContainerRecursivePermissionPlanBinding); }
public sealed record ContainerRecursivePermissionExecutionBinding(Guid Id, string Actor, string LoginBinding, string PlanHash, Guid ExecutionJobId,
    DateTimeOffset ConfirmedAtUtc, DateTimeOffset ExecutionExpiresAtUtc)
{ public override string ToString() => nameof(ContainerRecursivePermissionExecutionBinding); }
public sealed record ContainerRecursivePermissionConfirmation(Guid Id, string Actor, string LoginBinding, string PlanHash, DateTimeOffset ExpiresAtUtc)
{ public override string ToString() => nameof(ContainerRecursivePermissionConfirmation); }
public sealed record ConfirmContainerRecursivePermissionCommand(string PlanHash, bool Confirmed, long Version);
public sealed record ContainerRecursivePermissionConfirmationDto(string PlanHash, DateTimeOffset ExpiresAtUtc, string Warning);
public sealed record ContainerRecursivePermissionIntentDto(Guid Id, Guid PlanningJobId, Guid? ExecutionJobId, ContainerRecursivePermissionCommandDto Command,
    string State, uint? RootUid, uint? RootGid, uint? ExecutorUid, uint? ExecutorGid, uint? EntryCount, uint? FileCount, uint? DirectoryCount, uint? ChangeCount,
    string? ManifestDigest, DateTimeOffset? ReadyExpiresAtUtc, string? ResultCode, long Version,
    bool HistoryMayExist, bool HistoryVerified, string? HistoryDigest, ContainerRecursivePermissionReceiptDto? Receipt, ContainerRecursivePermissionConfirmationDto? Confirmation);
public sealed record AcknowledgeContainerRecursivePermissionCommand(long Version, bool Confirmed);
public sealed class ContainerRecursivePermissionPreflightRejectedException : Exception
{
    public string Code { get; }
    public ContainerRecursivePermissionPreflightRejectedException(string code) : base("CONTAINER_RECURSIVE_PERMISSION_PREFLIGHT_REJECTED")
    {
        if (code is not ("CONTAINER_RECURSIVE_PERMISSION_VERSION_CONFLICT" or "CONTAINER_RECURSIVE_PERMISSION_PROTECTED" or
            "CONTAINER_RECURSIVE_PERMISSION_TARGET_CHANGED" or "CONTAINER_RECURSIVE_PERMISSION_TREE_LIMIT" or "CONTAINER_RECURSIVE_PERMISSION_MANIFEST_LIMIT" or
            "CONTAINER_RECURSIVE_PERMISSION_EXTENDED_METADATA_REQUIRED" or "CONTAINER_RECURSIVE_PERMISSION_SOURCE_UNSUPPORTED" or "CONTAINER_RECURSIVE_PERMISSION_ATTRIBUTES_LIMIT"))
            throw new ArgumentException("CONTAINER_RECURSIVE_PERMISSION_PREFLIGHT_CODE_INVALID");
        Code = code;
    }
}
