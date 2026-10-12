using System.Text.Json;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

/// <summary>Policy-specific storage alarms. An unknown sample never clears an existing risk.</summary>
public static class BackupCapacityPolicy
{
    public const string RuleType = "BackupStorageCapacity";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static bool Triggered(BackupPolicy policy, BackupTargetCapacityDto capacity, DateTimeOffset now) =>
        policy.IsEnabled && (!capacity.IsCurrent(now) || capacity.UsedPercent >= policy.CapacityWarningPercent || capacity.FreeBytes < 256 * 1024 * 1024);

    public static string Severity(BackupPolicy policy, BackupTargetCapacityDto capacity, DateTimeOffset now) =>
        capacity.IsCurrent(now) && (capacity.UsedPercent >= policy.CapacityCriticalPercent || capacity.FreeBytes < 256 * 1024 * 1024) ? "Critical" : "Warning";

    public static string Detail(BackupPolicy policy, BackupTargetCapacityDto capacity, DateTimeOffset now) => JsonSerializer.Serialize(
        new BackupCapacityAlertDetail(policy.Id, policy.TargetDirectoryId, capacity.IsCurrent(now),
            capacity.IsCurrent(now) ? capacity.UsedPercent : null, capacity.IsCurrent(now) ? capacity.FreeBytes : null,
            policy.CapacityWarningPercent, policy.CapacityCriticalPercent, capacity.ObservedAtUtc), JsonOptions);

    public static string? Apply(AlertEvent item, BackupPolicy policy, BackupTargetCapacityDto capacity, DateTimeOffset now)
    {
        item.DetailJson = Detail(policy, capacity, now);
        if (!Triggered(policy, capacity, now))
        {
            item.State = "Recovered";
            item.RecoveredAtUtc = now;
            item.LastOccurredAtUtc = now;
            item.SilencedUntilUtc = null;
            return "Recovered";
        }
        string? history = null;
        // Missing observations do not downgrade an existing critical risk.
        if (capacity.IsCurrent(now) && item.Severity != Severity(policy, capacity, now))
        {
            var escalated = item.Severity != "Critical" && Severity(policy, capacity, now) == "Critical";
            item.Severity = Severity(policy, capacity, now);
            history = "SeverityChanged";
            if (escalated && item.State == "Acknowledged")
            {
                item.State = "Active";
                item.AcknowledgedAtUtc = null;
                item.AcknowledgedBySubject = null;
            }
        }
        if (item.State == "Silenced" && item.SilencedUntilUtc <= now)
        {
            item.State = "Active";
            item.SilencedUntilUtc = null;
            item.AcknowledgedAtUtc = null;
            item.AcknowledgedBySubject = null;
            history = "SilenceExpired";
        }
        if (item.LastOccurredAtUtc <= now.AddSeconds(-60))
        {
            item.LastOccurredAtUtc = now;
            item.OccurrenceCount++;
        }
        return history;
    }

    public static BackupCapacityAlertDetail? ReadDetail(AlertEvent item)
    {
        if (item.SummaryCode != "ALERT_BACKUPSTORAGECAPACITY" || item.DetailJson.Length > 2048) return null;
        try { return JsonSerializer.Deserialize<BackupCapacityAlertDetail>(item.DetailJson, JsonOptions); }
        catch (JsonException) { return null; }
    }
}
