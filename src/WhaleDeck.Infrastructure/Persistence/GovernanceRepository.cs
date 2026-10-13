using Microsoft.EntityFrameworkCore;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Infrastructure.Persistence;

public sealed class GovernanceRepository(PlatformDbContext db) : IGovernanceRepository
{
    public async Task<IReadOnlyCollection<ScheduledTask>> ListSchedulesAsync(CancellationToken cancellationToken) =>
        await db.ScheduledTasks.AsNoTracking().OrderBy(item => item.Name).ToArrayAsync(cancellationToken);

    public async Task<ScheduledTask> SaveScheduleAsync(string actorSubject, SaveScheduledTaskCommand command, CancellationToken cancellationToken)
    {
        ScheduledTask item;
        if (command.Id is { } id)
        {
            item = await db.ScheduledTasks.SingleOrDefaultAsync(value => value.Id == id, cancellationToken)
                ?? throw new KeyNotFoundException("Scheduled task was not found.");
            RequireVersion(command.ExpectedVersion, item.Version);
            item.TaskType = command.TaskType;
            item.Name = command.Name.Trim();
            item.ScheduleKind = command.ScheduleKind;
            item.ScheduleExpression = command.ScheduleExpression.Trim();
            item.Timezone = command.Timezone;
            item.ParametersJson = command.ParametersJson;
            item.ConcurrencyPolicy = command.ConcurrencyPolicy;
            item.TimeoutSeconds = command.TimeoutSeconds;
            item.IsEnabled = command.IsEnabled;
            item.UpdatedBySubject = actorSubject;
            item.UpdatedAtUtc = DateTimeOffset.UtcNow;
            item.NextRunAtUtc = item.IsEnabled ? ScheduleCalculator.NextUtc(item.ScheduleKind, item.ScheduleExpression, item.Timezone, DateTimeOffset.UtcNow) : null;
            item.Version++;
        }
        else
        {
            item = new ScheduledTask
            {
                TaskType = command.TaskType,
                Name = command.Name.Trim(),
                ScheduleKind = command.ScheduleKind,
                ScheduleExpression = command.ScheduleExpression.Trim(),
                Timezone = command.Timezone,
                ParametersJson = command.ParametersJson,
                ConcurrencyPolicy = command.ConcurrencyPolicy,
                TimeoutSeconds = command.TimeoutSeconds,
                IsEnabled = command.IsEnabled,
                NextRunAtUtc = command.IsEnabled ? ScheduleCalculator.NextUtc(command.ScheduleKind, command.ScheduleExpression, command.Timezone, DateTimeOffset.UtcNow) : null,
                CreatedBySubject = actorSubject,
                UpdatedBySubject = actorSubject
            };
            db.ScheduledTasks.Add(item);
        }
        await db.SaveChangesAsync(cancellationToken);
        return item;
    }

