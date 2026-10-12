namespace WhaleDeck.Domain.Entities;

public sealed class PortalItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string? OwnerSubject { get; set; }
    public required string Scope { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public required string Url { get; set; }
    public string IconKind { get; set; } = "BuiltIn";
    public required string IconValue { get; set; }
    public string Color { get; set; } = "primary";
    public int SortOrder { get; set; }
    public bool IsEnabled { get; set; } = true;
    public long Version { get; set; }
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class PublicPortalPreference
{
    public required string Subject { get; init; }
    public Guid PortalItemId { get; init; }
    public bool IsHidden { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class RoleMapping
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string AuthentikGroupId { get; set; }
    public required string AuthentikGroupNameSnapshot { get; set; }
    public string Role { get; set; } = "Administrator";
    public bool IsEnabled { get; set; } = true;
    public long Version { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ManagedResource
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string ResourceType { get; set; }
    public required string ExternalId { get; set; }
    public required string DisplayName { get; set; }
    public required string ProtectionLevel { get; set; }
    public string LabelsJson { get; set; } = "{}";
    public string Source { get; set; } = "Discovery";
    public DateTimeOffset LastSeenAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public long Version { get; set; }
}

public sealed class ApplicationInstallation
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string CatalogAppId { get; set; }
    public required string TemplateId { get; set; }
    public required string TemplateVersion { get; set; }
    public required string DisplayName { get; set; }
    public required string InstalledVersion { get; set; }
    public string? DesiredVersion { get; set; }
    public string State { get; set; } = "Installed";
    public bool AutoUpdateEnabled { get; set; }
    public string ConfigSummaryJson { get; set; } = "{}";
    public required string InstalledBySubject { get; init; }
    public DateTimeOffset InstalledAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public long Version { get; set; }
}

public sealed class ApplicationResource
{
    public Guid ApplicationId { get; init; }
    public Guid ResourceId { get; init; }
    public required string Role { get; init; }
}

public sealed class ApplicationUpdateRun
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ApplicationId { get; init; }
    public string? OldImageDigest { get; init; }
    public required string NewImageDigest { get; init; }
    public string? VersionPolicy { get; init; }
    public required string PlanHash { get; init; }
    public Guid JobId { get; init; }
    public DateTimeOffset StartedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public string Result { get; set; } = "Running";
    public bool WasRolledBack { get; set; }
    public string? ErrorSummary { get; set; }
}

public sealed class ContainerUpdateRun
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string ExternalContainerId { get; init; }
    public required string ContainerName { get; init; }
    public required string Image { get; init; }
    public string? OldImageDigest { get; init; }
    public string? NewImageDigest { get; init; }
    public string? VersionPolicy { get; init; }
    public Guid JobId { get; init; }
    public DateTimeOffset StartedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public string Result { get; set; } = "Running";
    public bool WasRolledBack { get; set; }
    public string? ErrorSummary { get; set; }
}

public sealed class DockerEventRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Fingerprint { get; init; }
    public DateTimeOffset OccurredAtUtc { get; init; }
    public required string EventType { get; init; }
    public required string Action { get; init; }
    public required string ResourceId { get; init; }
    public required string ResourceName { get; init; }
    public string? Image { get; init; }
    public string AttributesJson { get; init; } = "{}";
    public DateTimeOffset RecordedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class OperationJob
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string JobType { get; init; }
    public required string ActorSubject { get; init; }
    public Guid? ResourceId { get; init; }
    public string State { get; set; } = "Queued";
    public string Phase { get; set; } = "Queued";
    public short? ProgressPercent { get; set; }
    public required string IdempotencyKey { get; init; }
    public string RequestJson { get; set; } = "{}";
    public string? ResultJson { get; set; }
    public string? ErrorCode { get; set; }
    public Guid? AgentOperationId { get; set; }
    public DateTimeOffset? CancelRequestedAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public long Version { get; set; }
}

public sealed class OperationJobEvent
{
    public Guid JobId { get; init; }
    public long Sequence { get; init; }
    public required string State { get; init; }
    public required string Phase { get; init; }
    public short? ProgressPercent { get; init; }
    public required string MessageCode { get; init; }
    public string DetailJson { get; init; } = "{}";
    public DateTimeOffset OccurredAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class ScheduledTask
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string TaskType { get; set; }
    public required string Name { get; set; }
    public required string ScheduleKind { get; set; }
    public required string ScheduleExpression { get; set; }
    public string Timezone { get; set; } = "Asia/Shanghai";
    public string ParametersJson { get; set; } = "{}";
    public string ConcurrencyPolicy { get; set; } = "Forbid";
    public int TimeoutSeconds { get; set; } = 3600;
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset? NextRunAtUtc { get; set; }
    public required string CreatedBySubject { get; init; }
    public required string UpdatedBySubject { get; set; }
    public long Version { get; set; }
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ScheduledTaskRun
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ScheduleId { get; init; }
    public Guid JobId { get; init; }
    public DateTimeOffset ScheduledForUtc { get; init; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public string Result { get; set; } = "Queued";
}

public sealed class OutboxMessage
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string MessageType { get; init; }
    public required string PayloadJson { get; init; }
    public DateTimeOffset OccurredAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset AvailableAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public int Attempts { get; set; }
    public DateTimeOffset? ProcessedAtUtc { get; set; }
    public string? LastErrorCode { get; set; }
}

