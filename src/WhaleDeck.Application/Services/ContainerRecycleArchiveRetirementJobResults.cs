using System.Text.Json;
using System.Text.Json.Serialization;
using WhaleDeck.Domain.Entities;
using Retirement = WhaleDeck.Application.Services.ContainerRecycleArchiveRetirementWorkflowValidation;

namespace WhaleDeck.Application.Services;

/// <summary>Project bounded historical facts with no plan, path, credentials or native payload.</summary>
public static class ContainerRecycleArchiveRetirementJobResults
{
    public const string PlanningJob = "containers.files.recycle-archive-retirement-plan";
    public const string ExecutionJob = "containers.files.recycle-archive-retirement";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, PropertyNameCaseInsensitive = false, MaxDepth = 4 };

    public sealed record Summary(int SchemaVersion, Guid IntentId, Guid JobId, Guid ArchiveIntentId, string ContainerId, int RetainDays,
        string State, string Code, long? NotBeforeUnixMs, bool ReservationStarted, bool ReservationRecorded, bool ApplyStarted,
        bool PublishBarrier, string? ReceiptState, bool? RetirementApplied);

    public static string? Snapshot(ContainerRecycleArchiveRetirementIntent row, string containerId, OperationJob job)
    {
        var plan = row.PlanJson is null ? null : Retirement.ReadPlan(row);
        var summary = new Summary(1, row.Id, job.Id, row.ArchiveIntentId, containerId, row.RetainDays,
            row.State, Retirement.Prefix + Suffix(row.State, row.ReservationJson is not null, job.JobType == ExecutionJob), plan?.NotBeforeUnixMs,
            row.RootStartedAtUtc is not null, row.ReservationJson is not null, row.ApplyStartedAtUtc is not null, row.PublishBarrier, row.RootState, row.RetirementApplied);
        return Valid(summary, job) ? JsonSerializer.Serialize(summary, Json) : null;
    }

    public static bool IsRetirement(string jobType) => jobType is PlanningJob or ExecutionJob;

    public static string? ForJob(OperationJob job)
    {
        if (!IsRetirement(job.JobType) || job.ResultJson is not { Length: > 0 and <= 2048 }) return null;
        try
        {
            using var doc = JsonDocument.Parse(job.ResultJson, new() { MaxDepth = 4 });
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            var keys = doc.RootElement.EnumerateObject().Select(p => p.Name).ToArray();
            if (keys.Length != 15 || keys.Distinct(StringComparer.Ordinal).Count() != 15) return null;
            var value = JsonSerializer.Deserialize<Summary>(job.ResultJson, Json);
            return value is not null && Valid(value, job) ? JsonSerializer.Serialize(value, Json) : null;
        }
        catch (JsonException) { return null; }
    }

    private static bool Valid(Summary s, OperationJob job)
    {
        var execution = job.JobType == ExecutionJob;
        if (!IsRetirement(job.JobType) || s.SchemaVersion != 1 || s.IntentId == Guid.Empty || s.ArchiveIntentId == Guid.Empty ||
            s.IntentId == s.ArchiveIntentId || s.JobId != job.Id || s.JobId == s.ArchiveIntentId ||
            s.JobId != (execution ? Retirement.ExecutionId(s.IntentId) : s.IntentId) || s.RetainDays is < 1 or > 3650 ||
            s.ContainerId is not { Length: 64 } || s.ContainerId.Any(c => !char.IsAsciiDigit(c) && c is not (>= 'a' and <= 'f')) ||
            s.NotBeforeUnixMs is < 86400000 or > 253402300799999 ||
            s.Code != Retirement.Prefix + Suffix(s.State, s.ReservationRecorded, execution) ||
            job.State != (s.State is "Queued" or "Pending" ? "Queued" : s.State is "Ready" or "Succeeded" ? "Succeeded" :
                s.State is "Planning" or "Reserving" or "Applying" or "Unknown" ? "Running" : s.State == "Canceled" ? "Canceled" : "Failed") ||
            s.ReservationRecorded && !s.ReservationStarted || s.ApplyStarted && !s.ReservationRecorded) return false;
        if (!execution)
            return s.State is "Queued" or "Planning" or "Ready" or "Canceled" or "Expired" or "Failed" &&
                !s.ReservationStarted && !s.PublishBarrier && s.ReceiptState is null && s.RetirementApplied is null &&
                (s.State != "Ready" || s.NotBeforeUnixMs is not null);

        if (s.NotBeforeUnixMs is null || s.PublishBarrier != (s.State is "Reserving" or "Applying" or "Unknown") ||
            s.PublishBarrier && !s.ReservationStarted) return false;
        var validReceipt = s.ReceiptState switch
        {
            null => s.RetirementApplied is null,
            "Aborted" => s.ReservationStarted && s.RetirementApplied == false,
            "Unknown" => s.ReservationStarted && s.RetirementApplied != true,
            "Completed" => s.ApplyStarted && s.RetirementApplied == true,
            _ => false
        };
        return validReceipt && (s.State switch
        {
            "Pending" => !s.ReservationStarted && s.ReceiptState is null,
            "Reserving" => !s.ApplyStarted && s.ReceiptState is null,
            "Applying" => s.ApplyStarted && s.ReceiptState is null,
            "Succeeded" => s.ReceiptState == "Completed",
            "Unknown" => s.ReceiptState is null or "Unknown",
            "Acknowledged" => s.ReceiptState is not null,
            "Canceled" or "Expired" or "Failed" => s.ReceiptState == (s.ReservationStarted ? "Aborted" : null),
            _ => false
        });
    }

    private static string Suffix(string state, bool reserved, bool execution) => state switch
    {
        "Queued" => "PLAN_ACCEPTED",
        "Planning" => "PLANNING",
        "Ready" => "READY",
        "Pending" => "CONFIRMED",
        "Reserving" => reserved ? "RESERVED" : "RESERVING",
        "Applying" => "APPLYING",
        "Succeeded" => "COMPLETED",
        "Canceled" => "CANCELED",
        "Expired" => execution ? "EXECUTION_EXPIRED" : "PLAN_EXPIRED",
        "Unknown" => "RESULT_UNKNOWN",
        "Acknowledged" => "UNKNOWN_ACKNOWLEDGED",
        _ => execution ? "EXECUTION_FAILED" : "PLAN_FAILED"
    };
}
