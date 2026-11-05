using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

public static class ContainerRecycleWorkflowValidation
{
    public static readonly JsonSerializerOptions Json = new() { MaxDepth = 16, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    public static DateTimeOffset Time(DateTimeOffset value) => value.ToUniversalTime().AddTicks(-(value.Ticks % 10));
    private static void Timestamp(DateTimeOffset value)
    { if (value <= DateTimeOffset.UnixEpoch || value.Offset != TimeSpan.Zero || value.Ticks % 10 != 0) throw Invalid(); }
    private static void Actor(string actor, string login)
    { if (string.IsNullOrWhiteSpace(actor) || actor.Length > 256 || actor.Any(char.IsControl) || !FileManagementService.Hex(login)) throw Invalid(); }
    public static Guid Key(string key)
    {
        if (!Guid.TryParseExact(key, "D", out var id) || id == Guid.Empty || id.ToString("D") != key) throw new ArgumentException("CONTAINER_RECYCLE_IDEMPOTENCY_KEY_REQUIRED");
        return id;
    }
    public static Guid IntentId(string actor, string login, Guid key)
    {
        Actor(actor, login); if (key == Guid.Empty) throw Invalid();
        return new(SHA256.HashData(Encoding.UTF8.GetBytes("whaledeck-container-recycle-request-id-v1:" + BffSessionValidation.SubjectHash(actor) + ":" + login + ":" + key.ToString("N"))).AsSpan(0, 16));
    }
    public static Guid ExecutionId(Guid id)
    { if (id == Guid.Empty) throw Invalid(); return new(SHA256.HashData(Encoding.UTF8.GetBytes("whaledeck-container-recycle-execution-id-v1:" + id.ToString("N"))).AsSpan(0, 16)); }
    public static string Owner(string actor, string login)
    { Actor(actor, login); return FileManagementService.Hash("whaledeck-container-recycle-owner-v1:" + BffSessionValidation.SubjectHash(actor) + ":" + login); }
    public static void Request(ContainerRecycleRequestBinding value)
    {
        if (value is null) throw Invalid(); Actor(value.Actor, value.LoginBinding); Timestamp(value.CreatedAtUtc); Timestamp(value.PlanningExpiresAtUtc);
        if (value.Id != IntentId(value.Actor, value.LoginBinding, value.IdempotencyKey) || value.Owner != Owner(value.Actor, value.LoginBinding) ||
            string.IsNullOrWhiteSpace(value.TraceId) || value.TraceId.Length > 128 || value.TraceId.Any(char.IsControl) ||
            value.PlanningExpiresAtUtc != value.CreatedAtUtc.AddMinutes(15)) throw Invalid();
        ContainerRecycleValidation.Command(value.Command);
    }
    public static void Plan(ContainerRecyclePlanBinding value)
    {
        if (value is null) throw Invalid(); Request(value.Request); ContainerRecycleValidation.Plan(value.Plan); Timestamp(value.ReadyExpiresAtUtc);
        if (value.Plan.Command != value.Request.Command || value.ReadyExpiresAtUtc <= value.Request.CreatedAtUtc ||
            value.ReadyExpiresAtUtc > value.Request.PlanningExpiresAtUtc.AddMinutes(5)) throw Invalid();
    }
    public static void Execution(ContainerRecycleExecutionBinding value)
    {
        if (value is null) throw Invalid(); Actor(value.Actor, value.LoginBinding); Timestamp(value.ConfirmedAtUtc); Timestamp(value.ExecutionExpiresAtUtc);
        if (value.Id == Guid.Empty || value.ExecutionJobId != ExecutionId(value.Id) || !FileManagementService.Hex(value.PlanHash) ||
            value.ExecutionExpiresAtUtc != value.ConfirmedAtUtc.AddMinutes(15)) throw Invalid();
    }
    public static void Confirmation(ContainerRecycleConfirmation value)
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
    // Freeze the complete source/plan before any await. Canonical persistence
    // retains explicit zero values and exact nanosecond/uint64 identities.
    public static ContainerRecycleCommandDto SnapshotCommand(ContainerRecycleCommandDto command)
    { ContainerRecycleValidation.Command(command); var value = Read<ContainerRecycleCommandDto>(Serialize(command)); ContainerRecycleValidation.Command(value); return value; }
    public static ContainerRecyclePlanDto SnapshotPlan(ContainerRecyclePlanDto plan)
    { ContainerRecycleValidation.Plan(plan); var value = Read<ContainerRecyclePlanDto>(Serialize(plan)); ContainerRecycleValidation.Plan(value); return value; }
    public static ContainerRecycleCommandDto ReadCommand(ContainerRecycleIntent row)
    {
        var value = Read<ContainerRecycleCommandDto>(row.CommandJson); ContainerRecycleValidation.Command(value);
        if (value.Location != new ContainerFileLocationDto(row.ContainerId, row.InstanceId, row.RelativePath)) throw Invalid();
        return value;
    }
    public static ContainerRecyclePlanDto ReadPlan(ContainerRecycleIntent row)
    {
        var value = Read<ContainerRecyclePlanDto>(row.PlanJson); ContainerRecycleValidation.Plan(value);
        if (value.Command != ReadCommand(row)) throw Invalid(); return value;
    }
    public static ContainerRecycleRequestBinding RequestBinding(ContainerRecycleIntent row) => new(row.Id, row.IdempotencyKey, row.ActorSubject, row.LoginBinding,
        row.TraceId, row.OwnerHash, ReadCommand(row), row.CreatedAtUtc, row.PlanningExpiresAtUtc);
    public static ContainerRecyclePlanBinding PlanBinding(ContainerRecycleIntent row) => new(RequestBinding(row), ReadPlan(row), row.ReadyExpiresAtUtc ?? throw Invalid());
    public static ContainerRecycleExecutionBinding ExecutionBinding(ContainerRecycleIntent row) => new(row.Id, row.ActorSubject, row.LoginBinding,
        row.PlanHash ?? throw Invalid(), row.ExecutionJobId ?? throw Invalid(), row.ConfirmedAtUtc ?? throw Invalid(), row.ExecutionExpiresAtUtc ?? throw Invalid());
    public static ContainerRecycleOperationDto Reference(ContainerRecycleIntent row)
    {
        var value = new ContainerRecycleOperationDto(row.ExecutionJobId ?? throw Invalid(), row.OwnerHash, ReadPlan(row),
            new(row.ActorSubject, row.TraceId, row.ExecutionJobId.Value, row.IdempotencyKey));
        ContainerRecycleValidation.Operation(value); return value;
    }
    public static ContainerRecycleReceiptDto? ReadReceipt(ContainerRecycleIntent row)
    {
        if (row.ReceiptJson is null) return null;
        var receipt = Read<ContainerRecycleReceiptDto>(row.ReceiptJson);
        ContainerRecycleValidation.Receipt(Reference(row), receipt);
        if (row.RootState != receipt.State || row.SourceRetained != receipt.SourceRetained || row.SourceRestored != receipt.SourceRestored ||
            row.BindingCurrent != receipt.BindingCurrent || row.HistoryAvailable != receipt.HistoryAvailable ||
            row.HistoryVerified != receipt.HistoryVerified || receipt.HistoryMayExist && !row.HistoryMayExist ||
            row.HistoryDigest != (receipt.HistoryDigest.Length == 0 ? null : receipt.HistoryDigest)) throw Invalid();
        return receipt;
    }
    public static void RequireCompatibleReceipt(ContainerRecycleIntent row, ContainerRecycleReceiptDto receipt)
    {
        ContainerRecycleValidation.Receipt(Reference(row), receipt);
        var prior = ReadReceipt(row);
        // Current binding and HDD availability may change. The recorded
        // outcome and verified history may not be rewritten by acknowledgement.
        if (prior is not null && (prior.State != receipt.State || prior.SourceRetained != receipt.SourceRetained || prior.SourceRestored != receipt.SourceRestored ||
            prior.HistoryVerified != receipt.HistoryVerified || prior.HistoryDigest != receipt.HistoryDigest || prior.HistoryMayExist && !receipt.HistoryMayExist))
            throw new InvalidOperationException("CONTAINER_RECYCLE_FACT_CHANGED");
    }
    private static bool ValidFacts(ContainerRecycleIntent row)
    {
        if (row.ReceiptJson is not null)
        {
            var receipt = ReadReceipt(row)!;
            return row.State switch
            {
                "Succeeded" => receipt.State == "Trashed" && receipt.BindingCurrent,
                "Superseded" => receipt.State == "Restored" || receipt.State == "Trashed" && !receipt.BindingCurrent,
                "Unknown" => receipt.State == "Unknown",
                "Canceled" or "Failed" => receipt.State == "Aborted",
                "Acknowledged" => true,
                _ => false
            };
        }
        if (row.State is "Succeeded" or "Superseded" or "Acknowledged" || row.ExecutionJobId is not null && row.State is "Canceled" or "Failed") return false;
        return row.RootState is null && row.SourceRetained is null && row.SourceRestored is null && row.BindingCurrent is null && row.HistoryAvailable is null &&
            !row.HistoryVerified && row.HistoryDigest is null && (!row.HistoryMayExist || row.RootStartedAtUtc is not null);
    }
    public static bool HasValidBinding(ContainerRecycleIntent row, IContainerRecycleProtector protector)
    {
        try
        {
            var request = RequestBinding(row); Request(request);
            if (!Equal(row.RequestHash, protector.FingerprintRecycleRequest(request)) || !ValidFacts(row)) return false;
            if (row.ReceiptJson is null ? row.ReceiptHash is not null :
                !Equal(row.ReceiptHash, protector.FingerprintRecycleReceipt(new(Reference(row), ReadReceipt(row)!)))) return false;
            if (row.PlanJson is null) return row.PlanHash is null && row.ReadyExpiresAtUtc is null && NoExecution(row) && row.RootStartedAtUtc is null && !row.PublishBarrier;
            var plan = PlanBinding(row); Plan(plan);
            if (!Equal(row.PlanHash, protector.FingerprintRecyclePlan(plan))) return false;
            if (NoExecution(row)) return row.RootStartedAtUtc is null && !row.PublishBarrier;
            var execution = ExecutionBinding(row); Execution(execution);
            return execution.ConfirmedAtUtc >= row.CreatedAtUtc && execution.ConfirmedAtUtc < row.ReadyExpiresAtUtc &&
                Equal(row.ExecutionHash, protector.FingerprintRecycleExecution(execution));
        }
        catch (Exception error) when (error is ArgumentException or JsonException or InvalidOperationException or NullReferenceException) { return false; }
    }
    private static bool NoExecution(ContainerRecycleIntent row) => row.ExecutionJobId is null && row.ExecutionExpiresAtUtc is null && row.ConfirmedAtUtc is null && row.ExecutionHash is null;
    private static bool Equal(string? left, string right) => FileManagementService.Hex(left) && FileManagementService.Hex(right) &&
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(left!), Encoding.ASCII.GetBytes(right));
    public static ArgumentException Invalid() => new("CONTAINER_RECYCLE_INTENT_INVALID");
}
