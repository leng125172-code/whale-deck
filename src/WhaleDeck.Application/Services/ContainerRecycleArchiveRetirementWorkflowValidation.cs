using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

public static class ContainerRecycleArchiveRetirementWorkflowValidation
{
    public const string Prefix = "CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_";
    private const string Domain = "whaledeck-container-recycle-archive-retirement-business-v1:";
    public static DateTimeOffset Time(DateTimeOffset value) => ContainerRecycleArchiveWorkflowValidation.Time(value);

    private static bool Timestamp(DateTimeOffset time) => time > DateTimeOffset.UnixEpoch && Time(time).EqualsExact(time);
    public static Guid IntentId(string actor, string login, Guid key) => Id("intent:" + BffSessionValidation.SubjectHash(actor) + ":" + login + ":" + key.ToString("N"));
    public static Guid ExecutionId(Guid id) => Id("execution:" + id.ToString("N"));
    public static string Owner(string actor, string login) => FileManagementService.Hash(Domain + "owner:" + BffSessionValidation.SubjectHash(actor) + ":" + login);

    private static Guid Id(string value) => new(SHA256.HashData(Encoding.UTF8.GetBytes(Domain + value)).AsSpan(0, 16));

    public static void Request(ContainerRecycleArchiveRetirementIntent row)
    {
        FileManagementService.ValidateActor(new(row.ActorSubject, null, [], row.LoginBinding, row.TraceId, null));
        if (row.Id == Guid.Empty || row.ArchiveIntentId == Guid.Empty || row.Id == row.ArchiveIntentId || row.IdempotencyKey == Guid.Empty ||
            row.Id != IntentId(row.ActorSubject, row.LoginBinding, row.IdempotencyKey) || row.OwnerHash != Owner(row.ActorSubject, row.LoginBinding) ||
            !FileManagementService.Hex(row.SourceProofHash) || row.RetainDays is < 1 or > 3650 || !Timestamp(row.CreatedAtUtc) ||
            row.PlanningExpiresAtUtc != row.CreatedAtUtc.AddMinutes(15))
            throw Invalid();
    }

    public static void Confirmation(RetirementConfirmation value)
    {
        if (value.Id == Guid.Empty || string.IsNullOrWhiteSpace(value.Actor) || value.Actor.Length > 256 || value.Actor.Any(char.IsControl) ||
            !FileManagementService.Hex(value.LoginBinding) || !FileManagementService.Hex(value.PlanHash) || !Timestamp(value.ExpiresAtUtc))
            throw Invalid();
    }

    public static string Write<T>(T value) => ContainerRecycleArchiveWorkflowValidation.Serialize(value);

    public static T Read<T>(string? json)
    {
        if (json is null || json.Length is < 1 or > 65536 || Encoding.UTF8.GetByteCount(json) > 65536) throw Invalid();
        var value = JsonSerializer.Deserialize<T>(json, ContainerRecycleArchiveWorkflowValidation.Json) ?? throw Invalid();
        if (Write(value) != json) throw Invalid();
        return value;
    }

    public static ContainerRecycleArchiveRetirementPlanDto ReadPlan(ContainerRecycleArchiveRetirementIntent row)
    {
        var plan = Read<ContainerRecycleArchiveRetirementPlanDto>(row.PlanJson);
        ContainerRecycleArchiveRetirementValidation.Plan(plan);
        if (!plan.BindingCurrent || plan.Command.RetainDays != row.RetainDays) throw Invalid();
        return plan;
    }

    public static ContainerRecycleArchiveRetirementOperationDto Operation(ContainerRecycleArchiveRetirementIntent row)
    {
        var id = row.ExecutionJobId ?? throw Invalid();
        if (id != ExecutionId(row.Id)) throw Invalid();
        var result = new ContainerRecycleArchiveRetirementOperationDto(id, row.OwnerHash, ReadPlan(row), new(row.ActorSubject, row.TraceId, id, row.IdempotencyKey));
        ContainerRecycleArchiveRetirementValidation.Operation(result);
        return result;
    }

    public static bool EqualMac(string? a, string b) => FileManagementService.Hex(a) && FileManagementService.Hex(b) &&
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(a!), Encoding.ASCII.GetBytes(b));

    public static void Verify(ContainerRecycleArchiveRetirementIntent row, IContainerRecycleArchiveRetirementProtector protector)
    {
        Request(row);
        if (!EqualMac(row.RequestHash, protector.FingerprintRetirementRequest(row))) throw Invalid();
        if (row.PlanJson is not null)
        {
            if (row.ReadyExpiresAtUtc is not { } expires || !Timestamp(expires) || expires <= row.CreatedAtUtc ||
                expires > row.PlanningExpiresAtUtc.AddMinutes(5) || !EqualMac(row.PlanHash, protector.FingerprintRetirementPlan(row))) throw Invalid();
        }
        else if (row.PlanHash is not null || row.ReadyExpiresAtUtc is not null) throw Invalid();
        if (row.ExecutionJobId is not null)
        {
            var op = Operation(row);
            if (row.ConfirmedAtUtc is not { } confirmed || !Timestamp(confirmed) || confirmed < row.CreatedAtUtc || confirmed >= row.ReadyExpiresAtUtc ||
                row.ExecutionExpiresAtUtc != confirmed.AddMinutes(15) || !EqualMac(row.ExecutionHash, protector.FingerprintRetirementExecution(row))) throw Invalid();
            CheckReceipt(row.ReservationJson, row.ReservationHash, op, protector, "Reserved", false);
            CheckReceipt(row.ReceiptJson, row.ReceiptHash, op, protector, row.RootState, row.RetirementApplied);
        }
        else if (row.ConfirmedAtUtc is not null || row.ExecutionExpiresAtUtc is not null || row.ExecutionHash is not null ||
            row.ReservationJson is not null || row.ReservationHash is not null || row.ReceiptJson is not null || row.ReceiptHash is not null ||
            row.RootState is not null || row.RetirementApplied is not null) throw Invalid();
    }

    private static void CheckReceipt(string? json, string? mac, ContainerRecycleArchiveRetirementOperationDto op,
        IContainerRecycleArchiveRetirementProtector protector, string? state, bool? applied)
    {
        if (json is null)
        {
            if (mac is not null || state != "Reserved" && (state is not null || applied is not null)) throw Invalid();
            return;
        }
        var receipt = Read<ContainerRecycleArchiveRetirementReceiptDto>(json);
        if (receipt.State != state || receipt.RetirementApplied != applied ||
            !EqualMac(mac, protector.FingerprintRetirementReceipt(op, receipt))) throw Invalid();
    }

    public static InvalidOperationException Invalid() => new(Prefix + "BINDING_CHANGED");
}
