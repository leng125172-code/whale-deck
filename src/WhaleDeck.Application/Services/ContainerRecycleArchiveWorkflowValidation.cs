using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

public static class ContainerRecycleArchiveWorkflowValidation
{
    public static readonly JsonSerializerOptions Json = new() { MaxDepth = 16, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    public static DateTimeOffset Time(DateTimeOffset value) => value.ToUniversalTime().AddTicks(-(value.Ticks % 10));
    private static void Timestamp(DateTimeOffset value)
    { if (value <= DateTimeOffset.UnixEpoch || value.Offset != TimeSpan.Zero || value.Ticks % 10 != 0) throw Invalid(); }
    private static void Actor(string actor, string login)
    { if (string.IsNullOrWhiteSpace(actor) || actor.Length > 256 || actor.Any(char.IsControl) || !FileManagementService.Hex(login)) throw Invalid(); }
    public static Guid Key(string key)
    {
        if (!Guid.TryParseExact(key, "D", out var id) || id == Guid.Empty || id.ToString("D") != key) throw new ArgumentException("CONTAINER_RECYCLE_ARCHIVE_IDEMPOTENCY_KEY_REQUIRED");
        return id;
    }
    public static Guid IntentId(string actor, string login, Guid key)
    {
        Actor(actor, login); if (key == Guid.Empty) throw Invalid();
        return new(SHA256.HashData(Encoding.UTF8.GetBytes("whaledeck-container-recycle-archive-request-id-v1:" + BffSessionValidation.SubjectHash(actor) + ":" + login + ":" + key.ToString("N"))).AsSpan(0, 16));
    }
    public static Guid ExecutionId(Guid id)
    { if (id == Guid.Empty) throw Invalid(); return new(SHA256.HashData(Encoding.UTF8.GetBytes("whaledeck-container-recycle-archive-execution-id-v1:" + id.ToString("N"))).AsSpan(0, 16)); }
    public static string Owner(string actor, string login)
    { Actor(actor, login); return FileManagementService.Hash("whaledeck-container-recycle-archive-owner-v1:" + BffSessionValidation.SubjectHash(actor) + ":" + login); }
    public static void Request(ContainerRecycleArchiveRequestBinding value)
    {
        if (value is null) throw Invalid(); Actor(value.Actor, value.LoginBinding); Timestamp(value.CreatedAtUtc); Timestamp(value.PlanningExpiresAtUtc);
        ContainerRecycleArchiveValidation.Command(value.Command);
        if (value.Id != IntentId(value.Actor, value.LoginBinding, value.IdempotencyKey) || value.Owner != Owner(value.Actor, value.LoginBinding) ||
            string.IsNullOrWhiteSpace(value.TraceId) || value.TraceId.Length > 128 || value.TraceId.Any(char.IsControl) ||
            value.SourceIntentId == Guid.Empty || !FileManagementService.Hex(value.SourceProofHash) ||
            value.Id == value.SourceIntentId || ExecutionId(value.Id) == value.Command.Purge.Plan.Command.Source.OperationId ||
            ExecutionId(value.Id) == value.Command.Purge.OperationId || value.Id == value.Command.Purge.OperationId || value.Id == value.Command.Purge.Plan.Command.Source.OperationId ||
            value.PlanningExpiresAtUtc != value.CreatedAtUtc.AddMinutes(15)) throw Invalid();
        ContainerRecycleArchiveValidation.Command(value.Command);
    }
    public static void Plan(ContainerRecycleArchivePlanBinding value)
    {
        if (value is null) throw Invalid(); Request(value.Request); ContainerRecycleArchiveValidation.Plan(value.Plan); Timestamp(value.ReadyExpiresAtUtc);
        if (!SameCommand(value.Plan.Command, value.Request.Command) || value.ReadyExpiresAtUtc <= value.Request.CreatedAtUtc ||
            value.ReadyExpiresAtUtc > value.Request.PlanningExpiresAtUtc.AddMinutes(5)) throw Invalid();
    }
    public static void Execution(ContainerRecycleArchiveExecutionBinding value)
    {
        if (value is null) throw Invalid(); Actor(value.Actor, value.LoginBinding); Timestamp(value.ConfirmedAtUtc); Timestamp(value.ExecutionExpiresAtUtc);
        if (value.Id == Guid.Empty || value.ExecutionJobId != ExecutionId(value.Id) || !FileManagementService.Hex(value.PlanHash) ||
            value.ExecutionExpiresAtUtc != value.ConfirmedAtUtc.AddMinutes(15)) throw Invalid();
    }
    public static void Confirmation(ContainerRecycleArchiveConfirmation value)
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
    public static bool SameCommand(ContainerRecycleArchiveCommandDto left, ContainerRecycleArchiveCommandDto right) => Serialize(left) == Serialize(right);
    public static ContainerRecycleArchiveCommandDto SnapshotCommand(ContainerRecycleArchiveCommandDto command)
    { ContainerRecycleArchiveValidation.Command(command); var value = Read<ContainerRecycleArchiveCommandDto>(Serialize(command)); ContainerRecycleArchiveValidation.Command(value); return value; }
    public static ContainerRecycleArchivePlanDto SnapshotPlan(ContainerRecycleArchivePlanDto plan)
    { ContainerRecycleArchiveValidation.Plan(plan); var value = Read<ContainerRecycleArchivePlanDto>(Serialize(plan)); ContainerRecycleArchiveValidation.Plan(value); return value; }
    public static ContainerRecycleArchiveCommandDto ReadCommand(ContainerRecycleArchiveIntent row)
    {
        var value = Read<ContainerRecycleArchiveCommandDto>(row.CommandJson); ContainerRecycleArchiveValidation.Command(value);
        if (value.Purge.Plan.Command.Source.Plan.Command.Location != new ContainerFileLocationDto(row.ContainerId, row.InstanceId, row.RelativePath)) throw Invalid();
        return value;
    }
    public static ContainerRecycleArchivePlanDto ReadPlan(ContainerRecycleArchiveIntent row)
    {
        var value = Read<ContainerRecycleArchivePlanDto>(row.PlanJson); ContainerRecycleArchiveValidation.Plan(value);
        if (!SameCommand(value.Command, ReadCommand(row))) throw Invalid(); return value;
    }
    public static ContainerRecycleArchiveRequestBinding RequestBinding(ContainerRecycleArchiveIntent row) => new(row.Id, row.IdempotencyKey, row.ActorSubject, row.LoginBinding,
        row.TraceId, row.OwnerHash, row.SourceIntentId, row.SourceProofHash, ReadCommand(row), row.CreatedAtUtc, row.PlanningExpiresAtUtc);
    public static ContainerRecycleArchivePlanBinding PlanBinding(ContainerRecycleArchiveIntent row) => new(RequestBinding(row), ReadPlan(row), row.ReadyExpiresAtUtc ?? throw Invalid());
    public static ContainerRecycleArchiveExecutionBinding ExecutionBinding(ContainerRecycleArchiveIntent row) => new(row.Id, row.ActorSubject, row.LoginBinding,
        row.PlanHash ?? throw Invalid(), row.ExecutionJobId ?? throw Invalid(), row.ConfirmedAtUtc ?? throw Invalid(), row.ExecutionExpiresAtUtc ?? throw Invalid());
    public static ContainerRecycleArchiveOperationDto Reference(ContainerRecycleArchiveIntent row)
    {
        var value = new ContainerRecycleArchiveOperationDto(row.ExecutionJobId ?? throw Invalid(), row.OwnerHash, ReadPlan(row),
            new(row.ActorSubject, row.TraceId, row.ExecutionJobId.Value, row.IdempotencyKey));
        ContainerRecycleArchiveValidation.Operation(value); return value;
    }
    public static ContainerRecycleArchiveReceiptDto? ReadReservation(ContainerRecycleArchiveIntent row)
    {
        if(row.ReservationJson is null)return null;
        var value=Read<ContainerRecycleArchiveReceiptDto>(row.ReservationJson);ContainerRecycleArchiveValidation.Receipt(Reference(row),value);
        if(value.State!="Reserved"||row.RootStartedAtUtc is null)throw Invalid();return value;
    }
    public static ContainerRecycleArchiveReceiptDto? ReadReceipt(ContainerRecycleArchiveIntent row)
    {
        if(row.ReceiptJson is null)return null;
        var value=Read<ContainerRecycleArchiveReceiptDto>(row.ReceiptJson);ContainerRecycleArchiveValidation.Receipt(Reference(row),value);
        if(value.State=="Reserved"||row.RootState!=value.State||row.ArchiveCopied!=value.ArchiveCopied||row.SourceFenced!=value.SourceFenced||row.OwnedFence!=value.OwnedFence||row.BindingCurrent!=value.BindingCurrent)throw Invalid();return value;
    }
    public static void RequireCompatibleReceipt(ContainerRecycleArchiveIntent row,ContainerRecycleArchiveReceiptDto value)
    {
        ContainerRecycleArchiveValidation.Receipt(Reference(row),value);
        if(value.State=="Reserved")throw new InvalidOperationException("CONTAINER_RECYCLE_ARCHIVE_RESULT_UNRESOLVED");
        var prior=ReadReceipt(row);
        // Proven outcome is immutable. A fence may later belong to another
        // operation; keep the historical copy and its published inode unchanged.
        if(prior is not null&&(prior.State!=value.State||prior.ArchiveCopied!=value.ArchiveCopied||prior.PublishedIdentity!=value.PublishedIdentity||prior.PublishedChangedAtUnixNs!=value.PublishedChangedAtUnixNs))throw new InvalidOperationException("CONTAINER_RECYCLE_ARCHIVE_FACT_CHANGED");
    }
    private static bool ValidFacts(ContainerRecycleArchiveIntent row)
    {
        if(row.State is not ("Queued" or "Planning" or "Ready" or "Pending" or "Reserving" or "Applying" or "Succeeded" or "Canceled" or "Failed" or "Expired" or "Unknown" or "Acknowledged"))return false;
        if(row.State=="Unknown"&&(!row.PublishBarrier||row.CompletedAtUtc is not null))return false;
        if(row.RootStartedAtUtc is not null&&row.State is "Pending" or "Reserving" or "Applying"&&!row.PublishBarrier)return false;
        if(row.State is "Succeeded" or "Canceled" or "Failed" or "Expired" or "Acknowledged"&&(row.PublishBarrier||row.CompletedAtUtc is null))return false;
        if(row.ReceiptJson is not null)
        {
            var receipt=ReadReceipt(row)!;
            return row.State switch {
                "Succeeded"=>receipt.State=="Completed"&&row.ApplyStartedAtUtc is not null&&row.ReservationJson is not null,
                "Unknown"=>receipt.State=="Unknown",
                "Canceled" or "Failed"=>receipt.State=="Aborted",
                "Acknowledged"=>row.AcknowledgedBy is {Length:>0}&&row.AcknowledgementJson is not null,
                _=>false };
        }
        if(row.State is "Succeeded" or "Acknowledged"||row.ExecutionJobId is not null&&row.State is "Canceled" or "Failed")return false;
        return row.RootState is null&&row.ArchiveCopied is null&&row.SourceFenced is null&&row.OwnedFence is null&&row.BindingCurrent is null;
    }
    public static bool HasValidBinding(ContainerRecycleArchiveIntent row, IContainerRecycleArchiveProtector protector)
    {
        try
        {
            var request = RequestBinding(row); Request(request);
            if (!Equal(row.RequestHash, protector.FingerprintRecycleArchiveRequest(request)) || !ValidFacts(row)) return false;
            if (row.ReceiptJson is null ? row.ReceiptHash is not null :
                !Equal(row.ReceiptHash, protector.FingerprintRecycleArchiveReceipt(new(Reference(row), ReadReceipt(row)!)))) return false;
            if(row.ReservationJson is null ? row.ReservationHash is not null :
                !Equal(row.ReservationHash,protector.FingerprintRecycleArchiveReceipt(new(Reference(row),ReadReservation(row)!))))return false;
            if(row.ApplyStartedAtUtc is {} applying)
            {
                Timestamp(applying);
                if(row.ReservationJson is null||row.RootStartedAtUtc is null||applying<row.RootStartedAtUtc||applying>=row.ExecutionExpiresAtUtc)return false;
            }
            if(row.RootStartedAtUtc is {} started)
            {
                Timestamp(started);
                if(row.ExecutionJobId is null||started<row.ConfirmedAtUtc||started>=row.ExecutionExpiresAtUtc)return false;
            }
            if (row.PlanJson is null) return row.PlanHash is null && row.ReadyExpiresAtUtc is null && NoExecution(row) && row.RootStartedAtUtc is null && !row.PublishBarrier;
            var plan = PlanBinding(row); Plan(plan);
            if (!Equal(row.PlanHash, protector.FingerprintRecycleArchivePlan(plan))) return false;
            if (NoExecution(row)) return row.RootStartedAtUtc is null && !row.PublishBarrier;
            var execution = ExecutionBinding(row); Execution(execution);
            return execution.ConfirmedAtUtc >= row.CreatedAtUtc && execution.ConfirmedAtUtc < row.ReadyExpiresAtUtc &&
                Equal(row.ExecutionHash, protector.FingerprintRecycleArchiveExecution(execution));
        }
        catch (Exception error) when (error is ArgumentException or JsonException or InvalidOperationException or NullReferenceException) { return false; }
    }
    private static bool NoExecution(ContainerRecycleArchiveIntent row) => row.ExecutionJobId is null && row.ExecutionExpiresAtUtc is null && row.ConfirmedAtUtc is null && row.ExecutionHash is null && row.ReservationJson is null && row.ReservationHash is null && row.ApplyStartedAtUtc is null;
    private static bool Equal(string? left, string right) => FileManagementService.Hex(left) && FileManagementService.Hex(right) &&
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(left!), Encoding.ASCII.GetBytes(right));
    public static ArgumentException Invalid() => new("CONTAINER_RECYCLE_ARCHIVE_INTENT_INVALID");
}
