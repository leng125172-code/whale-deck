using System.Text.Json.Serialization;

namespace WhaleDeck.Application.Models;

/// <summary>Private original material is server-only and never a replacement identity.</summary>
public sealed class FileRecoverySnapshot(string kind, string originalJson, FileRecoveryTargetBinding binding)
{
    public string Kind { get; } = kind;
    [JsonIgnore] public string OriginalJson { get; } = originalJson;
    public FileRecoveryTargetBinding Binding { get; } = binding;
    public override string ToString() => $"FileRecoverySnapshot({Kind},{Binding.IntentId:D})";
}
/// <summary>Only a validated original receipt can support reconciliation; no file Apply is exposed.</summary>
public sealed record FileRecoveryObservation(string State, string ReceiptHash, bool BindingCurrent)
{
    [JsonIgnore] public string ReceiptJson { get; init; } = "";
    public override string ToString() => $"FileRecoveryObservation({State})";
}
public sealed class FileRecoveryBusyException : InvalidOperationException { }
public sealed record QueueFileRecoveryCommand(string Kind, Guid IntentId, long OriginalVersion, string Disposition, string ReasonCode);
public sealed record ConfirmFileRecoveryCommand(string PlanHash, bool Confirmed, long Version);
public sealed record FileRecoveryRequestBinding(Guid Id, string Actor, string LoginBinding, string IdempotencyKey,
    FileRecoveryTargetBinding Target, string Disposition, string ReasonCode, DateTimeOffset CreatedAtUtc, DateTimeOffset PlanningExpiresAtUtc);
public sealed record FileRecoveryConfirmationDto(string PlanHash, DateTimeOffset ExpiresAtUtc, string Warning);
public sealed record FileRecoveryIntentDto(Guid Id, Guid PlanningJobId, Guid? ExecutionJobId, string Kind, Guid OriginalIntentId,
    Guid OriginalExecutionJobId, long OriginalVersion, string State, string Disposition, string ReasonCode,
    string? RootState, DateTimeOffset? ReadyExpiresAtUtc, long Version, string? ResultCode, string? OriginalResolution,
    FileRecoveryConfirmationDto? Confirmation);
