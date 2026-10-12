namespace WhaleDeck.Application.Models;

public sealed record ScheduledTaskDto(Guid Id, string TaskType, string Name, string ScheduleKind, string ScheduleExpression,
    string Timezone, string ParametersJson, string ConcurrencyPolicy, int TimeoutSeconds, bool IsEnabled, DateTimeOffset? NextRunAtUtc, long Version);

public sealed record ScheduledTaskRunDto(Guid Id, Guid ScheduleId, Guid JobId, DateTimeOffset ScheduledForUtc,
    DateTimeOffset? StartedAtUtc, DateTimeOffset? CompletedAtUtc, string Result);

public sealed record SaveScheduledTaskCommand(Guid? Id, string TaskType, string Name, string ScheduleKind,
    string ScheduleExpression, string Timezone, string ParametersJson, string ConcurrencyPolicy, int TimeoutSeconds,
    bool IsEnabled, long? ExpectedVersion);

public sealed record BackupTargetCapacityDto(string TargetDirectoryId, bool Available, long TotalBytes, long UsedBytes,
    long FreeBytes, DateTimeOffset? ObservedAtUtc, string? ErrorCode)
{
    public const string FixedTarget = "database-platform.backup.hdd";
    public static BackupTargetCapacityDto Unknown(string code) => new(FixedTarget, false, 0, 0, 0, null, code);
    public bool IsCurrent(DateTimeOffset now) => Available && TargetDirectoryId == FixedTarget && TotalBytes > 0 &&
        UsedBytes >= 0 && UsedBytes <= TotalBytes && FreeBytes >= 0 && FreeBytes <= TotalBytes - UsedBytes &&
        ObservedAtUtc is { } observed && observed >= now.AddSeconds(-90) && observed <= now.AddSeconds(5);
    public double? UsedPercent => Available && TotalBytes > 0 ? UsedBytes * 100d / TotalBytes : null;
}

public sealed record BackupCapacityAlertDetail(Guid PolicyId, string TargetDirectoryId, bool Available,
    double? UsedPercent, long? FreeBytes, int WarningPercent, int CriticalPercent, DateTimeOffset? ObservedAtUtc);

public sealed record BackupPolicyDto(Guid Id, string InstanceResourceId, bool IsEnabled, string ScheduleExpression,
    string Timezone, int RetentionCount, int RetentionDays, string TargetDirectoryId, string Compression,
    bool VerifyAfterBackup, int CapacityWarningPercent, int CapacityCriticalPercent, long Version, string Consistency = "quiesced");

public sealed record SaveBackupPolicyCommand(Guid? Id, string InstanceResourceId, bool IsEnabled, string ScheduleExpression,
    string Timezone, int RetentionCount, int RetentionDays, string TargetDirectoryId, string Compression,
    bool VerifyAfterBackup, int CapacityWarningPercent, int CapacityCriticalPercent, long? ExpectedVersion,
    string Consistency = "quiesced", bool Confirmed = false);

public sealed record BackupRecordDto(
    Guid Id,
    string InstanceResourceId,
    Guid? PolicyId,
    Guid JobId,
    string Status,
    string? RelativePath,
    long? SizeBytes,
    string? ChecksumAlgorithm,
    string? Checksum,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    DateTimeOffset? VerifiedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    string? ErrorCode);

public sealed record AlertRuleDto(Guid Id, string RuleType, string ResourceSelectorJson, string ThresholdJson,
    int EvaluationWindowSeconds, string Severity, bool IsEnabled, long Version);

public sealed record SaveAlertRuleCommand(Guid? Id, string RuleType, string ResourceSelectorJson, string ThresholdJson,
    int EvaluationWindowSeconds, string Severity, bool IsEnabled, long? ExpectedVersion);

public sealed record AlertEventDto(Guid Id, Guid RuleId, Guid? ResourceId, string State, string Severity, int OccurrenceCount,
    DateTimeOffset FirstOccurredAtUtc, DateTimeOffset LastOccurredAtUtc, DateTimeOffset? RecoveredAtUtc,
    string? AcknowledgedBySubject, DateTimeOffset? AcknowledgedAtUtc, DateTimeOffset? SilencedUntilUtc, string SummaryCode,
    BackupCapacityAlertDetail? Capacity = null);

public sealed record AlertEventHistoryDto(Guid Id, Guid AlertEventId, string State, string ActorSubject,
    DateTimeOffset OccurredAtUtc);

public sealed record PlatformSettingDto(string Key, string ValueJson, long Version, DateTimeOffset UpdatedAtUtc);
public sealed record SavePlatformSettingCommand(string ValueJson, long? ExpectedVersion);

public sealed record AuditEventDto(Guid Id, string ActorSubject, string Action, string TargetType, string TargetId,
    string Result, string? SourceIp, Guid? JobId, string TraceId, DateTimeOffset OccurredAtUtc);