    public async Task<ScheduledTask> TriggerScheduleAsync(Guid id, string actorSubject, CancellationToken cancellationToken)
    {
        var item = await db.ScheduledTasks.SingleOrDefaultAsync(value => value.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Scheduled task was not found.");
        if (!item.IsEnabled) throw new InvalidOperationException("A disabled scheduled task cannot run.");
        item.NextRunAtUtc = DateTimeOffset.UtcNow;
        item.UpdatedBySubject = actorSubject;
        item.UpdatedAtUtc = DateTimeOffset.UtcNow;
        item.Version++;
        db.AuditEvents.Add(new AuditEvent
        {
            ActorSubject = actorSubject,
            Action = "schedule.run-now",
            TargetType = "schedule",
            TargetId = item.Id.ToString("D"),
            Result = "Accepted",
            TraceId = item.Id.ToString("N")
        });
        await db.SaveChangesAsync(cancellationToken);
        return item;
    }

    public async Task<IReadOnlyCollection<ScheduledTaskRun>> ListScheduleRunsAsync(
        Guid? scheduleId,
        int take,
        CancellationToken cancellationToken)
    {
        var query = db.ScheduledTaskRuns.AsNoTracking();
        if (scheduleId is { } id) query = query.Where(item => item.ScheduleId == id);
        return await query.OrderByDescending(item => item.ScheduledForUtc).Take(take).ToArrayAsync(cancellationToken);
    }

    public async Task DeleteScheduleAsync(Guid id, long expectedVersion, CancellationToken cancellationToken)
    {
        var item = await db.ScheduledTasks.SingleOrDefaultAsync(value => value.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Scheduled task was not found.");
        RequireVersion(expectedVersion, item.Version);
        db.ScheduledTasks.Remove(item);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<BackupPolicy>> ListBackupPoliciesAsync(CancellationToken cancellationToken) =>
        await db.BackupPolicies.AsNoTracking().OrderBy(item => item.InstanceResourceId).ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyCollection<BackupRecord>> ListBackupRecordsAsync(
        string? instanceResourceId,
        int take,
        CancellationToken cancellationToken)
    {
        var query = db.BackupRecords.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(instanceResourceId))
        {
            var resourceId = await db.ManagedResources
                .Where(item => (item.ResourceType == "Database" || item.ResourceType == "Container" || item.ResourceType == "ComposeApplication") &&
                               item.ExternalId == instanceResourceId)
                .OrderByDescending(item => item.ResourceType == "Database")
                .Select(item => (Guid?)item.Id)
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new KeyNotFoundException("Managed database resource was not found.");
            query = query.Where(item => item.InstanceResourceId == resourceId);
        }
        return await query.OrderByDescending(item => item.StartedAtUtc).Take(Math.Clamp(take, 1, 200)).ToArrayAsync(cancellationToken);
    }

    public async Task<string> ResolveResourceExternalIdAsync(Guid id, CancellationToken cancellationToken) =>
        await db.ManagedResources.Where(item => item.Id == id).Select(item => item.ExternalId).SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("Managed database resource was not found.");

    public async Task<BackupPolicy> SaveBackupPolicyAsync(SaveBackupPolicyCommand command, CancellationToken cancellationToken)
    {
        var resource = await db.ManagedResources
            .Where(value => (value.ResourceType == "Database" || value.ResourceType == "Container" || value.ResourceType == "ComposeApplication") &&
                value.ExternalId == command.InstanceResourceId)
            .OrderByDescending(value => value.ResourceType == "Database").FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("Managed backup resource was not found.");
        if (resource.ResourceType == "ComposeApplication" && (resource.ProtectionLevel != "Managed" ||
            !resource.ExternalId.StartsWith("application:", StringComparison.Ordinal)))
            throw new InvalidOperationException("Only registered unprotected applications accept backup policies.");
        BackupPolicy item;
        if (command.Id is { } id)
        {
            item = await db.BackupPolicies.SingleOrDefaultAsync(value => value.Id == id, cancellationToken)
                ?? throw new KeyNotFoundException("Backup policy was not found.");
            RequireVersion(command.ExpectedVersion, item.Version);
            if (item.InstanceResourceId != resource.Id) throw new InvalidOperationException("Backup policy resource cannot be changed.");
            item.IsEnabled = command.IsEnabled;
            item.ScheduleExpression = command.ScheduleExpression;
            item.Timezone = command.Timezone;
            item.RetentionCount = command.RetentionCount;
            item.RetentionDays = command.RetentionDays;
            item.TargetDirectoryId = command.TargetDirectoryId;
            item.Compression = command.Compression;
            item.VerifyAfterBackup = command.VerifyAfterBackup;
            item.Consistency = command.Consistency;
            item.CapacityWarningPercent = command.CapacityWarningPercent;
            item.CapacityCriticalPercent = command.CapacityCriticalPercent;
            item.UpdatedAtUtc = DateTimeOffset.UtcNow;
            item.Version++;
        }
        else
        {
            item = new BackupPolicy
            {
                InstanceResourceId = resource.Id,
                IsEnabled = command.IsEnabled,
                ScheduleExpression = command.ScheduleExpression,
                Timezone = command.Timezone,
                RetentionCount = command.RetentionCount,
                RetentionDays = command.RetentionDays,
                TargetDirectoryId = command.TargetDirectoryId,
                Compression = command.Compression,
                VerifyAfterBackup = command.VerifyAfterBackup,
                Consistency = command.Consistency,
                CapacityWarningPercent = command.CapacityWarningPercent,
                CapacityCriticalPercent = command.CapacityCriticalPercent
            };
            db.BackupPolicies.Add(item);
        }
        await db.SaveChangesAsync(cancellationToken);
        return item;
    }

    public async Task<IReadOnlyCollection<AlertRule>> ListAlertRulesAsync(CancellationToken cancellationToken) =>
        await db.AlertRules.AsNoTracking().OrderBy(item => item.RuleType).ToArrayAsync(cancellationToken);

    public async Task<AlertRule> SaveAlertRuleAsync(SaveAlertRuleCommand command, CancellationToken cancellationToken)
    {
        AlertRule item;
        if (command.Id is { } id)
        {
            item = await db.AlertRules.SingleOrDefaultAsync(value => value.Id == id, cancellationToken)
                ?? throw new KeyNotFoundException("Alert rule was not found.");
            if (item.RuleType == BackupCapacityPolicy.RuleType) throw new InvalidOperationException("Backup capacity rules are managed by their backup policy.");
            RequireVersion(command.ExpectedVersion, item.Version);
            item.RuleType = command.RuleType;
            item.ResourceSelectorJson = command.ResourceSelectorJson;
            item.ThresholdJson = command.ThresholdJson;
            item.EvaluationWindowSeconds = command.EvaluationWindowSeconds;
            item.Severity = command.Severity;
            item.IsEnabled = command.IsEnabled;
            item.UpdatedAtUtc = DateTimeOffset.UtcNow;
            item.Version++;
        }
        else
        {
            item = new AlertRule
            {
                RuleType = command.RuleType,
                ResourceSelectorJson = command.ResourceSelectorJson,
                ThresholdJson = command.ThresholdJson,
                EvaluationWindowSeconds = command.EvaluationWindowSeconds,
                Severity = command.Severity,
                IsEnabled = command.IsEnabled
            };
            db.AlertRules.Add(item);
        }
        await db.SaveChangesAsync(cancellationToken);
        return item;
    }

    public async Task<IReadOnlyCollection<AlertEvent>> ListAlertsAsync(bool includeRecovered, CancellationToken cancellationToken) =>
        await db.AlertEvents.AsNoTracking().Where(item => includeRecovered || item.State != "Recovered")
            .OrderByDescending(item => item.LastOccurredAtUtc).Take(500).ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyCollection<AlertEventHistory>> ListAlertHistoryAsync(
        Guid? alertEventId,
        int take,
        CancellationToken cancellationToken)
    {
        var query = db.AlertEventHistory.AsNoTracking();
        if (alertEventId is { } id) query = query.Where(item => item.AlertEventId == id);
        return await query.OrderByDescending(item => item.OccurredAtUtc).Take(take).ToArrayAsync(cancellationToken);
    }

    public async Task<AlertEvent> UpdateAlertAsync(Guid id, string actorSubject, DateTimeOffset? silencedUntilUtc, CancellationToken cancellationToken)
    {
        var item = await db.AlertEvents.SingleOrDefaultAsync(value => value.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Alert event was not found.");
        if (item.State == "Recovered") throw new InvalidOperationException("Recovered alerts cannot be acknowledged or silenced again.");
        item.AcknowledgedBySubject = actorSubject;
        item.AcknowledgedAtUtc = DateTimeOffset.UtcNow;
        item.SilencedUntilUtc = silencedUntilUtc;
        item.State = silencedUntilUtc is null ? "Acknowledged" : "Silenced";
        item.Version++;
        db.AlertEventHistory.Add(new AlertEventHistory { AlertEventId = item.Id, State = item.State, ActorSubject = actorSubject });
        await db.SaveChangesAsync(cancellationToken);
        return item;
    }

    public async Task<IReadOnlyCollection<PlatformSetting>> ListSettingsAsync(CancellationToken cancellationToken) =>
        await db.PlatformSettings.AsNoTracking().OrderBy(item => item.Key).ToArrayAsync(cancellationToken);

    public async Task<PlatformSetting> SaveSettingAsync(string key, string actorSubject, SavePlatformSettingCommand command, CancellationToken cancellationToken)
    {
        var item = await db.PlatformSettings.SingleOrDefaultAsync(value => value.Key == key, cancellationToken);
        if (item is null)
        {
            if (command.ExpectedVersion is not null) throw new InvalidOperationException("Setting version does not match.");
            item = new PlatformSetting { Key = key, ValueJson = command.ValueJson, UpdatedBySubject = actorSubject };
            db.PlatformSettings.Add(item);
        }
        else
        {
            RequireVersion(command.ExpectedVersion, item.Version);
            item.ValueJson = command.ValueJson;
            item.UpdatedBySubject = actorSubject;
            item.UpdatedAtUtc = DateTimeOffset.UtcNow;
            item.Version++;
        }
        await db.SaveChangesAsync(cancellationToken);
        return item;
    }

    public async Task<IReadOnlyCollection<AuditEvent>> ListAuditAsync(
        int take,
        string? actorSubject,
        string? action,
        string? result,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        CancellationToken cancellationToken)
    {
        var query = db.AuditEvents.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(actorSubject)) query = query.Where(item => item.ActorSubject == actorSubject);
        if (!string.IsNullOrWhiteSpace(action)) query = query.Where(item => item.Action == action);
        if (!string.IsNullOrWhiteSpace(result)) query = query.Where(item => item.Result == result);
        if (fromUtc is { } from) query = query.Where(item => item.OccurredAtUtc >= from);
        if (toUtc is { } to) query = query.Where(item => item.OccurredAtUtc <= to);
        return await query.OrderByDescending(item => item.OccurredAtUtc).Take(take).ToArrayAsync(cancellationToken);
    }

    private static void RequireVersion(long? expected, long actual)
    {
        if (expected != actual) throw new InvalidOperationException("Resource was modified by another request.");
    }
}
