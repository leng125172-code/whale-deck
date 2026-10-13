using System.Text.Json;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Services;

public sealed class GovernanceService(IGovernanceRepository repository)
{
    private static readonly HashSet<string> ScheduleTypes = new(StringComparer.OrdinalIgnoreCase)
        { "Backup", "ApplicationUpdate", "ContainerUpdate", "SystemUpdate", "MetricsRollup", "RetentionCleanup" };
    private static readonly HashSet<string> SettingKeys = new(StringComparer.OrdinalIgnoreCase)
        { "maintenance.window", "applications.autoupdate", "metrics.retention", "catalog.refresh", "ui.branding" };
    private static readonly HashSet<string> AlertTypes = new(StringComparer.OrdinalIgnoreCase)
        { "CpuUtilization", "MemoryUtilization", "DiskUtilization", "LoadAverage", "SystemdHealth", "ContainerHealth", "BackupFailure", "AgentOffline" };

    public async Task<IReadOnlyCollection<ScheduledTaskDto>> ListSchedulesAsync(CancellationToken cancellationToken) =>
        (await repository.ListSchedulesAsync(cancellationToken)).Select(Map).ToArray();

    public async Task<ScheduledTaskDto> SaveScheduleAsync(string actorSubject, SaveScheduledTaskCommand command, CancellationToken cancellationToken)
    {
        if (!ScheduleTypes.Contains(command.TaskType)) throw new ArgumentException("Unsupported scheduled task type.");
        ValidateName(command.Name);
        ValidateSchedule(command.ScheduleKind, command.ScheduleExpression);
        _ = ScheduleCalculator.NextUtc(command.ScheduleKind, command.ScheduleExpression, command.Timezone, DateTimeOffset.UtcNow);
        ValidateJson(command.ParametersJson);
        if (command.TimeoutSeconds is < 30 or > 86400) throw new ArgumentException("Schedule timeout is out of range.");
        if (command.ConcurrencyPolicy is not ("Forbid" or "Replace" or "Allow")) throw new ArgumentException("Invalid concurrency policy.");
        return Map(await repository.SaveScheduleAsync(actorSubject, command, cancellationToken));
    }

    public Task DeleteScheduleAsync(Guid id, long expectedVersion, CancellationToken cancellationToken) =>
        repository.DeleteScheduleAsync(id, expectedVersion, cancellationToken);

    public async Task<ScheduledTaskDto> TriggerScheduleAsync(Guid id, string actorSubject, CancellationToken cancellationToken) =>
        Map(await repository.TriggerScheduleAsync(id, actorSubject, cancellationToken));

    public async Task<IReadOnlyCollection<ScheduledTaskRunDto>> ListScheduleRunsAsync(
        Guid? scheduleId,
        int take,
        CancellationToken cancellationToken) =>
        (await repository.ListScheduleRunsAsync(scheduleId, Math.Clamp(take, 1, 500), cancellationToken))
        .Select(item => new ScheduledTaskRunDto(item.Id, item.ScheduleId, item.JobId, item.ScheduledForUtc,
            item.StartedAtUtc, item.CompletedAtUtc, item.Result))
        .ToArray();

    public async Task<IReadOnlyCollection<BackupPolicyDto>> ListBackupPoliciesAsync(CancellationToken cancellationToken)
    {
        var policies = await repository.ListBackupPoliciesAsync(cancellationToken);
        var result = new List<BackupPolicyDto>(policies.Count);
        foreach (var item in policies)
        {
            result.Add(new BackupPolicyDto(item.Id,
                await repository.ResolveResourceExternalIdAsync(item.InstanceResourceId, cancellationToken), item.IsEnabled,
                item.ScheduleExpression, item.Timezone, item.RetentionCount, item.RetentionDays, item.TargetDirectoryId,
                item.Compression, item.VerifyAfterBackup, item.CapacityWarningPercent, item.CapacityCriticalPercent, item.Version, item.Consistency));
        }
        return result;
    }

    public async Task<IReadOnlyCollection<BackupRecordDto>> ListBackupRecordsAsync(
        string? instanceResourceId,
        int take,
        CancellationToken cancellationToken)
    {
        var records = await repository.ListBackupRecordsAsync(instanceResourceId, Math.Clamp(take, 1, 200), cancellationToken);
        var result = new List<BackupRecordDto>(records.Count);
        foreach (var item in records)
        {
            result.Add(new BackupRecordDto(
                item.Id,
                await repository.ResolveResourceExternalIdAsync(item.InstanceResourceId, cancellationToken),
                item.PolicyId,
                item.JobId,
                item.Status,
                item.RelativePath,
                item.SizeBytes,
                item.ChecksumAlgorithm,
                item.Checksum,
                item.StartedAtUtc,
                item.CompletedAtUtc,
                item.VerifiedAtUtc,
                item.ExpiresAtUtc,
                item.ErrorCode));
        }
        return result;
    }

