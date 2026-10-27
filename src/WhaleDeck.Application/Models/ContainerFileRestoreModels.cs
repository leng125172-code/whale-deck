namespace WhaleDeck.Application.Models;

public sealed record ContainerFileRestoreCommand(ContainerFileLocationDto Location, string CaptureId, string ExpectedCaptureHash, string ExpectedVersion,
    bool RestoreAttributes = false);
public sealed record ContainerFileRestorePlanClaims(string Actor, string LoginBinding, ContainerFileUploadTargetDto Target,
    ContainerHistoryEntryDto Source, DateTimeOffset CreatedAtUtc, DateTimeOffset ExpiresAtUtc,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] bool RestoreAttributes = false)
{ public override string ToString() => nameof(ContainerFileRestorePlanClaims); }

public sealed record ContainerFileRestorePlanDto(string PlanHash, DateTimeOffset ExpiresAtUtc, bool Overwrites, string Warning,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] bool RestoreAttributes = false);
