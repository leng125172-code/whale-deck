namespace WhaleDeck.Application.Models;

public sealed record ContainerFileUploadCommand(ContainerFileLocationDto Location, long SizeBytes, string Sha256, string ExpectedVersion);
public sealed record ContainerFileUploadPlanClaims(string Actor, string LoginBinding, ContainerFileUploadTargetDto Target,
    long SizeBytes, string Sha256, DateTimeOffset CreatedAtUtc, DateTimeOffset ExpiresAtUtc)
{ public override string ToString() => nameof(ContainerFileUploadPlanClaims); }
public sealed record ContainerFileUploadBinding(Guid Id, Guid IdempotencyKey, string Actor, string LoginBinding, string TraceId,
    ContainerFileUploadTargetDto Target, long SizeBytes, string Sha256, string PlanHash, DateTimeOffset CreatedAtUtc, DateTimeOffset ExpiresAtUtc,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? HistorySourceJson = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] bool RestoreAttributes = false)
{ public override string ToString() => nameof(ContainerFileUploadBinding); }
public sealed record ContainerFileUploadIntentDto(Guid Id, Guid JobId, ContainerFileLocationDto Location, long SizeBytes, string Sha256,
    string State, DateTimeOffset ExpiresAtUtc, string? ResultCode, long Version, string? PublishedVersion, bool PreviousRetained,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] bool RestoreAttributes = false);
public enum ContainerFileUploadStartResult { Started, Busy, LeaseLost, Canceled, Expired }

/// <summary>A fixed, definitive instance/target failure; never arbitrary transport text.</summary>
public sealed class ContainerFileUploadTargetUnavailableException : InvalidOperationException
{
    public ContainerFileUploadTargetUnavailableException() : base("CONTAINER_UPLOAD_TARGET_UNAVAILABLE") { }
}
