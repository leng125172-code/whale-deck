using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

public static class ContainerAclWorkflowValidation
{
    public static readonly JsonSerializerOptions Json = new() { MaxDepth = 16, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    public static DateTimeOffset Time(DateTimeOffset value) => value.ToUniversalTime().AddTicks(-(value.Ticks % 10));
    private static void Timestamp(DateTimeOffset value)
    { if (value <= DateTimeOffset.UnixEpoch || value.Offset != TimeSpan.Zero || value.Ticks % 10 != 0) throw Invalid(); }
    private static void Actor(string actor, string login)
    { if (string.IsNullOrWhiteSpace(actor) || actor.Length > 256 || actor.Any(char.IsControl) || !FileManagementService.Hex(login)) throw Invalid(); }
    public static Guid Key(string key)
    {
        if (!Guid.TryParseExact(key, "D", out var id) || id == Guid.Empty || id.ToString("D") != key) throw new ArgumentException("CONTAINER_ACL_IDEMPOTENCY_KEY_REQUIRED");
        return id;
    }
    public static Guid IntentId(string actor, string login, Guid key)
    {
        Actor(actor, login); if (key == Guid.Empty) throw Invalid();
        return new(SHA256.HashData(Encoding.UTF8.GetBytes("whaledeck-container-acl-request-id-v1:" + BffSessionValidation.SubjectHash(actor) + ":" + login + ":" + key.ToString("N"))).AsSpan(0, 16));
    }
    public static Guid ExecutionId(Guid id)
    { if (id == Guid.Empty) throw Invalid(); return new(SHA256.HashData(Encoding.UTF8.GetBytes("whaledeck-container-acl-execution-id-v1:" + id.ToString("N"))).AsSpan(0, 16)); }
    public static string Owner(string actor, string login)
    { Actor(actor, login); return FileManagementService.Hash("whaledeck-container-acl-owner-v1:" + BffSessionValidation.SubjectHash(actor) + ":" + login); }
    public static void Request(ContainerAclRequestBinding value)
    {
        if (value is null) throw Invalid(); Actor(value.Actor, value.LoginBinding); Timestamp(value.CreatedAtUtc); Timestamp(value.PlanningExpiresAtUtc);
        if (value.Id != IntentId(value.Actor, value.LoginBinding, value.IdempotencyKey) || value.Owner != Owner(value.Actor, value.LoginBinding) ||
            string.IsNullOrWhiteSpace(value.TraceId) || value.TraceId.Length > 128 || value.TraceId.Any(char.IsControl) ||
            value.PlanningExpiresAtUtc != value.CreatedAtUtc.AddMinutes(15)) throw Invalid();
        ContainerAclValidation.Command(value.Command);
    }
    public static void Plan(ContainerAclPlanBinding value)
    {
        if (value is null) throw Invalid(); Request(value.Request); ContainerAclValidation.Plan(value.Plan); Timestamp(value.ReadyExpiresAtUtc);
        if (!ContainerAclValidation.SameCommand(value.Plan.Command, value.Request.Command) || value.ReadyExpiresAtUtc <= value.Request.CreatedAtUtc ||
            value.ReadyExpiresAtUtc > value.Request.PlanningExpiresAtUtc.AddMinutes(5)) throw Invalid();
    }
    public static void Execution(ContainerAclExecutionBinding value)
    {
        if (value is null) throw Invalid(); Actor(value.Actor, value.LoginBinding); Timestamp(value.ConfirmedAtUtc); Timestamp(value.ExecutionExpiresAtUtc);
        if (value.Id == Guid.Empty || value.ExecutionJobId != ExecutionId(value.Id) || !FileManagementService.Hex(value.PlanHash) ||
            value.ExecutionExpiresAtUtc != value.ConfirmedAtUtc.AddMinutes(15)) throw Invalid();
    }
    public static void Confirmation(ContainerAclConfirmation value)
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
    // Deep snapshots remove caller-owned mutable list aliases before the first repository/Agent await.
    public static ContainerAclCommandDto SnapshotCommand(ContainerAclCommandDto command)
    { var value = Read<ContainerAclCommandDto>(Serialize(command)); ContainerAclValidation.Command(value); return value; }
    public static ContainerAclPlanDto SnapshotPlan(ContainerAclPlanDto plan)
    { var value = Read<ContainerAclPlanDto>(Serialize(plan)); ContainerAclValidation.Plan(value); return value; }
    public static ContainerAclReceiptDto? ReadReceipt(ContainerAclIntent row)
    {
        if (row.ReceiptJson is null) return null;
        var receipt = Read<ContainerAclReceiptDto>(row.ReceiptJson); ContainerAclValidation.Receipt(Reference(row), receipt);
        if (row.AclConfirmed != receipt.AclConfirmed || row.HistoryVerified != receipt.HistoryVerified ||
            row.HistoryDigest != (receipt.HistoryVerified ? receipt.HistoryDigest : null) || receipt.HistoryMayExist && !row.HistoryMayExist ||
            row.PublishedVersion != (receipt.State == "Completed" ? receipt.Version : null)) throw Invalid();
        return receipt;
    }
    public static void RequireCompatibleReceipt(ContainerAclIntent row, ContainerAclReceiptDto receipt)
    {
        ContainerAclValidation.Receipt(Reference(row), receipt);
        var prior = ReadReceipt(row);
        // Root historical facts remain immutable; current binding/replay are observations only.
        if (prior is not null && (prior.State != receipt.State || prior.AclConfirmed != receipt.AclConfirmed || prior.Version != receipt.Version ||
            prior.HistoryVerified != receipt.HistoryVerified || prior.HistoryDigest != receipt.HistoryDigest || prior.HistoryMayExist && !receipt.HistoryMayExist))
            throw new InvalidOperationException("CONTAINER_ACL_FACT_CHANGED");
    }
    private static bool ValidFacts(ContainerAclIntent row)
    {
        if (row.ReceiptJson is not null)
        {
            var receipt = ReadReceipt(row)!;
            return row.State switch {
                "Succeeded" => receipt.State == "Completed" && receipt.BindingCurrent,
                "Superseded" => receipt.State == "Completed" && !receipt.BindingCurrent,
                "Unknown" => receipt.State == "Unknown", "Canceled" or "Failed" => receipt.State == "Aborted",
                "Acknowledged" => true, _ => false };
        }
        if (row.State is "Succeeded" or "Superseded" or "Acknowledged" || row.ExecutionJobId is not null && row.State is "Canceled" or "Failed") return false;
        return row.AclConfirmed is null && row.PublishedVersion is null && !row.HistoryVerified && row.HistoryDigest is null &&
            (!row.HistoryMayExist || row.RootStartedAtUtc is not null);
    }
    public static ContainerAclCommandDto ReadCommand(ContainerAclIntent row)
    {
        var value = Read<ContainerAclCommandDto>(row.CommandJson); ContainerAclValidation.Command(value);
        if (value.Location != new ContainerFileLocationDto(row.ContainerId, row.InstanceId, row.RelativePath)) throw Invalid();
        return value;
    }
    public static ContainerAclPlanDto ReadPlan(ContainerAclIntent row)
    {
        var value = Read<ContainerAclPlanDto>(row.PlanJson); ContainerAclValidation.Plan(value);
        if (!ContainerAclValidation.SameCommand(value.Command, ReadCommand(row))) throw Invalid(); return value;
    }
    public static ContainerAclRequestBinding RequestBinding(ContainerAclIntent row) => new(row.Id, row.IdempotencyKey, row.ActorSubject, row.LoginBinding,
        row.TraceId, row.OwnerHash, ReadCommand(row), row.CreatedAtUtc, row.PlanningExpiresAtUtc);
    public static ContainerAclPlanBinding PlanBinding(ContainerAclIntent row) => new(RequestBinding(row), ReadPlan(row), row.ReadyExpiresAtUtc ?? throw Invalid());
    public static ContainerAclExecutionBinding ExecutionBinding(ContainerAclIntent row) => new(row.Id, row.ActorSubject, row.LoginBinding,
        row.PlanHash ?? throw Invalid(), row.ExecutionJobId ?? throw Invalid(), row.ConfirmedAtUtc ?? throw Invalid(), row.ExecutionExpiresAtUtc ?? throw Invalid());
    public static ContainerAclOperationDto Reference(ContainerAclIntent row)
    {
        var value = new ContainerAclOperationDto(row.ExecutionJobId ?? throw Invalid(), row.OwnerHash, ReadPlan(row),
            new(row.ActorSubject, row.TraceId, row.ExecutionJobId.Value, row.IdempotencyKey));
        ContainerAclValidation.Operation(value); return value;
    }
    public static bool HasValidBinding(ContainerAclIntent row, IContainerAclProtector protector)
    {
        try
        {
            var request = RequestBinding(row); Request(request);
            if (!Equal(row.RequestHash, protector.FingerprintAclRequest(request)) || !ValidFacts(row)) return false;
            if (row.ReceiptJson is null ? row.ReceiptHash is not null :
                !Equal(row.ReceiptHash, protector.FingerprintAclReceipt(new(Reference(row), ReadReceipt(row)!)))) return false;
            if (row.PlanJson is null) return row.PlanHash is null && row.ReadyExpiresAtUtc is null && NoExecution(row) && row.RootStartedAtUtc is null && !row.PublishBarrier;
            var plan = PlanBinding(row); Plan(plan);
            if (!Equal(row.PlanHash, protector.FingerprintAclPlan(plan))) return false;
            if (NoExecution(row)) return row.RootStartedAtUtc is null && !row.PublishBarrier;
            var execution = ExecutionBinding(row); Execution(execution);
            return execution.ConfirmedAtUtc >= row.CreatedAtUtc && execution.ConfirmedAtUtc < row.ReadyExpiresAtUtc &&
                Equal(row.ExecutionHash, protector.FingerprintAclExecution(execution));
        }
        catch (Exception error) when (error is ArgumentException or JsonException or InvalidOperationException or NullReferenceException) { return false; }
    }
    private static bool NoExecution(ContainerAclIntent row) => row.ExecutionJobId is null && row.ExecutionExpiresAtUtc is null && row.ConfirmedAtUtc is null && row.ExecutionHash is null;
    private static bool Equal(string? left, string right) => FileManagementService.Hex(left) && FileManagementService.Hex(right) &&
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(left!), Encoding.ASCII.GetBytes(right));
    public static ArgumentException Invalid() => new("CONTAINER_ACL_INTENT_INVALID");
}
