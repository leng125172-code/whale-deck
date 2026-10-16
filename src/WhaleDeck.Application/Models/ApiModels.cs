namespace WhaleDeck.Application.Models;

public sealed record PortalItemDto(
    Guid Id,
    string Scope,
    string Name,
    string? Description,
    string Url,
    string IconKind,
    string IconValue,
    string Color,
    int SortOrder,
    bool IsEnabled,
    long Version);

public sealed record SavePortalItemCommand(
    Guid? Id,
    string Scope,
    string Name,
    string? Description,
    string Url,
    string IconKind,
    string IconValue,
    string Color,
    int SortOrder,
    bool IsEnabled,
    long? ExpectedVersion);

public sealed record CurrentUserDto(
    string Subject,
    string? Name,
    IReadOnlyCollection<string> Groups,
    bool IsAdministrator,
    DateTimeOffset PermissionExpiresAtUtc);

public sealed record AgentHealthDto(
    bool Available,
    bool DockerAvailable,
    bool SystemdAvailable,
    string Status,
    DateTimeOffset ObservedAtUtc);

public sealed record AgentCapabilitiesDto(
    string AgentVersion,
    int ProtocolMajor,
    int ProtocolMinor,
    IReadOnlyCollection<string> Capabilities);

public sealed record AgentStatusDto(
    AgentCapabilitiesDto Capabilities,
    AgentHealthDto Health,
    IReadOnlyCollection<JobDto> CurrentJobs);

public sealed record ServiceEntryDto(
    string Id,
    string Name,
    string Description,
    string Url,
    string Status,
    long? LatencyMilliseconds,
    DateTimeOffset CheckedAtUtc);

public sealed record HostInfoDto(
    string HostName,
    string Distribution,
    string KernelVersion,
    string Architecture,
    DateTimeOffset BootTimeUtc,
    TimeSpan Uptime,
    int LogicalProcessorCount,
    ulong TotalMemoryBytes,
    IReadOnlyCollection<HostAddressDto> Addresses);

public sealed record HostAddressDto(string InterfaceName, string Address);

public sealed record JournalEntryDto(
    DateTimeOffset OccurredAtUtc,
    string Unit,
    string Priority,
    string Process,
    int ProcessId,
    string Message);

public sealed record ManagedResourceDto(
    string Id,
    string Name,
    string Type,
    string State,
    string Version,
    bool IsProtected,
    IReadOnlyDictionary<string, string> Attributes);

public sealed record ApplicationImageMetadataDto(
    string Image,
    string ImageId,
    IReadOnlyCollection<string> Environment,
    IReadOnlyCollection<string> ExposedPorts,
    IReadOnlyCollection<string> Volumes,
    IReadOnlyCollection<string> Entrypoint,
    IReadOnlyCollection<string> Command,
    IReadOnlyDictionary<string, string> Labels);

public sealed record DockerSettingsDto(
    string SettingsJson,
    IReadOnlyCollection<string> EditableKeys);

public sealed record ContainerDto(
    string Id,
    string Name,
    string Image,
    string State,
    string Status,
    bool IsProtected,
    string ProtectionLevel,
    IReadOnlyDictionary<string, string> Labels,
    IReadOnlyCollection<string>? Ports = null,
    IReadOnlyCollection<string>? Networks = null,
    IReadOnlyCollection<string>? Addresses = null,
    DateTimeOffset? CreatedAtUtc = null,
    string? ImageId = null,
    IReadOnlyCollection<string>? VolumeNames = null,
    IReadOnlyCollection<string>? NetworkIds = null);

public sealed record ContainerLogsDto(IReadOnlyCollection<string> Lines, bool Truncated);

public sealed record ContainerStatsDto(IReadOnlyDictionary<string, string> Values);
public sealed record ContainerUsageDto(string ContainerId, string State, DateTimeOffset? SampledAtUtc,
    double? CpuPercent, double CpuWindowSeconds, ulong? MemoryUsedBytes, ulong? MemoryLimitBytes, double? MemoryPercent,
    ulong? NetworkReceiveBytes, ulong? NetworkSendBytes, ulong? BlockReadBytes, ulong? BlockWriteBytes, ulong? Pids, string Quality);
public sealed record ContainerUsageBatchDto(IReadOnlyCollection<ContainerUsageDto> Items, DateTimeOffset? SampledAtUtc,
    bool IsStale, bool IsTruncated, int SkippedCount, int TotalCount, int IntervalSeconds);
public sealed record DockerEventDto(
    string Fingerprint,
    DateTimeOffset OccurredAtUtc,
    string EventType,
    string Action,
    string ResourceId,
    string ResourceName,
    string? Image,
    IReadOnlyDictionary<string, string> Attributes);

public sealed record ContainerInspectDto(
    string Id,
    string Name,
    string Image,
    IReadOnlyCollection<string> EnvironmentNames,
    IReadOnlyCollection<string> Command,
    IReadOnlyCollection<string> Entrypoint,
    IReadOnlyDictionary<string, string> Labels,
    IReadOnlyCollection<string> Ports,
    IReadOnlyCollection<string> Volumes,
    IReadOnlyCollection<string> Networks,
    string RestartPolicy,
    double Cpus,
    long MemoryMb,
    IReadOnlyCollection<string> HealthCommand,
    long HealthIntervalSeconds,
    long HealthTimeoutSeconds,
    long HealthRetries,
    ContainerRelationshipsDto? Relationships = null);