    public async Task<BackupPolicyDto> SaveBackupPolicyAsync(SaveBackupPolicyCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.InstanceResourceId) || command.InstanceResourceId.Length > 160)
            throw new ArgumentException("Managed database resource id is invalid.");
        ValidateSchedule("Cron", command.ScheduleExpression);
        _ = ScheduleCalculator.NextUtc("Cron", command.ScheduleExpression, command.Timezone, DateTimeOffset.UtcNow);
        if (command.Consistency is not ("live" or "quiesced")) throw new ArgumentException("Backup consistency is unsupported.");
        if (command.InstanceResourceId.StartsWith("application:", StringComparison.Ordinal))
        {
            if (command.Compression != "gzip" || !command.VerifyAfterBackup)
                throw new ArgumentException("Application archives require gzip and mandatory SHA256 verification.");
            if (command.IsEnabled && !command.Confirmed) throw new ArgumentException("Application backup policy requires confirmation.");
        }
        if (command.RetentionCount is < 1 or > 365 || command.RetentionDays is < 1 or > 3650) throw new ArgumentException("Backup retention is out of range.");
        if (!string.Equals(command.TargetDirectoryId, "database-platform.backup.hdd", StringComparison.Ordinal)) throw new ArgumentException("Backup target is not approved.");
        if (command.CapacityWarningPercent is < 50 or > 95 || command.CapacityCriticalPercent <= command.CapacityWarningPercent || command.CapacityCriticalPercent > 99)
            throw new ArgumentException("Backup capacity thresholds are invalid.");
        var item = await repository.SaveBackupPolicyAsync(command, cancellationToken);
        return new BackupPolicyDto(item.Id, await repository.ResolveResourceExternalIdAsync(item.InstanceResourceId, cancellationToken), item.IsEnabled, item.ScheduleExpression, item.Timezone,
            item.RetentionCount, item.RetentionDays, item.TargetDirectoryId, item.Compression, item.VerifyAfterBackup,
            item.CapacityWarningPercent, item.CapacityCriticalPercent, item.Version, item.Consistency);
    }

    public async Task<IReadOnlyCollection<AlertRuleDto>> ListAlertRulesAsync(CancellationToken cancellationToken) =>
        (await repository.ListAlertRulesAsync(cancellationToken)).Select(Map).ToArray();

    public async Task<AlertRuleDto> SaveAlertRuleAsync(AlertWriteContext context, SaveAlertRuleCommand command, CancellationToken cancellationToken)
    {
        ValidateAlertContext(context);
        if (!AlertTypes.Contains(command.RuleType)) throw new ArgumentException("Unsupported alert rule type.");
        _ = MonitoringAlertPolicy.Parse(command);
        if (command.EvaluationWindowSeconds is < 12 or > 86400) throw new ArgumentException("Alert evaluation window is out of range.");
        if (command.Severity is not ("Info" or "Warning" or "Critical")) throw new ArgumentException("Invalid alert severity.");
        return Map(await repository.SaveAlertRuleAsync(context, command, cancellationToken));
    }

    public async Task<IReadOnlyCollection<AlertEventDto>> ListAlertsAsync(bool includeRecovered, CancellationToken cancellationToken) =>
        (await repository.ListAlertsAsync(includeRecovered, cancellationToken)).Select(Map).ToArray();

    public async Task<IReadOnlyCollection<AlertEventHistoryDto>> ListAlertHistoryAsync(
        Guid? alertEventId,
        int take,
        CancellationToken cancellationToken) =>
        (await repository.ListAlertHistoryAsync(alertEventId, Math.Clamp(take, 1, 1000), cancellationToken))
        .Select(item => new AlertEventHistoryDto(item.Id, item.AlertEventId, item.State, item.ActorSubject, item.OccurredAtUtc))
        .ToArray();

    public async Task<AlertEventDto> UpdateAlertAsync(Guid id, AlertWriteContext context, DateTimeOffset? silencedUntilUtc, CancellationToken cancellationToken)
    {
        ValidateAlertContext(context);
        if (silencedUntilUtc is { } until && (until <= DateTimeOffset.UtcNow || until > DateTimeOffset.UtcNow.AddDays(30)))
            throw new ArgumentException("Silence expiration is out of range.");
        return Map(await repository.UpdateAlertAsync(id, context, silencedUntilUtc, cancellationToken));
    }

    private static void ValidateAlertContext(AlertWriteContext context)
    {
        if (string.IsNullOrWhiteSpace(context.Subject) || context.Subject.Length > 256 ||
            string.IsNullOrWhiteSpace(context.TraceId) || context.TraceId.Length > 128)
            throw new ArgumentException("Authenticated alert actor and bounded request trace are required.");
    }

    public async Task<IReadOnlyCollection<PlatformSettingDto>> ListSettingsAsync(CancellationToken cancellationToken) =>
        (await repository.ListSettingsAsync(cancellationToken)).Select(item => new PlatformSettingDto(item.Key, item.ValueJson, item.Version, item.UpdatedAtUtc)).ToArray();

    public async Task<PlatformSettingDto> SaveSettingAsync(string key, string actorSubject, SavePlatformSettingCommand command, CancellationToken cancellationToken)
    {
        if (!SettingKeys.Contains(key)) throw new ArgumentException("Setting key is not approved.");
        ValidateJson(command.ValueJson);
        var item = await repository.SaveSettingAsync(key, actorSubject, command, cancellationToken);
        return new PlatformSettingDto(item.Key, item.ValueJson, item.Version, item.UpdatedAtUtc);
    }

    public async Task<IReadOnlyCollection<AuditEventDto>> ListAuditAsync(
        int take,
        string? actorSubject,
        string? action,
        string? result,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        bool export,
        CancellationToken cancellationToken)
    {
        if (fromUtc > toUtc) throw new ArgumentException("Audit time range is invalid.");
        ValidateAuditFilter(actorSubject, nameof(actorSubject));
        ValidateAuditFilter(action, nameof(action));
        ValidateAuditFilter(result, nameof(result));
        return (await repository.ListAuditAsync(Math.Clamp(take, 1, export ? 5000 : 500), actorSubject, action, result,
            fromUtc, toUtc, cancellationToken)).Select(item => new AuditEventDto(item.Id,
                item.ActorSubject, item.Action, item.TargetType, item.TargetId, item.Result, item.SourceIp, item.JobId, item.TraceId,
                item.OccurredAtUtc)).ToArray();
    }

    private static ScheduledTaskDto Map(Domain.Entities.ScheduledTask item) => new(item.Id, item.TaskType, item.Name,
        item.ScheduleKind, item.ScheduleExpression, item.Timezone, item.ParametersJson, item.ConcurrencyPolicy, item.TimeoutSeconds, item.IsEnabled,
        item.NextRunAtUtc, item.Version);
    private static AlertRuleDto Map(Domain.Entities.AlertRule item) => new(item.Id, item.RuleType, item.ResourceSelectorJson,
        item.ThresholdJson, item.EvaluationWindowSeconds, item.Severity, item.IsEnabled, item.Version);
    private static AlertEventDto Map(Domain.Entities.AlertEvent item) => new(item.Id, item.RuleId, item.ResourceId, item.State,
        item.Severity, item.OccurrenceCount, item.FirstOccurredAtUtc, item.LastOccurredAtUtc, item.RecoveredAtUtc,
        item.AcknowledgedBySubject, item.AcknowledgedAtUtc, item.SilencedUntilUtc, item.SummaryCode, BackupCapacityPolicy.ReadDetail(item), MonitoringAlertPolicy.ReadDetail(item));

    private static void ValidateName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 120) throw new ArgumentException("Name is required and must be at most 120 characters.");
    }
    private static void ValidateSchedule(string kind, string expression)
    {
        if (kind is not ("Cron" or "Interval" or "Daily")) throw new ArgumentException("Unsupported schedule kind.");
        if (string.IsNullOrWhiteSpace(expression) || expression.Length > 128) throw new ArgumentException("Schedule expression is invalid.");
        if (kind == "Cron" && expression.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length != 5) throw new ArgumentException("Cron expression must have five fields.");
    }
    private static void ValidateJson(string json)
    {
        if (json.Length > 32768) throw new ArgumentException("JSON value is too large.");
        using var _ = JsonDocument.Parse(json);
    }
    private static void ValidateAuditFilter(string? value, string name)
    {
        if (value?.Length > 256 || value?.Any(char.IsControl) == true)
            throw new ArgumentException($"Audit filter is invalid: {name}.");
    }
}
