namespace WhaleDeck.Application.Models;

public sealed record ContainerPermissionRequestBinding(Guid Id, Guid IdempotencyKey, string Actor, string LoginBinding, string TraceId,
    string Owner, ContainerPermissionCommandDto Command, DateTimeOffset CreatedAtUtc, DateTimeOffset PlanningExpiresAtUtc)
{ public override string ToString() => nameof(ContainerPermissionRequestBinding); }
public sealed record ContainerPermissionPlanBinding(ContainerPermissionRequestBinding Request, ContainerPermissionPlanDto Plan, DateTimeOffset ReadyExpiresAtUtc)
{ public override string ToString() => nameof(ContainerPermissionPlanBinding); }
public sealed record ContainerPermissionExecutionBinding(Guid Id, string Actor, string LoginBinding, string PlanHash, Guid ExecutionJobId,
    DateTimeOffset ConfirmedAtUtc, DateTimeOffset ExecutionExpiresAtUtc)
{ public override string ToString() => nameof(ContainerPermissionExecutionBinding); }
public sealed record ContainerPermissionConfirmation(Guid Id, string Actor, string LoginBinding, string PlanHash, DateTimeOffset ExpiresAtUtc)
{ public override string ToString() => nameof(ContainerPermissionConfirmation); }
public sealed record ConfirmContainerPermissionCommand(string PlanHash, bool Confirmed, long Version);
public sealed record ContainerPermissionConfirmationDto(string PlanHash, DateTimeOffset ExpiresAtUtc, string Warning);
public sealed record ContainerPermissionIntentDto(Guid Id, Guid PlanningJobId, Guid? ExecutionJobId, ContainerPermissionCommandDto Command,
    string State, string? Kind, string? OriginalMode, bool? NoChange, DateTimeOffset? ReadyExpiresAtUtc, string? ResultCode,
    long Version, string? PublishedVersion, bool? ModeConfirmed, bool HistoryMayExist, bool HistoryVerified,
    string? HistoryDigest, ContainerPermissionConfirmationDto? Confirmation);
public sealed record AcknowledgeContainerPermissionCommand(long Version, bool Confirmed);

/// <summary>Only explicit non-transient preflight refusals. Never used to infer execution outcome.</summary>
public sealed class ContainerPermissionPreflightRejectedException : Exception
{
    public string Code { get; }
    public ContainerPermissionPreflightRejectedException(string code) : base("Container permission preflight was refused.")
    {
        if (code is not ("CONTAINER_PERMISSION_VERSION_CONFLICT" or "CONTAINER_PERMISSION_SOURCE_UNSUPPORTED" or
            "CONTAINER_PERMISSION_EXTENDED_METADATA_REQUIRED" or "CONTAINER_PERMISSION_PROTECTED" or
            "CONTAINER_PERMISSION_TARGET_CHANGED" or "CONTAINER_PERMISSION_ATTRIBUTES_LIMIT")) throw new ArgumentException("CONTAINER_PERMISSION_PREFLIGHT_CODE_INVALID");
        Code = code;
    }
}
