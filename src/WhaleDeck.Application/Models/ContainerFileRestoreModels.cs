namespace WhaleDeck.Application.Models;

public sealed record ContainerFileRestoreCommand(ContainerFileLocationDto Location, string CaptureId, string ExpectedCaptureHash, string ExpectedVersion);
public sealed record ContainerFileRestorePlanClaims(string Actor, string LoginBinding, ContainerFileUploadTargetDto Target,
    ContainerHistoryEntryDto Source, DateTimeOffset CreatedAtUtc, DateTimeOffset ExpiresAtUtc)
{ public override string ToString() => nameof(ContainerFileRestorePlanClaims); }
