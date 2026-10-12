using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Abstractions;

public interface IPortalRepository
{
    Task<IReadOnlyCollection<PortalItem>> ListVisibleAsync(string subject, bool administrator, CancellationToken cancellationToken);
    Task<PortalItem> SaveAsync(string subject, bool administrator, SavePortalItemCommand command, CancellationToken cancellationToken);
    Task DeleteAsync(string subject, bool administrator, Guid id, CancellationToken cancellationToken);
}

public interface IJobRepository
{
    Task<OperationJob> EnqueueAsync(OperationJob job, OutboxMessage outbox, AuditEvent audit, CancellationToken cancellationToken);
    Task<OperationJob?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<OperationJob>> ListAsync(int take, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<OperationJobEvent>> ListEventsAsync(Guid id, long afterSequence, CancellationToken cancellationToken);
    Task RequestCancellationAsync(Guid id, string actorSubject, CancellationToken cancellationToken);
}

public interface IAgentGateway
{
    Task<AgentCapabilitiesDto> GetCapabilitiesAsync(CancellationToken cancellationToken);
    Task<AgentHealthDto> GetHealthAsync(CancellationToken cancellationToken);
    Task<HostInfoDto> GetHostInfoAsync(CancellationToken cancellationToken);
    Task<IReadOnlyCollection<JournalEntryDto>> QueryJournalAsync(string? unit, int take, int sinceMinutes, string? priority, string? keyword, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<ContainerDto>> ListContainersAsync(bool includeStopped, CancellationToken cancellationToken);
    Task<ContainerLogsDto> GetContainerLogsAsync(string containerId, int tail, int sinceMinutes, CancellationToken cancellationToken);
    Task<ContainerStatsDto> GetContainerStatsAsync(string containerId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<DockerEventDto>> ListDockerEventsAsync(DateTimeOffset sinceUtc, int take, CancellationToken cancellationToken);
    Task<ContainerInspectDto> InspectContainerAsync(string containerId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<ManagedResourceDto>> ListResourcesAsync(string kind, CancellationToken cancellationToken);
    Task<ManagedResourceDto> GetConfigRepositoryStatusAsync(CancellationToken cancellationToken);
    Task<DockerSettingsDto> GetDockerSettingsAsync(CancellationToken cancellationToken);
    Task<ApplicationImageMetadataDto> InspectApplicationImageAsync(string image, bool pullIfMissing, CancellationToken cancellationToken);
    Task<PlanDto> PlanAsync(string area, string action, string? resourceId, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<MetricValueDto>> GetMetricsSnapshotAsync(CancellationToken cancellationToken);
    Task<AgentOperationDto> ExecuteAsync(string area, string action, string resourceId, IReadOnlyDictionary<string, string> parameters, string? planHash, Guid jobId, string idempotencyKey, CancellationToken cancellationToken);
    Task<AgentOperationDto> GetOperationAsync(string operationId, CancellationToken cancellationToken);
    Task<AgentOperationDto> CancelOperationAsync(string operationId, CancellationToken cancellationToken);
    Task<byte[]> DownloadDiagnosticBundleAsync(string bundleId, CancellationToken cancellationToken);
}

public interface IHostProcessGateway
{
    Task<HostProcessPageDto> ListProcessesAsync(HostProcessQuery query, CancellationToken cancellationToken);
    Task<HostProcessDto> GetProcessAsync(int pid, string instanceId, CancellationToken cancellationToken);
}

public interface ICatalogProvider
{
    Task<CatalogResultDto> GetPopularAsync(CancellationToken cancellationToken);
}

public interface IOneTimeSecretStore
{
    Task<OneTimeSecretTicketDto> StoreAsync(string ownerSubject, string secret, CancellationToken cancellationToken);
    Task<string?> ConsumeAsync(string ownerSubject, string token, CancellationToken cancellationToken);
}

public interface IIdentityDirectory
{
    Task<CurrentUserDto> ResolveCurrentAsync(string subject, string? name, IReadOnlyCollection<string> claimGroups, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<ManagedResourceDto>> ListUsersAsync(CancellationToken cancellationToken);
    Task<IReadOnlyCollection<ManagedResourceDto>> ListGroupsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyCollection<ManagedResourceDto>> ListSsoApplicationsAsync(CancellationToken cancellationToken);
}

public interface IIdentityManager
{
    Task ExecuteAsync(string action, string? resourceId, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken);
}

public interface IRoleMappingRepository
{
    Task<IReadOnlyCollection<RoleMapping>> ListAsync(CancellationToken cancellationToken);
    Task<RoleMapping> SaveAsync(string actorSubject, string traceId, SaveRoleMappingCommand command, CancellationToken cancellationToken);
    Task DeleteAsync(string actorSubject, string traceId, Guid id, long expectedVersion, CancellationToken cancellationToken);
}

public interface IAdministratorGroupConfiguration
{
    string GroupId { get; }
}

public interface IManagementQuery
{
    Task<IReadOnlyCollection<ManagedResourceDto>> ListAsync(string area, CancellationToken cancellationToken);
}

public interface IMetricsQuery
{
    Task<IReadOnlyCollection<MetricSeriesSnapshotDto>> GetHistoryAsync(IReadOnlyCollection<string> kinds, int take, CancellationToken cancellationToken);
}

public interface IApplicationRepository
{
    Task<IReadOnlyCollection<ApplicationInstallation>> ListAsync(CancellationToken cancellationToken);
    Task<ApplicationInstallation?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<(ManagedResource Resource, string Role)>> ListResourcesAsync(Guid applicationId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<ApplicationUpdateRun>> ListUpdateRunsAsync(Guid applicationId, int take, CancellationToken cancellationToken);
}

public interface IContainerUpdateRepository
{
    Task<IReadOnlyCollection<ContainerUpdateRun>> ListAsync(string? containerIdOrName, int take, CancellationToken cancellationToken);
}

public interface IDockerEventRepository
{
    Task<IReadOnlyCollection<DockerEventRecord>> ListAsync(string? containerIdOrName, int take, CancellationToken cancellationToken);
}

public interface IGlobalSearchRepository
{
    Task<IReadOnlyCollection<GlobalSearchResultDto>> SearchAdministrationAsync(string query, int take, CancellationToken cancellationToken);
}

public interface IGovernanceRepository
{
    Task<IReadOnlyCollection<ScheduledTask>> ListSchedulesAsync(CancellationToken cancellationToken);
    Task<ScheduledTask> SaveScheduleAsync(string actorSubject, SaveScheduledTaskCommand command, CancellationToken cancellationToken);
    Task<ScheduledTask> TriggerScheduleAsync(Guid id, string actorSubject, CancellationToken cancellationToken);
    Task DeleteScheduleAsync(Guid id, long expectedVersion, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<ScheduledTaskRun>> ListScheduleRunsAsync(Guid? scheduleId, int take, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<BackupPolicy>> ListBackupPoliciesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyCollection<BackupRecord>> ListBackupRecordsAsync(string? instanceResourceId, int take, CancellationToken cancellationToken);
    Task<string> ResolveResourceExternalIdAsync(Guid id, CancellationToken cancellationToken);
    Task<BackupPolicy> SaveBackupPolicyAsync(SaveBackupPolicyCommand command, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<AlertRule>> ListAlertRulesAsync(CancellationToken cancellationToken);
    Task<AlertRule> SaveAlertRuleAsync(SaveAlertRuleCommand command, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<AlertEvent>> ListAlertsAsync(bool includeRecovered, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<AlertEventHistory>> ListAlertHistoryAsync(Guid? alertEventId, int take, CancellationToken cancellationToken);
    Task<AlertEvent> UpdateAlertAsync(Guid id, string actorSubject, DateTimeOffset? silencedUntilUtc, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<PlatformSetting>> ListSettingsAsync(CancellationToken cancellationToken);
    Task<PlatformSetting> SaveSettingAsync(string key, string actorSubject, SavePlatformSettingCommand command, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<AuditEvent>> ListAuditAsync(int take, string? actorSubject, string? action, string? result,
        DateTimeOffset? fromUtc, DateTimeOffset? toUtc, CancellationToken cancellationToken);
}
