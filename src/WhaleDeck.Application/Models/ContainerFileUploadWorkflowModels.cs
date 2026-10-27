namespace WhaleDeck.Application.Models;

public sealed record ContainerFileUploadCommand(ContainerFileLocationDto Location, long SizeBytes, string Sha256, string ExpectedVersion);
public sealed record ContainerFileUploadPlanClaims(string Actor, string LoginBinding, ContainerFileUploadTargetDto Target,
    long SizeBytes, string Sha256, DateTimeOffset CreatedAtUtc, DateTimeOffset ExpiresAtUtc)
{ public override string ToString() => nameof(ContainerFileUploadPlanClaims); }
public sealed record ContainerFileUploadBinding(Guid Id, Guid IdempotencyKey, string Actor, string LoginBinding, string TraceId,
    ContainerFileUploadTargetDto Target, long SizeBytes, string Sha256, string PlanHash, DateTimeOffset CreatedAtUtc, DateTimeOffset ExpiresAtUtc)
{ public override string ToString() => nameof(ContainerFileUploadBinding); }
public sealed record ContainerFileUploadIntentDto(Guid Id, Guid JobId, ContainerFileLocationDto Location, long SizeBytes, string Sha256,
    string State, DateTimeOffset ExpiresAtUtc, string? ResultCode, long Version, string? PublishedVersion, bool PreviousRetained);
public enum ContainerFileUploadStartResult { Started, Busy, LeaseLost, Canceled, Expired }
