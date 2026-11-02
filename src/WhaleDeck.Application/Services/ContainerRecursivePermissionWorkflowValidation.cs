using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

public static class ContainerRecursivePermissionWorkflowValidation
{
    public static readonly JsonSerializerOptions Json = new() { MaxDepth = 16, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    public static DateTimeOffset Time(DateTimeOffset value) => value.ToUniversalTime().AddTicks(-(value.Ticks % 10));
    private static void Timestamp(DateTimeOffset value)
    { if (value <= DateTimeOffset.UnixEpoch || value.Offset != TimeSpan.Zero || value.Ticks % 10 != 0) throw Invalid(); }
    private static void Actor(string actor, string login)
    { if (string.IsNullOrWhiteSpace(actor) || actor.Length > 256 || actor.Any(char.IsControl) || !FileManagementService.Hex(login)) throw Invalid(); }
    public static Guid Key(string key)
    {
        if (!Guid.TryParseExact(key, "D", out var id) || id == Guid.Empty || id.ToString("D") != key) throw new ArgumentException("CONTAINER_RECURSIVE_PERMISSION_IDEMPOTENCY_KEY_REQUIRED");
        return id;
    }
    public static Guid IntentId(string actor, string login, Guid key)
    {
        Actor(actor, login); if (key == Guid.Empty) throw Invalid();
        return new(SHA256.HashData(Encoding.UTF8.GetBytes("whaledeck-container-recursive-permission-request-id-v1:" + BffSessionValidation.SubjectHash(actor) + ":" + login + ":" + key.ToString("N"))).AsSpan(0, 16));
    }
    public static Guid ExecutionId(Guid id)
    { if (id == Guid.Empty) throw Invalid(); return new(SHA256.HashData(Encoding.UTF8.GetBytes("whaledeck-container-recursive-permission-execution-id-v1:" + id.ToString("N"))).AsSpan(0, 16)); }
    public static string Owner(string actor, string login)
    { Actor(actor, login); return FileManagementService.Hash("whaledeck-container-recursive-permission-owner-v1:" + BffSessionValidation.SubjectHash(actor) + ":" + login); }
    public static void Request(ContainerRecursivePermissionRequestBinding value)
    {
        if (value is null) throw Invalid(); Actor(value.Actor, value.LoginBinding); Timestamp(value.CreatedAtUtc); Timestamp(value.PlanningExpiresAtUtc);
        if (value.Id != IntentId(value.Actor, value.LoginBinding, value.IdempotencyKey) || value.Owner != Owner(value.Actor, value.LoginBinding) ||
            string.IsNullOrWhiteSpace(value.TraceId) || value.TraceId.Length > 128 || value.TraceId.Any(char.IsControl) ||
            value.PlanningExpiresAtUtc != value.CreatedAtUtc.AddMinutes(15)) throw Invalid();
        ContainerRecursivePermissionValidation.Command(value.Command);
    }
    public static void Plan(ContainerRecursivePermissionPlanBinding value)
    {
        if (value is null) throw Invalid(); Request(value.Request); ContainerRecursivePermissionValidation.Plan(value.Plan); Timestamp(value.ReadyExpiresAtUtc);
        if (value.Plan.Command != value.Request.Command || value.ReadyExpiresAtUtc <= value.Request.CreatedAtUtc ||
            value.ReadyExpiresAtUtc > value.Request.PlanningExpiresAtUtc.AddMinutes(5)) throw Invalid();
    }
    public static void Execution(ContainerRecursivePermissionExecutionBinding value)
    {
        if (value is null) throw Invalid(); Actor(value.Actor, value.LoginBinding); Timestamp(value.ConfirmedAtUtc); Timestamp(value.ExecutionExpiresAtUtc);
        if (value.Id == Guid.Empty || value.ExecutionJobId != ExecutionId(value.Id) || !FileManagementService.Hex(value.PlanHash) ||
            value.ExecutionExpiresAtUtc != value.ConfirmedAtUtc.AddMinutes(15)) throw Invalid();
    }
    public static void Confirmation(ContainerRecursivePermissionConfirmation value)
    {
        if (value is null) throw Invalid(); Actor(value.Actor, value.LoginBinding); Timestamp(value.ExpiresAtUtc);
        if (value.Id == Guid.Empty || !FileManagementService.Hex(value.PlanHash)) throw Invalid();
    }
    public static string Serialize<T>(T value)
    {
        var text = JsonSerializer.Serialize(value, Json);
        if (Encoding.UTF8.GetByteCount(text) > 65536) throw Invalid(); return text;
    }
    private static T Read<T>(string? text)
    {
        if (text is null || text.Length is < 1 or > 65536 || Encoding.UTF8.GetByteCount(text) > 65536) throw Invalid();
        var value = JsonSerializer.Deserialize<T>(text, Json) ?? throw Invalid();
        if (Serialize(value) != text) throw Invalid(); return value;
    }
    public static ContainerRecursivePermissionCommandDto ReadCommand(ContainerRecursivePermissionIntent row)
    {
        var value = Read<ContainerRecursivePermissionCommandDto>(row.CommandJson); ContainerRecursivePermissionValidation.Command(value);
        if (value.Location != new ContainerFileLocationDto(row.ContainerId, row.InstanceId, row.RelativePath)) throw Invalid();
        return value;
    }
    public static ContainerRecursivePermissionPlanDto ReadPlan(ContainerRecursivePermissionIntent row)
    {
        var value = Read<ContainerRecursivePermissionPlanDto>(row.PlanJson); ContainerRecursivePermissionValidation.Plan(value);
        if (value.Command != ReadCommand(row)) throw Invalid(); return value;
    }
    public static ContainerRecursivePermissionRequestBinding RequestBinding(ContainerRecursivePermissionIntent row) => new(row.Id, row.IdempotencyKey, row.ActorSubject, row.LoginBinding,
        row.TraceId, row.OwnerHash, ReadCommand(row), row.CreatedAtUtc, row.PlanningExpiresAtUtc);
    public static ContainerRecursivePermissionPlanBinding PlanBinding(ContainerRecursivePermissionIntent row) => new(RequestBinding(row), ReadPlan(row), row.ReadyExpiresAtUtc ?? throw Invalid());
    public static ContainerRecursivePermissionExecutionBinding ExecutionBinding(ContainerRecursivePermissionIntent row) => new(row.Id, row.ActorSubject, row.LoginBinding,
        row.PlanHash ?? throw Invalid(), row.ExecutionJobId ?? throw Invalid(), row.ConfirmedAtUtc ?? throw Invalid(), row.ExecutionExpiresAtUtc ?? throw Invalid());
    public static ContainerRecursivePermissionOperationDto Reference(ContainerRecursivePermissionIntent row)
    {
        var value = new ContainerRecursivePermissionOperationDto(row.ExecutionJobId ?? throw Invalid(), row.OwnerHash, ReadPlan(row),
            new(row.ActorSubject, row.TraceId, row.ExecutionJobId.Value, row.IdempotencyKey));
        ContainerRecursivePermissionValidation.Operation(value); return value;
    }
    public static ContainerRecursivePermissionReceiptDto? ReadReceipt(ContainerRecursivePermissionIntent row)
    {
        if (row.ReceiptJson is null) return null;
        var receipt = Read<ContainerRecursivePermissionReceiptDto>(row.ReceiptJson);
        ContainerRecursivePermissionValidation.Receipt(Reference(row), receipt);
        if (row.RootState != receipt.State || row.CompletedCount != receipt.CompletedCount || row.ChangedCount != receipt.ChangedCount ||
            row.AbortedCount != receipt.AbortedCount || row.NotStartedCount != receipt.NotStartedCount || row.UnknownCount != receipt.UnknownCount ||
            row.OutcomeDigest != receipt.OutcomeDigest || row.BindingCurrent != receipt.BindingCurrent || row.HistoryAvailable != receipt.HistoryAvailable ||
            row.HistoryVerified != receipt.HistoryVerified || receipt.HistoryMayExist && !row.HistoryMayExist ||
            row.HistoryDigest != (receipt.HistoryDigest.Length == 0 ? null : receipt.HistoryDigest)) throw Invalid();
        return receipt;
    }
    public static void RequireCompatibleReceipt(ContainerRecursivePermissionIntent row, ContainerRecursivePermissionReceiptDto receipt)
    {
        ContainerRecursivePermissionValidation.Receipt(Reference(row), receipt);
        var prior = ReadReceipt(row);
        // BindingCurrent / HistoryAvailable / Replayed are observations, not
        // immutable outcomes. A missing HDD must not be reported as available
        // just because an older response was available. Counts/digests and
        // historical verification cannot be erased by acknowledgement.
        if (prior is not null && (prior.State != receipt.State || prior.CompletedCount != receipt.CompletedCount || prior.ChangedCount != receipt.ChangedCount ||
            prior.AbortedCount != receipt.AbortedCount || prior.NotStartedCount != receipt.NotStartedCount || prior.UnknownCount != receipt.UnknownCount ||
            prior.OutcomeDigest != receipt.OutcomeDigest || prior.HistoryVerified != receipt.HistoryVerified || prior.HistoryDigest != receipt.HistoryDigest ||
            prior.HistoryMayExist && !receipt.HistoryMayExist))
            throw new InvalidOperationException("CONTAINER_RECURSIVE_PERMISSION_FACT_CHANGED");
    }
    private static bool ValidFacts(ContainerRecursivePermissionIntent row)
    {
        if (row.ReceiptJson is not null)
        {
            var receipt = ReadReceipt(row)!;
            return row.State switch
            {
                "Succeeded" => receipt.State == "Completed" && receipt.BindingCurrent,
                "Superseded" => receipt.State == "Completed" && !receipt.BindingCurrent,
                "Partial" => receipt.State == "Partial",
                "Unknown" => receipt.State == "Unknown",
                "Canceled" or "Failed" => receipt.State == "Aborted",
                "Acknowledged" => true,
                _ => false
            };
        }
        if (row.State is "Succeeded" or "Superseded" or "Partial" or "Acknowledged" || row.ExecutionJobId is not null && row.State is "Canceled" or "Failed") return false;
        return row.RootState is null && row.CompletedCount is null && row.ChangedCount is null && row.AbortedCount is null && row.NotStartedCount is null &&
            row.UnknownCount is null && row.OutcomeDigest is null && row.BindingCurrent is null && row.HistoryAvailable is null &&
            !row.HistoryVerified && row.HistoryDigest is null && (!row.HistoryMayExist || row.RootStartedAtUtc is not null);
    }
    public static bool HasValidBinding(ContainerRecursivePermissionIntent row, IContainerRecursivePermissionProtector protector)
    {
        try
        {
            var request = RequestBinding(row); Request(request);
            if (!Equal(row.RequestHash, protector.FingerprintRecursivePermissionRequest(request)) || !ValidFacts(row)) return false;
            if (row.PlanJson is null) return row.PlanHash is null && row.ReadyExpiresAtUtc is null && NoExecution(row) && row.RootStartedAtUtc is null && !row.PublishBarrier;
            var plan = PlanBinding(row); Plan(plan);
            if (!Equal(row.PlanHash, protector.FingerprintRecursivePermissionPlan(plan))) return false;
            if (NoExecution(row)) return row.RootStartedAtUtc is null && !row.PublishBarrier;
            var execution = ExecutionBinding(row); Execution(execution);
            return execution.ConfirmedAtUtc >= row.CreatedAtUtc && execution.ConfirmedAtUtc < row.ReadyExpiresAtUtc &&
                Equal(row.ExecutionHash, protector.FingerprintRecursivePermissionExecution(execution));
        }
        catch (Exception error) when (error is ArgumentException or JsonException or InvalidOperationException or NullReferenceException) { return false; }
    }
    private static bool NoExecution(ContainerRecursivePermissionIntent row) => row.ExecutionJobId is null && row.ExecutionExpiresAtUtc is null && row.ConfirmedAtUtc is null && row.ExecutionHash is null;
    private static bool Equal(string? left, string right) => FileManagementService.Hex(left) && FileManagementService.Hex(right) &&
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(left!), Encoding.ASCII.GetBytes(right));
    public static ArgumentException Invalid() => new("CONTAINER_RECURSIVE_PERMISSION_INTENT_INVALID");
}