public sealed class ResourceOperationLease
{
    public required string LockKey { get; init; }
    public Guid JobId { get; set; }
    public required string OwnerId { get; set; }
    public DateTimeOffset AcquiredAtUtc { get; set; }
    public DateTimeOffset LeaseExpiresAtUtc { get; set; }
    public long Version { get; set; }
}

public sealed class MetricSeries
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ResourceId { get; init; }
    public required string MetricKind { get; init; }
    public required string Unit { get; init; }
    public string DimensionsJson { get; init; } = "{}";
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastSeenAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class MetricSample
{
    public Guid SeriesId { get; init; }
    public DateTimeOffset SampledAtUtc { get; init; }
    public double? ValueDouble { get; init; }
    public string Quality { get; init; } = "Good";
}

public sealed class MetricRollup
{
    public Guid SeriesId { get; init; }
    public DateTimeOffset WindowStartUtc { get; init; }
    public required string Resolution { get; init; }
    public double Minimum { get; init; }
    public double Maximum { get; init; }
    public double Average { get; init; }
    public double Sum { get; init; }
    public long SampleCount { get; init; }
}

public sealed class BackupPolicy
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid InstanceResourceId { get; init; }
    public bool IsEnabled { get; set; } = true;
    public required string ScheduleExpression { get; set; }
    public string Timezone { get; set; } = "Asia/Shanghai";
    public int RetentionCount { get; set; } = 14;
    public int RetentionDays { get; set; } = 14;
    public required string TargetDirectoryId { get; set; }
    public string Compression { get; set; } = "Default";
    public bool VerifyAfterBackup { get; set; } = true;
    public string Consistency { get; set; } = "quiesced";
    public int CapacityWarningPercent { get; set; } = 80;
    public int CapacityCriticalPercent { get; set; } = 85;
    public long Version { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class BackupRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid InstanceResourceId { get; init; }
    public Guid? PolicyId { get; init; }
    public Guid JobId { get; init; }
    public string Status { get; set; } = "Running";
    public string? RelativePath { get; set; }
    public long? SizeBytes { get; set; }
    public string? ChecksumAlgorithm { get; set; }
    public string? Checksum { get; set; }
    public DateTimeOffset StartedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public DateTimeOffset? VerifiedAtUtc { get; set; }
    public DateTimeOffset? ExpiresAtUtc { get; set; }
    public string? ErrorCode { get; set; }
}

public sealed class AlertRule
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string RuleType { get; set; }
    public string ResourceSelectorJson { get; set; } = "{}";
    public string ThresholdJson { get; set; } = "{}";
    public int EvaluationWindowSeconds { get; set; } = 60;
    public string Severity { get; set; } = "Warning";
    public bool IsEnabled { get; set; } = true;
    public long Version { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AlertEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid RuleId { get; init; }
    public Guid? ResourceId { get; init; }
    public required string Fingerprint { get; init; }
    public string State { get; set; } = "Active";
    public required string Severity { get; init; }
    public int OccurrenceCount { get; set; } = 1;
    public DateTimeOffset FirstOccurredAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastOccurredAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RecoveredAtUtc { get; set; }
    public string? AcknowledgedBySubject { get; set; }
    public DateTimeOffset? AcknowledgedAtUtc { get; set; }
    public DateTimeOffset? SilencedUntilUtc { get; set; }
    public Guid? RelatedJobId { get; set; }
    public required string SummaryCode { get; init; }
    public string DetailJson { get; set; } = "{}";
}

public sealed class AlertEventHistory
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid AlertEventId { get; init; }
    public required string State { get; init; }
    public required string ActorSubject { get; init; }
    public DateTimeOffset OccurredAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class ConfigSnapshot
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string RepositoryId { get; init; }
    public required string Branch { get; init; }
    public required string BaseCommitSha { get; init; }
    public string? ResultCommitSha { get; set; }
    public string FileManifestJson { get; init; } = "[]";
    public string DiffSummaryJson { get; init; } = "{}";
    public required string SensitiveScanResult { get; init; }
    public Guid JobId { get; init; }
    public required string CreatedBySubject { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class ConfigSyncRun
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string RepositoryId { get; init; }
    public required string Direction { get; init; }
    public string? BeforeCommitSha { get; init; }
    public string? AfterCommitSha { get; set; }
    public string State { get; set; } = "Running";
    public Guid JobId { get; init; }
    public string? ErrorCode { get; set; }
    public DateTimeOffset StartedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAtUtc { get; set; }
}

public sealed class CatalogSnapshot
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Provider { get; init; }
    public required string PayloadJson { get; init; }
    public DateTimeOffset FetchedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAtUtc { get; init; }
    public string? SourceEtag { get; init; }
}