public sealed record ContainerRelationshipsDto(string ImageId, string ComposeProject, string ComposeService,
    string NetworkMode, IReadOnlyCollection<ContainerNetworkEndpointDto> Endpoints,
    IReadOnlyCollection<ContainerMountDto> Mounts, IReadOnlyCollection<ContainerPortBindingDto> PortBindings,
    bool IsTruncated);
public sealed record ContainerNetworkEndpointDto(string Name, string NetworkId, string Ipv4Address,
    long Ipv4PrefixLength, string Ipv4Gateway, string Ipv6Address, long Ipv6PrefixLength,
    string Ipv6Gateway, string MacAddress);
public sealed record ContainerMountDto(string Type, string Name, string Source, string Destination, bool ReadWrite);
public sealed record ContainerPortBindingDto(string ContainerPort, string HostAddress, string HostPort, bool Published);

public sealed record OverviewDto(
    string Scope,
    string PlatformStatus,
    DateTimeOffset SampledAtUtc,
    IReadOnlyCollection<PortalItemDto> Portals,
    AgentHealthDto? Agent,
    HostInfoDto? Host,
    IReadOnlyCollection<ManagedResourceDto>? Resources,
    IReadOnlyCollection<MetricValueDto>? Metrics,
    IReadOnlyCollection<CatalogApplicationDto>? Applications,
    bool CatalogIsStale);

public sealed record OperationCommand(
    string Area,
    string Action,
    string? ResourceId,
    string IdempotencyKey,
    string RequestJson,
    string? PlanHash,
    bool Confirmed,
    Guid? RetriedFromJobId = null);

public sealed record JobRetryContextDto(Guid JobId, string JobType, string? ResourceId, bool RequiresInput, string? InputKind);
public sealed record JobRetryPlanDto(JobRetryContextDto Context, PlanDto? Plan);

public sealed record JobDto(
    Guid Id,
    string JobType,
    string State,
    string Phase,
    short? ProgressPercent,
    string? ErrorCode,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? ResultJson);

public sealed record JobEventDto(
    long Sequence,
    string State,
    string Phase,
    short? ProgressPercent,
    string MessageCode,
    DateTimeOffset OccurredAtUtc);

public sealed record JobEventBatchDto(IReadOnlyCollection<JobEventDto> Events, bool IsTerminal);

public sealed record PlanDto(
    string PlanHash,
    DateTimeOffset ExpiresAtUtc,
    IReadOnlyCollection<string> Changes,
    IReadOnlyCollection<string> Warnings);

public sealed record MetricValueDto(string Kind, string DeviceId, double Value, string Unit, string Quality, DateTimeOffset SampledAtUtc);

public sealed record MetricPointDto(DateTimeOffset SampledAtUtc, double? Value, string Quality, double? AggregationWeight = null);
public sealed record MetricSeriesSnapshotDto(string Kind, string DeviceId, string Unit, IReadOnlyCollection<MetricPointDto> Points,
    DateTimeOffset? WindowStartUtc = null, DateTimeOffset? WindowEndUtc = null, int ResolutionSeconds = 6);

public sealed record AgentOperationDto(
    string OperationId,
    string State,
    string Phase,
    int ProgressPercent,
    string? ErrorCode,
    string? ResultJson = null);

public sealed record OneTimeSecretTicketDto(string Token, DateTimeOffset ExpiresAtUtc);

public sealed record CatalogApplicationDto(
    string Id,
    string Name,
    string Description,
    string Image,
    string Version,
    string? IconUrl,
    bool Installed,
    string? State);

public sealed record CatalogResultDto(
    IReadOnlyCollection<CatalogApplicationDto> Applications,
    DateTimeOffset FetchedAtUtc,
    bool IsStale);

public sealed record ApplicationInstallationDto(
    Guid Id,
    string CatalogAppId,
    string TemplateId,
    string TemplateVersion,
    string DisplayName,
    string InstalledVersion,
    string? DesiredVersion,
    string State,
    bool AutoUpdateEnabled,
    string ConfigSummaryJson,
    string InstalledBySubject,
    DateTimeOffset InstalledAtUtc,
    DateTimeOffset UpdatedAtUtc,
    long Version);

public sealed record ApplicationResourceDto(
    string Role,
    Guid ResourceId,
    string ExternalId,
    string Name,
    string Type,
    string State,
    string Version,
    bool IsProtected);

public sealed record ApplicationUpdateRunDto(
    Guid Id,
    Guid ApplicationId,
    string? OldImageDigest,
    string NewImageDigest,
    string? VersionPolicy,
    string PlanHash,
    Guid JobId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string Result,
    bool WasRolledBack,
    string? ErrorSummary);

public sealed record ApplicationDetailDto(
    ApplicationInstallationDto Installation,
    IReadOnlyCollection<ApplicationResourceDto> Resources,
    IReadOnlyCollection<ApplicationUpdateRunDto> UpdateHistory);

public sealed record ContainerUpdateRunDto(
    Guid Id,
    string ExternalContainerId,
    string ContainerName,
    string Image,
    string? OldImageDigest,
    string? NewImageDigest,
    string? VersionPolicy,
    Guid JobId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string Result,
    bool WasRolledBack,
    string? ErrorSummary);

public sealed record GlobalSearchResultDto(
    string Id,
    string Kind,
    string Title,
    string Subtitle,
    string TargetUrl,
    string? State,
    bool External);
