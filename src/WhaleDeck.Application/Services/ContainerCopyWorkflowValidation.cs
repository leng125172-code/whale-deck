using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

public static class ContainerCopyWorkflowValidation
{
    public static readonly JsonSerializerOptions Json = new() { MaxDepth = 16, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    public static DateTimeOffset Time(DateTimeOffset value) => value.ToUniversalTime().AddTicks(-(value.Ticks % 10));
    private static void Timestamp(DateTimeOffset value)
    { if (value <= DateTimeOffset.UnixEpoch || value.Offset != TimeSpan.Zero || value.Ticks % 10 != 0) throw Invalid(); }
    private static void Actor(string actor, string login)
    { if (string.IsNullOrWhiteSpace(actor) || actor.Length > 256 || actor.Any(char.IsControl) || !FileManagementService.Hex(login)) throw Invalid(); }
    public static Guid Key(string key)
    {
        if (!Guid.TryParseExact(key, "D", out var id) || id == Guid.Empty || id.ToString("D") != key) throw new ArgumentException("CONTAINER_COPY_IDEMPOTENCY_KEY_REQUIRED");
        return id;
    }
    public static Guid IntentId(string actor, string login, Guid key)
    {
        Actor(actor, login); if (key == Guid.Empty) throw Invalid();
        return new(SHA256.HashData(Encoding.UTF8.GetBytes("whaledeck-container-copy-request-id-v1:" + BffSessionValidation.SubjectHash(actor) + ":" + login + ":" + key.ToString("N"))).AsSpan(0, 16));
    }
    public static Guid ExecutionId(Guid id)
    { if (id == Guid.Empty) throw Invalid(); return new(SHA256.HashData(Encoding.UTF8.GetBytes("whaledeck-container-copy-execution-id-v1:" + id.ToString("N"))).AsSpan(0, 16)); }
    public static string Owner(string actor, string login)
    { Actor(actor, login); return FileManagementService.Hash("whaledeck-container-copy-owner-v1:" + BffSessionValidation.SubjectHash(actor) + ":" + login); }
    public static void Request(ContainerCopyRequestBinding value)
    {
        if (value is null) throw Invalid(); Actor(value.Actor, value.LoginBinding); Timestamp(value.CreatedAtUtc); Timestamp(value.PlanningExpiresAtUtc);
        if (value.Id != IntentId(value.Actor, value.LoginBinding, value.IdempotencyKey) || value.Owner != Owner(value.Actor, value.LoginBinding) ||
            string.IsNullOrWhiteSpace(value.TraceId) || value.TraceId.Length > 128 || value.TraceId.Any(char.IsControl) ||
            value.PlanningExpiresAtUtc != value.CreatedAtUtc.AddMinutes(15)) throw Invalid();
        ContainerCopyValidation.Command(value.Command);
    }
    public static void Plan(ContainerCopyPlanBinding value)
    {
        if (value is null) throw Invalid(); Request(value.Request); ContainerCopyValidation.Plan(value.Plan); Timestamp(value.ReadyExpiresAtUtc);
        if (value.Plan.Command != value.Request.Command || value.ReadyExpiresAtUtc <= value.Request.CreatedAtUtc ||
            value.ReadyExpiresAtUtc > value.Request.PlanningExpiresAtUtc.AddMinutes(5)) throw Invalid();
    }
    public static void Execution(ContainerCopyExecutionBinding value)
    {
        if (value is null) throw Invalid(); Actor(value.Actor, value.LoginBinding); Timestamp(value.ConfirmedAtUtc); Timestamp(value.ExecutionExpiresAtUtc);
        if (value.Id == Guid.Empty || value.ExecutionJobId != ExecutionId(value.Id) || !FileManagementService.Hex(value.PlanHash) ||
            value.ExecutionExpiresAtUtc != value.ConfirmedAtUtc.AddMinutes(15)) throw Invalid();
    }
    public static void Confirmation(ContainerCopyConfirmation value)
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
    public static ContainerCopyCommandDto ReadCommand(ContainerCopyIntent row)
    {
        var value = Read<ContainerCopyCommandDto>(row.CommandJson); ContainerCopyValidation.Command(value);
        if (value.Source != new ContainerFileLocationDto(row.ContainerId, row.InstanceId, row.SourcePath) || value.TargetPath != row.TargetPath) throw Invalid();
        return value;
    }
    public static ContainerCopyPlanDto ReadPlan(ContainerCopyIntent row)
    {
        var value = Read<ContainerCopyPlanDto>(row.PlanJson); ContainerCopyValidation.Plan(value);
        if (value.Command != ReadCommand(row)) throw Invalid(); return value;
    }
    public static ContainerCopyRequestBinding RequestBinding(ContainerCopyIntent row) => new(row.Id, row.IdempotencyKey, row.ActorSubject, row.LoginBinding,
        row.TraceId, row.OwnerHash, ReadCommand(row), row.CreatedAtUtc, row.PlanningExpiresAtUtc);
    public static ContainerCopyPlanBinding PlanBinding(ContainerCopyIntent row) => new(RequestBinding(row), ReadPlan(row), row.ReadyExpiresAtUtc ?? throw Invalid());
    public static ContainerCopyExecutionBinding ExecutionBinding(ContainerCopyIntent row) => new(row.Id, row.ActorSubject, row.LoginBinding,
        row.PlanHash ?? throw Invalid(), row.ExecutionJobId ?? throw Invalid(), row.ConfirmedAtUtc ?? throw Invalid(), row.ExecutionExpiresAtUtc ?? throw Invalid());
    public static ContainerCopyOperationDto Reference(ContainerCopyIntent row)
    {
        var value = new ContainerCopyOperationDto(row.ExecutionJobId ?? throw Invalid(), row.OwnerHash, ReadPlan(row),
            new(row.ActorSubject, row.TraceId, row.ExecutionJobId.Value, row.IdempotencyKey));
        ContainerCopyValidation.Operation(value); return value;
    }
    public static bool HasValidBinding(ContainerCopyIntent row, IContainerCopyProtector protector)
    {
        try
        {
            var request = RequestBinding(row); Request(request);
            if (!Equal(row.RequestHash, protector.FingerprintCopyRequest(request))) return false;
            if (row.PlanJson is null) return row.PlanHash is null && row.ReadyExpiresAtUtc is null && NoExecution(row) && row.RootStartedAtUtc is null && !row.PublishBarrier;
            var plan = PlanBinding(row); Plan(plan);
            if (!Equal(row.PlanHash, protector.FingerprintCopyPlan(plan))) return false;
            if (NoExecution(row)) return row.RootStartedAtUtc is null && !row.PublishBarrier;
            var execution = ExecutionBinding(row); Execution(execution);
            return execution.ConfirmedAtUtc >= row.CreatedAtUtc && execution.ConfirmedAtUtc < row.ReadyExpiresAtUtc &&
                Equal(row.ExecutionHash, protector.FingerprintCopyExecution(execution));
        }
        catch (Exception error) when (error is ArgumentException or JsonException or InvalidOperationException or NullReferenceException) { return false; }
    }
    private static bool NoExecution(ContainerCopyIntent row) => row.ExecutionJobId is null && row.ExecutionExpiresAtUtc is null && row.ConfirmedAtUtc is null && row.ExecutionHash is null;
    private static bool Equal(string? left, string right) => FileManagementService.Hex(left) && FileManagementService.Hex(right) &&
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(left!), Encoding.ASCII.GetBytes(right));
    public static ArgumentException Invalid() => new("CONTAINER_COPY_INTENT_INVALID");
}
