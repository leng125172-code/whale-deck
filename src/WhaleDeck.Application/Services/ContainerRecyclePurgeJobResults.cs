using System.Text.Json;
using System.Text.Json.Serialization;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

/// <summary>Bounded historical facts only. No command, path, identity, capability, plan or signed receipt.</summary>
public static class ContainerRecyclePurgeJobResults
{
    private const string Prefix = "CONTAINER_RECYCLE_PURGE_";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, PropertyNameCaseInsensitive = false, MaxDepth = 4 };
    public sealed record Summary(int SchemaVersion, Guid IntentId, Guid JobId, Guid SourceIntentId, Guid SourceOperationId,
        string Stage, string State, string Code, string? ReceiptState, bool ReservationStarted, bool ReservationRecorded,
        bool ApplyStarted, bool? PurgeApplied, bool? SourceFenced, bool? OwnedFence, bool PublishBarrier);

    public static string Snapshot(ContainerRecyclePurgeIntent row, bool execution)
    {
        var code = row.State == "Reserving" ? Prefix + (row.ReservationJson is null ? "RESERVING" : "RESERVED")
            : row.State == "Applying" ? Prefix + "APPLYING" : row.ResultCode!;
        return JsonSerializer.Serialize(new Summary(1, row.Id, execution ? row.ExecutionJobId!.Value : row.Id,
            row.SourceIntentId, ContainerRecyclePurgeWorkflowValidation.ReadCommand(row).Source.OperationId,
            execution ? "execution" : "planning", row.State, code, row.RootState, row.RootStartedAtUtc is not null,
            row.ReservationJson is not null, row.ApplyStartedAtUtc is not null, row.PurgeApplied, row.SourceFenced, row.OwnedFence, row.PublishBarrier), Json);
    }
    public static string? ForJob(OperationJob job)
    {
        if (job.JobType is not ("containers.files.recycle-purge" or "containers.files.recycle-purge-plan") || job.ResultJson is not { Length: > 0 and <= 4096 }) return null;
        try
        {
            using var doc = JsonDocument.Parse(job.ResultJson, new() { MaxDepth = 4 });
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            var names = doc.RootElement.EnumerateObject().Select(p => p.Name).ToArray();
            if (names.Length != 16 || names.Distinct(StringComparer.Ordinal).Count() != 16) return null;
            var value = JsonSerializer.Deserialize<Summary>(job.ResultJson, Json);
            return value is not null && Valid(value, job) ? JsonSerializer.Serialize(value, Json) : null;
        }
        catch (JsonException) { return null; }
    }
    private static bool Valid(Summary s, OperationJob job)
    {
        var execution = job.JobType == "containers.files.recycle-purge";
        if (s.SchemaVersion != 1 || s.IntentId == Guid.Empty || s.SourceIntentId == Guid.Empty || s.IntentId == s.SourceIntentId ||
            s.SourceOperationId != ContainerRecycleWorkflowValidation.ExecutionId(s.SourceIntentId) || s.JobId != job.Id ||
            s.JobId != (execution ? ContainerRecyclePurgeWorkflowValidation.ExecutionId(s.IntentId) : s.IntentId) ||
            execution && s.JobId == s.SourceOperationId || s.Stage != (execution ? "execution" : "planning") ||
            job.State != (s.State is "Ready" or "Succeeded" ? "Succeeded" : s.State is "Unknown" or "Reserving" or "Applying" ? "Running" : s.State == "Canceled" ? "Canceled" : "Failed") ||
            s.Code is null || s.Code.Length <= Prefix.Length || s.Code.Length > 64 || !s.Code.StartsWith(Prefix, StringComparison.Ordinal) ||
            s.Code.Any(c => !(char.IsAsciiLetterUpper(c) || c == '_')) || !CodeMatches(s, execution) ||
            s.ReservationRecorded && !s.ReservationStarted || s.ApplyStarted && !s.ReservationRecorded ||
            s.OwnedFence == true && s.SourceFenced != true) return false;
        if (!execution) return s.State is "Ready" or "Expired" or "Canceled" or "Failed" &&
            !s.ReservationStarted && !s.ReservationRecorded && !s.ApplyStarted && !s.PublishBarrier &&
            s.ReceiptState is null && s.PurgeApplied is null && s.SourceFenced is null && s.OwnedFence is null;
        if (s.State is not ("Reserving" or "Applying" or "Succeeded" or "Canceled" or "Failed" or "Unknown" or "Acknowledged")) return false;
        if (s.PublishBarrier != (s.State is "Reserving" or "Applying" or "Unknown")) return false;
        if (s.State is "Reserving" or "Applying") return s.ReservationStarted && s.ReceiptState is null &&
            s.PurgeApplied is null && s.SourceFenced is null && s.OwnedFence is null &&
            (s.State == "Applying" ? s.ApplyStarted : !s.ApplyStarted);
        if (s.ReceiptState is null) return s.State == "Unknown" && s.PurgeApplied is null && s.SourceFenced is null && s.OwnedFence is null;
        var facts = s.ReceiptState switch
        {
            "Completed" => s.PurgeApplied == true && s.SourceFenced == true && s.OwnedFence == true,
            "Aborted" => s.PurgeApplied == false && s.SourceFenced is not null && s.OwnedFence == false,
            "Unknown" => s.PurgeApplied is null && s.SourceFenced == true && s.OwnedFence is not null,
            _ => false
        };
        return facts && (s.State switch
        {
            "Succeeded" => s.ReceiptState == "Completed" && s.ApplyStarted,
            "Canceled" or "Failed" => s.ReceiptState == "Aborted",
            "Unknown" => s.ReceiptState == "Unknown",
            "Acknowledged" => true,
            _ => false
        });
    }
    private static bool CodeMatches(Summary s, bool execution)
    {
        var suffix = s.Code[Prefix.Length..];
        return s.State switch
        {
            "Ready" => suffix == "READY", "Expired" => suffix == "PLAN_EXPIRED", "Succeeded" => suffix == "COMPLETED",
            "Canceled" => suffix == "CANCELED", "Reserving" => suffix == (s.ReservationRecorded ? "RESERVED" : "RESERVING"),
            "Applying" => suffix == "APPLYING", "Unknown" => suffix is "RESULT_UNKNOWN" or "OUTPUT_INVALID" or "BINDING_CHANGED" or "EXECUTION_MARKER_MISSING",
            "Acknowledged" => suffix == "UNKNOWN_ACKNOWLEDGED",
            "Failed" => execution ? suffix == "INTERRUPTED" : suffix is not ("READY" or "PLAN_EXPIRED" or "COMPLETED" or "CANCELED" or "UNKNOWN_ACKNOWLEDGED" or "RESERVING" or "RESERVED" or "APPLYING" or "ACCEPTED" or "PLANNING" or "PLAN_ACCEPTED"),
            _ => false
        };
    }
}
