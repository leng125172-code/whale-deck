using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Persistence;

namespace WhaleDeck.Worker;

public sealed class Worker(IServiceScopeFactory scopeFactory, ILogger<Worker> logger) : BackgroundService
{
    private static readonly Action<ILogger, Exception?> LogCycleFailure = LoggerMessage.Define(
        LogLevel.Error,
        new EventId(1001, "WorkerCycleFailed"),
        "Worker cycle failed with diagnostic code WORKER_CYCLE_FAILED");
    private static readonly Action<ILogger, Guid, int, Exception?> LogOutboxRetry = LoggerMessage.Define<Guid, int>(
        LogLevel.Warning,
        new EventId(1002, "OutboxDispatchRetry"),
        "Outbox message {MessageId} dispatch failed; retry attempt {Attempt} is scheduled");
    private static readonly Action<ILogger, Guid, string, Exception?> LogIdentityProviderRejected = LoggerMessage.Define<Guid, string>(
        LogLevel.Warning,
        new EventId(1003, "IdentityProviderRejected"),
        "Identity operation in outbox message {MessageId} was rejected: {ProviderDetail}");

    private DateTimeOffset _nextCatalogRefresh = DateTimeOffset.MinValue;
    private DateTimeOffset _nextRetentionSweep = DateTimeOffset.MinValue;
    private DateTimeOffset _nextMetricPartitionMaintenance = DateTimeOffset.MinValue;
    private readonly string _workerInstanceId = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(6));
        do
        {
            try
            {
                await RunCycle(stoppingToken);
                WorkerHeartbeat.Report();
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                LogCycleFailure(logger, exception);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunCycle(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var agent = scope.ServiceProvider.GetRequiredService<IAgentGateway>();
        var secrets = scope.ServiceProvider.GetRequiredService<IOneTimeSecretStore>();
        var identityDirectory = scope.ServiceProvider.GetRequiredService<IIdentityDirectory>();
        var identity = scope.ServiceProvider.GetRequiredService<IIdentityManager>();
        var leases = scope.ServiceProvider.GetRequiredService<ResourceLeaseManager>();
        // Prepare current/future UTC partitions before the first insert, including after long downtime.
        var now = DateTimeOffset.UtcNow;
        if (_nextMetricPartitionMaintenance <= now)
        {
            if (await MetricPartitions.MaintainAsync(db, now, cancellationToken) < 0) return;
            _nextMetricPartitionMaintenance = now.AddHours(1);
        }
        await CollectMetrics(db, agent, cancellationToken);
        await SyncManagedResources(db, agent, cancellationToken);
        await CollectContainerMetrics(db, agent, cancellationToken);
        await SyncDockerEvents(db, agent, cancellationToken);
        await DispatchScheduledTasks(db, agent, cancellationToken);
        await DispatchBackupPolicies(db, cancellationToken);
        await DispatchOutbox(db, agent, secrets, identityDirectory, identity, leases, _workerInstanceId, logger, cancellationToken);
        await ReconcileAgentOperations(db, agent, leases, _workerInstanceId, cancellationToken);
        await ApplyRollups(db, cancellationToken);
        await BackupCapacityAlerts.EvaluateAsync(db, agent, cancellationToken);
        await MonitoringAlerts.EvaluateAsync(db, DateTimeOffset.UtcNow, cancellationToken);

        if (_nextCatalogRefresh <= DateTimeOffset.UtcNow)
        {
            await scope.ServiceProvider.GetRequiredService<ICatalogProvider>().GetPopularAsync(cancellationToken);
            _nextCatalogRefresh = DateTimeOffset.UtcNow.AddHours(1);
        }
        if (_nextRetentionSweep <= DateTimeOffset.UtcNow)
        {
            await ApplyRetention(db, cancellationToken);
            _nextRetentionSweep = DateTimeOffset.UtcNow.AddHours(1);
        }
    }

    private static async Task CollectMetrics(PlatformDbContext db, IAgentGateway agent, CancellationToken cancellationToken)
    {
        var health = await agent.GetHealthAsync(cancellationToken);
        if (!health.Available) return;
        var resource = await db.ManagedResources.SingleOrDefaultAsync(item => item.ResourceType == "Host" && item.ExternalId == "local", cancellationToken);
        if (resource is null)
        {
            resource = new ManagedResource { ResourceType = "Host", ExternalId = "local", DisplayName = "Precision-7920-Tower", ProtectionLevel = "ControlPlane", Source = "Agent" };
            db.ManagedResources.Add(resource);
        }
        var snapshot = await agent.GetMetricsSnapshotAsync(cancellationToken);
        await MetricSampleWriter.StageAsync(db, resource.Id, snapshot, cancellationToken);
        resource.LastSeenAtUtc = health.ObservedAtUtc;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task CollectContainerMetrics(PlatformDbContext db, IAgentGateway agent, CancellationToken cancellationToken)
    {
        var snapshot = await agent.GetContainerUsageAsync(cancellationToken);
        if (snapshot.IsStale || snapshot.Items.Count == 0) return;
        var externalIds = snapshot.Items.Select(item => item.ContainerId).ToArray();
        var resources = await db.ManagedResources.Where(item => item.ResourceType == "Container" && externalIds.Contains(item.ExternalId))
            .Select(item => new { item.Id, item.ExternalId }).ToArrayAsync(cancellationToken);
        var identities = resources.ToDictionary(item => item.ExternalId, item => item.Id, StringComparer.Ordinal);
        var now = DateTimeOffset.UtcNow;
        var values = snapshot.Items.Where(item => identities.ContainsKey(item.ContainerId))
            .SelectMany(item => ContainerMetricProjection.Project(item, now).Select(metric => new ResourceMetric(identities[item.ContainerId], metric))).ToArray();
        await MetricSampleWriter.StageManyAsync(db, values, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task SyncManagedResources(PlatformDbContext db, IAgentGateway agent, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var discovered = (await agent.ListResourcesAsync("overview", cancellationToken))
            .Concat(await agent.ListResourcesAsync("databases", cancellationToken))
            .Concat(await agent.ListResourcesAsync("applications", cancellationToken))
            .DistinctBy(item => (item.Type, item.Id))
            .ToArray();
        var resources = new Dictionary<(string Type, string Id), ManagedResource>();
        foreach (var item in discovered)
        {
            var resource = await db.ManagedResources.SingleOrDefaultAsync(value => value.ResourceType == item.Type && value.ExternalId == item.Id, cancellationToken);
            if (resource is null)
            {
                resource = new ManagedResource
                {
                    ResourceType = item.Type,
                    ExternalId = item.Id,
                    DisplayName = item.Name,
                    ProtectionLevel = item.IsProtected ? "Protected" : "Managed",
                    Source = "Agent"
                };
                db.ManagedResources.Add(resource);
            }
            resource.DisplayName = item.Name;
            resource.ProtectionLevel = item.IsProtected ? "Protected" : "Managed";
            resource.LabelsJson = JsonSerializer.Serialize(new { state = item.State, version = item.Version, attributes = item.Attributes });
            resource.LastSeenAtUtc = now;
            resource.Version++;
            resources[(item.Type, item.Id)] = resource;
        }

        foreach (var item in discovered.Where(item => item.Type == "ComposeApplication"))
        {
            var slug = item.Id.StartsWith("application:", StringComparison.Ordinal) ? item.Id["application:".Length..] : item.Name;
            var installation = await db.ApplicationInstallations.SingleOrDefaultAsync(value => value.CatalogAppId == slug, cancellationToken);
            if (installation is null)
            {
                installation = new ApplicationInstallation
                {
                    CatalogAppId = slug,
                    TemplateId = "managed-compose",
                    TemplateVersion = "1",
                    DisplayName = item.Name,
                    InstalledVersion = item.Version,
                    InstalledBySubject = "system:discovery"
                };
                db.ApplicationInstallations.Add(installation);
            }
            installation.DisplayName = item.Name;
            installation.InstalledVersion = item.Version;
            installation.DesiredVersion = item.Attributes.GetValueOrDefault("desiredImage", item.Version);
            installation.State = item.State;
            installation.AutoUpdateEnabled = item.Attributes.TryGetValue("autoUpdate", out var enabled) &&
                bool.TryParse(enabled, out var parsed) && parsed;
            installation.ConfigSummaryJson = JsonSerializer.Serialize(new
            {
                image = item.Version,
                composeProject = item.Attributes.GetValueOrDefault("composeProject"),
                versionPolicy = item.Attributes.GetValueOrDefault("versionPolicy", "*"),
                maintenanceWindow = item.Attributes.GetValueOrDefault("maintenanceWindow", string.Empty)
            });
            installation.UpdatedAtUtc = now;
            installation.Version++;

            var composeProject = item.Attributes.GetValueOrDefault("composeProject", $"whaledeck-app-{slug}");
            var desired = discovered.Where(candidate =>
                    candidate.Id == item.Id ||
                    candidate.Type == "Container" && candidate.Attributes.TryGetValue("com.docker.compose.project", out var project) &&
                    (string.Equals(project, composeProject, StringComparison.Ordinal) || string.Equals(project, slug, StringComparison.Ordinal)))
                .Select(candidate => resources[(candidate.Type, candidate.Id)])
                .DistinctBy(candidate => candidate.Id)
                .ToArray();
            var existingLinks = await db.ApplicationResources.Where(link => link.ApplicationId == installation.Id).ToArrayAsync(cancellationToken);
            var desiredIds = desired.Select(resource => resource.Id).ToHashSet();
            db.ApplicationResources.RemoveRange(existingLinks.Where(link => !desiredIds.Contains(link.ResourceId)));
            foreach (var resource in desired.Where(resource => existingLinks.All(link => link.ResourceId != resource.Id)))
            {
                db.ApplicationResources.Add(new ApplicationResource
                {
                    ApplicationId = installation.Id,
                    ResourceId = resource.Id,
                    Role = resource.ResourceType == "Container" ? "Container" : "ComposeProject"
                });
            }
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task SyncDockerEvents(
        PlatformDbContext db,
        IAgentGateway agent,
        CancellationToken cancellationToken)
    {
        var events = await agent.ListDockerEventsAsync(DateTimeOffset.UtcNow.AddHours(-6), 1_000, cancellationToken);
        if (events.Count == 0) return;
        var fingerprints = events.Select(item => item.Fingerprint).ToArray();
        var existing = (await db.DockerEvents.AsNoTracking()
            .Where(item => fingerprints.Contains(item.Fingerprint))
            .Select(item => item.Fingerprint)
            .ToArrayAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var item in events.Where(item => !existing.Contains(item.Fingerprint)))
        {
            db.DockerEvents.Add(new DockerEventRecord
            {
                Fingerprint = item.Fingerprint,
                OccurredAtUtc = item.OccurredAtUtc,
                EventType = item.EventType,
                Action = item.Action,
                ResourceId = item.ResourceId,
                ResourceName = item.ResourceName,
                Image = item.Image,
                AttributesJson = JsonSerializer.Serialize(item.Attributes)
            });
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task DispatchScheduledTasks(PlatformDbContext db, IAgentGateway agent, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var schedules = await db.ScheduledTasks.Where(item => item.IsEnabled && item.NextRunAtUtc != null && item.NextRunAtUtc <= now)
            .OrderBy(item => item.NextRunAtUtc).Take(20).ToArrayAsync(cancellationToken);
        foreach (var schedule in schedules)
        {
            var scheduledFor = schedule.NextRunAtUtc!.Value;
            if (await db.ScheduledTaskRuns.AnyAsync(item => item.ScheduleId == schedule.Id && item.ScheduledForUtc == scheduledFor, cancellationToken))
            {
                schedule.NextRunAtUtc = ScheduleCalculator.NextUtc(schedule.ScheduleKind, schedule.ScheduleExpression, schedule.Timezone, scheduledFor);
                continue;
            }

            using var parametersDocument = JsonDocument.Parse(schedule.ParametersJson);
            var root = parametersDocument.RootElement;
            var (area, action, defaultResource) = schedule.TaskType switch
            {
                "Backup" => ("backups", "run", string.Empty),
                "ApplicationUpdate" => ("applications", "update", string.Empty),
                "ContainerUpdate" => ("containers", "update", string.Empty),
                "SystemUpdate" => ("host", "update-install", "whaledeck.host"),
                "MetricsRollup" => ("internal", "metrics-rollup", string.Empty),
                "RetentionCleanup" => ("internal", "retention-cleanup", string.Empty),
                _ => throw new InvalidOperationException("A scheduled task type is unsupported.")
            };
            var resourceId = root.TryGetProperty("resourceId", out var resourceValue) ? resourceValue.GetString() ?? defaultResource : defaultResource;
            var parameters = root.TryGetProperty("parameters", out var parameterValue) && parameterValue.ValueKind == JsonValueKind.Object
                ? parameterValue.EnumerateObject().ToDictionary(item => item.Name, item => item.Value.ToString(), StringComparer.Ordinal)
                : new Dictionary<string, string>();
            ManagedResource? backupResource = null;
            if (schedule.TaskType == "Backup")
            {
                backupResource = await db.ManagedResources.Where(item => item.ExternalId == resourceId &&
                    (item.ResourceType == "Database" || item.ResourceType == "Container" || item.ResourceType == "ComposeApplication"))
                    .OrderByDescending(item => item.ResourceType == "Database").FirstOrDefaultAsync(cancellationToken)
                    ?? throw new InvalidOperationException("Scheduled backup resource is unavailable.");
                if (backupResource.ResourceType == "ComposeApplication")
                {
                    if (backupResource.ProtectionLevel != "Managed") throw new InvalidOperationException("Protected application backup is forbidden.");
                    area = "applications";
                    action = "backup";
                    parameters.TryAdd("consistency", "quiesced");
                }
            }
            if (schedule.TaskType is "ApplicationUpdate" or "ContainerUpdate") parameters["automatic"] = "true";
            string? planHash = null;
            if (action is "update" or "update-install")
                planHash = (await agent.PlanAsync(area, action, resourceId, parameters, cancellationToken)).PlanHash;

            var job = new OperationJob
            {
                JobType = $"{area}.{action}",
                ActorSubject = "system:scheduler",
                IdempotencyKey = $"schedule:{schedule.Id:D}:{scheduledFor.ToUnixTimeSeconds()}",
                RequestJson = JsonSerializer.Serialize(new { resourceId, parameters, planHash })
            };
            db.OperationJobs.Add(job);
            if (backupResource is not null)
                db.BackupRecords.Add(new BackupRecord { InstanceResourceId = backupResource.Id, JobId = job.Id, ExpiresAtUtc = now.AddDays(14) });
            var scheduleRun = new ScheduledTaskRun { ScheduleId = schedule.Id, JobId = job.Id, ScheduledForUtc = scheduledFor };
            db.ScheduledTaskRuns.Add(scheduleRun);
            db.AuditEvents.Add(new AuditEvent
            {
                ActorSubject = "system:scheduler",
                Action = job.JobType,
                TargetType = "schedule",
                TargetId = schedule.Id.ToString("D"),
                Result = "Accepted",
                JobId = job.Id,
                TraceId = job.Id.ToString("N")
            });
            if (area == "internal")
            {
                if (action == "metrics-rollup") await ApplyRollups(db, cancellationToken);
                if (action == "retention-cleanup") await ApplyRetention(db, cancellationToken);
                job.State = "Succeeded";
                job.Phase = "Completed";
                job.ProgressPercent = 100;
                job.StartedAtUtc = now;
                job.CompletedAtUtc = DateTimeOffset.UtcNow;
                scheduleRun.StartedAtUtc = job.StartedAtUtc;
                scheduleRun.CompletedAtUtc = job.CompletedAtUtc;
                scheduleRun.Result = job.State;
            }
            else
            {
                db.OutboxMessages.Add(new OutboxMessage { MessageType = "OperationRequested", PayloadJson = JsonSerializer.Serialize(new { jobId = job.Id }) });
            }
            schedule.NextRunAtUtc = ScheduleCalculator.NextUtc(schedule.ScheduleKind, schedule.ScheduleExpression, schedule.Timezone, scheduledFor);
            schedule.Version++;
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task DispatchBackupPolicies(PlatformDbContext db, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var policies = await db.BackupPolicies.Where(item => item.IsEnabled)
            .OrderBy(item => item.Id).Take(50).ToArrayAsync(cancellationToken);
        foreach (var policy in policies)
        {
            var resource = await db.ManagedResources.SingleOrDefaultAsync(item => item.Id == policy.InstanceResourceId, cancellationToken);
            if (resource is null) continue;
            var application = resource.ResourceType == "ComposeApplication";
            if (application && (resource.ProtectionLevel != "Managed" || !resource.ExternalId.StartsWith("application:", StringComparison.Ordinal))) continue;
            if (application)
            {
                var cleanupKey = $"application-retention:{policy.Id:D}:{now:yyyyMMdd}";
                if (!await db.OperationJobs.AnyAsync(item => item.ActorSubject == "system:backup-policy" && item.IdempotencyKey == cleanupKey, cancellationToken))
                {
                    var cleanupJob = new OperationJob { JobType = "applications.cleanup-backups", ActorSubject = "system:backup-policy",
                        ResourceId = resource.Id, IdempotencyKey = cleanupKey, RequestJson = JsonSerializer.Serialize(new {
                            resourceId = resource.ExternalId, policyId = policy.Id, policyVersion = policy.Version, parameters = new Dictionary<string, string> {
                                ["retentionDays"] = policy.RetentionDays.ToString(System.Globalization.CultureInfo.InvariantCulture),
                                ["retentionCount"] = policy.RetentionCount.ToString(System.Globalization.CultureInfo.InvariantCulture) }, planHash = (string?)null }) };
                    db.OperationJobs.Add(cleanupJob);
                    db.OutboxMessages.Add(new OutboxMessage { MessageType = "OperationRequested", PayloadJson = JsonSerializer.Serialize(new { jobId = cleanupJob.Id }) });
                    db.AuditEvents.Add(new AuditEvent { ActorSubject = cleanupJob.ActorSubject, Action = cleanupJob.JobType,
                        TargetType = "backup-policy", TargetId = policy.Id.ToString("D"), Result = "Accepted", JobId = cleanupJob.Id, TraceId = cleanupJob.Id.ToString("N") });
                }
            }
            var latest = await db.BackupRecords.Where(item => item.PolicyId == policy.Id)
                .OrderByDescending(item => item.StartedAtUtc).Select(item => (DateTimeOffset?)item.StartedAtUtc).FirstOrDefaultAsync(cancellationToken);
            var after = latest ?? policy.UpdatedAtUtc.AddMinutes(-1);
            var next = ScheduleCalculator.NextUtc("Cron", policy.ScheduleExpression, policy.Timezone, after);
            if (next > now) continue;
            var policyParameters = application ? new Dictionary<string, string> { ["consistency"] = policy.Consistency,
                ["capacityCriticalPercent"] = policy.CapacityCriticalPercent.ToString(System.Globalization.CultureInfo.InvariantCulture) } :
                new Dictionary<string, string> {
                    ["retentionDays"] = policy.RetentionDays.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["retentionCount"] = policy.RetentionCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["capacityCriticalPercent"] = policy.CapacityCriticalPercent.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["verifyAfterBackup"] = policy.VerifyAfterBackup.ToString().ToLowerInvariant() };
            var job = new OperationJob
            {
                JobType = application ? "applications.backup" : "backups.run",
                ResourceId = resource.Id,
                ActorSubject = "system:backup-policy",
                IdempotencyKey = $"backup-policy:{policy.Id:D}:{next.ToUnixTimeSeconds()}",
                RequestJson = JsonSerializer.Serialize(new
                {
                    resourceId = resource.ExternalId,
                    policyId = policy.Id,
                    policyVersion = policy.Version,
                    parameters = policyParameters,
                    planHash = (string?)null
                })
            };
            db.OperationJobs.Add(job);
            db.BackupRecords.Add(new BackupRecord
            {
                InstanceResourceId = policy.InstanceResourceId,
                PolicyId = policy.Id,
                JobId = job.Id,
                ExpiresAtUtc = now.AddDays(policy.RetentionDays)
            });
            db.OutboxMessages.Add(new OutboxMessage { MessageType = "OperationRequested", PayloadJson = JsonSerializer.Serialize(new { jobId = job.Id }) });
            db.AuditEvents.Add(new AuditEvent
            {
                ActorSubject = "system:backup-policy",
                Action = job.JobType,
                TargetType = "backup-policy",
                TargetId = policy.Id.ToString("D"),
                Result = "Accepted",
                JobId = job.Id,
                TraceId = job.Id.ToString("N")
            });
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task DispatchOutbox(
        PlatformDbContext db,
        IAgentGateway agent,
        IOneTimeSecretStore secrets,
        IIdentityDirectory identityDirectory,
        IIdentityManager identity,
        ResourceLeaseManager leases,
        string workerInstanceId,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var messages = await db.OutboxMessages.Where(item => item.ProcessedAtUtc == null && item.AvailableAtUtc <= DateTimeOffset.UtcNow)
            .OrderBy(item => item.OccurredAtUtc).Take(10).ToArrayAsync(cancellationToken);
        foreach (var message in messages)
        {
            try
            {
                if (message.MessageType == "OperationRequested")
                {
                    using var payload = JsonDocument.Parse(message.PayloadJson);
                    var jobId = payload.RootElement.GetProperty("jobId").GetGuid();
                    var job = await db.OperationJobs.SingleAsync(item => item.Id == jobId, cancellationToken);
                    if (job.CompletedAtUtc is not null)
                    {
                        message.ProcessedAtUtc = DateTimeOffset.UtcNow;
                        await db.SaveChangesAsync(cancellationToken);
                        continue;
                    }
                    if (!await HasDispatchPermissionAsync(identityDirectory, job.ActorSubject, cancellationToken))
                    {
                        await RevokeQueuedJobsAsync(db, job, cancellationToken);
                        message.ProcessedAtUtc = DateTimeOffset.UtcNow;
                        await db.SaveChangesAsync(cancellationToken);
                        continue;
                    }
                    var lockKey = GetResourceLockKey(job);
                    if (!await leases.TryAcquireAsync(lockKey, job.Id, workerInstanceId, cancellationToken))
                    {
                        message.AvailableAtUtc = DateTimeOffset.UtcNow.AddSeconds(6);
                        await db.SaveChangesAsync(cancellationToken);
                        continue;
                    }
                    if (job.CancelRequestedAtUtc is not null)
                    {
                        if (job.AgentOperationId is { } activeId)
                            await ApplyAgentState(db, job, await agent.CancelOperationAsync(activeId.ToString("D"), cancellationToken), cancellationToken);
                        else
                            await Complete(db, job, "Canceled", "Canceled", null, cancellationToken);
                    }
                    else
                    {
                        var split = job.JobType.Split('.', 2);
                        using var request = JsonDocument.Parse(job.RequestJson);
                        if (job.ActorSubject == "system:backup-policy" && request.RootElement.TryGetProperty("policyId", out var policyIdValue))
                        {
                            var policyId = policyIdValue.GetGuid();
                            var expectedVersion = request.RootElement.GetProperty("policyVersion").GetInt64();
                            if (!await db.BackupPolicies.AnyAsync(item => item.Id == policyId && item.IsEnabled && item.Version == expectedVersion, cancellationToken))
                            {
                                await Complete(db, job, "Canceled", "BackupPolicyChanged", null, cancellationToken);
                                message.ProcessedAtUtc = DateTimeOffset.UtcNow;
                                await leases.ReleaseAsync(lockKey, job.Id, workerInstanceId, cancellationToken);
                                await db.SaveChangesAsync(cancellationToken);
                                continue;
                            }
                        }
                        var resourceId = request.RootElement.TryGetProperty("resourceId", out var resource) ? resource.GetString() ?? string.Empty : string.Empty;
                        var parameters = request.RootElement.TryGetProperty("parameters", out var values) && values.ValueKind == JsonValueKind.Object
                            ? values.EnumerateObject().ToDictionary(item => item.Name, item => item.Value.ToString(), StringComparer.Ordinal)
                            : new Dictionary<string, string>();
                        var planHash = request.RootElement.TryGetProperty("planHash", out var planHashElement) && planHashElement.ValueKind == JsonValueKind.String
                            ? planHashElement.GetString()
                            : null;
                        if (job.ActorSubject is "system:backup-policy" or "system:scheduler" &&
                            job.JobType is "applications.backup" or "applications.cleanup-backups")
                            planHash = (await agent.PlanAsync(split[0], split[1], resourceId, parameters, cancellationToken)).PlanHash;
                        if (parameters.TryGetValue("inputTicket", out var inputTicket))
                        {
                            var secret = await secrets.ConsumeAsync(job.ActorSubject, inputTicket, cancellationToken)
                                ?? throw new InvalidOperationException("The staged secret expired before the operation was dispatched.");
                            var target = parameters.TryGetValue("inputKind", out var configuredTarget) ? configuredTarget : "password";
                            if (target is not ("password" or "environment")) throw new InvalidOperationException("The staged secret target is unsupported.");
                            parameters[target] = secret;
                            var successor = await secrets.StoreAsync(job.ActorSubject, secret, cancellationToken);
                            parameters["inputTicket"] = successor.Token;
                            var persistedParameters = parameters
                                .Where(item => item.Key != target)
                                .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
                            job.RequestJson = JsonSerializer.Serialize(new { resourceId, parameters = persistedParameters, planHash });
                            job.ResultJson = JsonSerializer.Serialize(new { secretToken = successor.Token, secretExpiresAtUtc = successor.ExpiresAtUtc });
                        }

                        job.State = "Running";
                        job.Phase = "DispatchingToAgent";
                        job.StartedAtUtc ??= DateTimeOffset.UtcNow;
                        // A reply can be lost or SaveChanges can conflict with a concurrent
                        // cancellation. Keep the original backup handle BEFORE sending RPC.
                        if (job.JobType is "applications.backup" or "applications.verify-backup" or "applications.cleanup-backups")
                            job.AgentOperationId = job.Id;
                        job.Version++;
                        await db.SaveChangesAsync(cancellationToken);
                        if (split[0] == "identity")
                        {
                            await identity.ExecuteAsync(split[1], resourceId, parameters, cancellationToken);
                            await Complete(db, job, "Succeeded", "Completed", null, cancellationToken, 100);
                        }
                        else
                        {
                            var operation = await agent.ExecuteAsync(split[0], split[1], resourceId, parameters, planHash, job.Id, job.IdempotencyKey, cancellationToken);
                            job.AgentOperationId = Guid.TryParse(operation.OperationId, out var operationId) ? operationId : null;
                            await ApplyAgentState(db, job, operation, cancellationToken);
                        }
                    }
                    if (job.CompletedAtUtc is not null)
                    {
                        await leases.ReleaseAsync(lockKey, job.Id, workerInstanceId, cancellationToken);
                    }
                }
                else if (message.MessageType == "OperationCancellationRequested")
                {
                    using var payload = JsonDocument.Parse(message.PayloadJson);
                    var jobId = payload.RootElement.GetProperty("jobId").GetGuid();
                    var job = await db.OperationJobs.SingleAsync(item => item.Id == jobId, cancellationToken);
                    var lockKey = GetResourceLockKey(job);
                    if (!await leases.TryAcquireAsync(lockKey, job.Id, workerInstanceId, cancellationToken))
                    {
                        message.AvailableAtUtc = DateTimeOffset.UtcNow.AddSeconds(6);
                        await db.SaveChangesAsync(cancellationToken);
                        continue;
                    }
                    if (job.CompletedAtUtc is null)
                    {
                        if (job.AgentOperationId is { } operationId)
                        {
                            var operation = await agent.CancelOperationAsync(operationId.ToString("D"), cancellationToken);
                            await ApplyAgentState(db, job, operation, cancellationToken);
                        }
                        else
                        {
                            await Complete(db, job, "Canceled", "CanceledBeforeDispatch", null, cancellationToken);
                        }
                    }
                    if (job.CompletedAtUtc is not null)
                    {
                        await leases.ReleaseAsync(lockKey, job.Id, workerInstanceId, cancellationToken);
                    }
                }
                message.ProcessedAtUtc = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception exception)
            {
                message.Attempts++;
                var failure = DispatchFailurePolicy.Classify(exception, message.Attempts);
                var terminal = failure.Terminal;
                message.LastErrorCode = failure.ErrorCode;
                if (terminal)
                {
                    message.ProcessedAtUtc = DateTimeOffset.UtcNow;
                    if (message.MessageType is "OperationRequested" or "OperationCancellationRequested")
                    {
                        using var failedPayload = JsonDocument.Parse(message.PayloadJson);
                        var failedJobId = failedPayload.RootElement.GetProperty("jobId").GetGuid();
                        var failedJob = await db.OperationJobs.SingleAsync(item => item.Id == failedJobId, cancellationToken);
                        await Complete(db, failedJob, "Failed", failure.Phase, message.LastErrorCode, cancellationToken);
                    }
                }
                else
                {
                    message.AvailableAtUtc = DateTimeOffset.UtcNow.AddSeconds(Math.Min(300, 5 * Math.Pow(2, message.Attempts)));
                }
                await db.SaveChangesAsync(cancellationToken);
                if (failure.ErrorCode == "IDENTITY_PROVIDER_REJECTED") LogIdentityProviderRejected(logger, message.Id, failure.ErrorCode, null);
                if (!terminal) LogOutboxRetry(logger, message.Id, message.Attempts, null);
            }
        }
    }

    private static async Task<bool> HasDispatchPermissionAsync(
        IIdentityDirectory identity,
        string actorSubject,
        CancellationToken cancellationToken)
    {
        if (actorSubject.StartsWith("system:", StringComparison.Ordinal)) return true;
        try
        {
            var current = await identity.ResolveCurrentAsync(actorSubject, null, [], cancellationToken);
            return current.IsAdministrator;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static async Task RevokeQueuedJobsAsync(
        PlatformDbContext db,
        OperationJob current,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var pending = await db.OperationJobs
            .Where(item => item.ActorSubject == current.ActorSubject && item.CompletedAtUtc == null &&
                           item.State == "Queued" && item.Id != current.Id)
            .ToArrayAsync(cancellationToken);
        foreach (var job in pending)
        {
            job.CancelRequestedAtUtc ??= now;
            job.Version++;
        }
        db.AuditEvents.Add(new AuditEvent
        {
            ActorSubject = current.ActorSubject,
            Action = "permission.revalidated",
            TargetType = "Job",
            TargetId = current.Id.ToString("D"),
            Result = "Denied",
            JobId = current.Id,
            TraceId = "worker",
            DetailJson = JsonSerializer.Serialize(new { reason = "administrator-permission-revoked", queuedJobsCanceled = pending.Length })
        });
        await Complete(db, current, "Canceled", "PermissionRevoked", "PERMISSION_REVOKED", cancellationToken);
    }

    private static async Task ReconcileAgentOperations(
        PlatformDbContext db,
        IAgentGateway agent,
        ResourceLeaseManager leases,
        string workerInstanceId,
        CancellationToken cancellationToken)
    {
        var jobs = await db.OperationJobs
            .Where(item => item.AgentOperationId != null && item.CompletedAtUtc == null)
            .OrderBy(item => item.StartedAtUtc)
            .Take(50)
            .ToArrayAsync(cancellationToken);

        foreach (var job in jobs)
        {
            var lockKey = GetResourceLockKey(job);
            if (!await leases.TryAcquireAsync(lockKey, job.Id, workerInstanceId, cancellationToken)) continue;
            var operationId = job.AgentOperationId!.Value.ToString("D");
            var operation = job.CancelRequestedAtUtc is not null
                ? await agent.CancelOperationAsync(operationId, cancellationToken)
                : await agent.GetOperationAsync(operationId, cancellationToken);
            await ApplyAgentState(db, job, operation, cancellationToken);
            if (job.CompletedAtUtc is not null)
            {
                await leases.ReleaseAsync(lockKey, job.Id, workerInstanceId, cancellationToken);
            }
        }
    }


    private static string GetResourceLockKey(OperationJob job)
    {
        using var request = JsonDocument.Parse(job.RequestJson);
        var resourceId = request.RootElement.TryGetProperty("resourceId", out var resource) && resource.ValueKind == JsonValueKind.String
            ? resource.GetString()
            : null;
        var area = job.JobType.Split('.', 2)[0];
        if (area is "applications" or "compose-projects")
        {
            var slug = resourceId?.StartsWith("application:", StringComparison.Ordinal) == true ? resourceId["application:".Length..] :
                request.RootElement.TryGetProperty("parameters", out var values) && values.TryGetProperty("slug", out var slugValue) ? slugValue.GetString() : null;
            // Compose controls and application updates must serialize against
            // the same installation, even though their REST areas differ.
            area = "applications";
            resourceId = slug ?? resourceId;
        }
        var identity = $"{area}:{resourceId ?? "global"}";
        return $"{area}:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))}";
    }

    private static Task ApplyAgentState(
        PlatformDbContext db,
        OperationJob job,
        AgentOperationDto operation,
        CancellationToken cancellationToken)
    {
        var transition = AgentOperationStateMapper.Map(operation);
        if (!string.IsNullOrWhiteSpace(operation.ResultJson)) job.ResultJson = operation.ResultJson;
        return Complete(db, job, transition.State, transition.Phase, transition.ErrorCode, cancellationToken, transition.ProgressPercent);
    }

    private static async Task Complete(
        PlatformDbContext db,
        OperationJob job,
        string state,
        string phase,
        string? errorCode,
        CancellationToken cancellationToken,
        short? progressPercent = null)
    {
        if (job.State == state && job.Phase == phase && job.ProgressPercent == progressPercent && job.ErrorCode == errorCode)
        {
            return;
        }
        job.State = state;
        job.Phase = phase;
        job.ErrorCode = errorCode;
        job.ProgressPercent = state == "Succeeded" ? (short)100 : progressPercent;
        if (state is "Succeeded" or "Failed" or "Canceled") job.CompletedAtUtc = DateTimeOffset.UtcNow;
        job.Version++;
        var sequence = await db.OperationJobEvents.Where(item => item.JobId == job.Id).MaxAsync(item => (long?)item.Sequence, cancellationToken) ?? 0;
        db.OperationJobEvents.Add(new OperationJobEvent { JobId = job.Id, Sequence = sequence + 1, State = state, Phase = phase, ProgressPercent = job.ProgressPercent, MessageCode = $"JOB_{state.ToUpperInvariant()}" });
        var backup = await db.BackupRecords.SingleOrDefaultAsync(item => item.JobId == job.Id, cancellationToken);
        if (backup is not null && state is "Succeeded" or "Failed" or "Canceled")
        {
            backup.Status = state;
            backup.CompletedAtUtc = DateTimeOffset.UtcNow;
            backup.ErrorCode = errorCode;
            if (state == "Succeeded" && TryReadBackupResult(job.ResultJson, out var backupResult))
            {
                backup.RelativePath = backupResult.RelativePath;
                backup.SizeBytes = backupResult.SizeBytes;
                backup.ChecksumAlgorithm = backupResult.ChecksumAlgorithm;
                backup.Checksum = backupResult.Checksum;
                backup.VerifiedAtUtc = backupResult.Verified ? DateTimeOffset.UtcNow : null;
            }
            if (state == "Succeeded" && job.JobType == "applications.backup" && backup.PolicyId is { } policyId)
            {
                var policy = await db.BackupPolicies.SingleOrDefaultAsync(item => item.Id == policyId && item.IsEnabled, cancellationToken);
                var resource = await db.ManagedResources.SingleOrDefaultAsync(item => item.Id == backup.InstanceResourceId, cancellationToken);
                if (policy is not null && resource is not null && resource.ProtectionLevel == "Managed")
                {
                    var cleanupJob = new OperationJob { JobType = "applications.cleanup-backups", ActorSubject = "system:backup-policy",
                        ResourceId = resource.Id, IdempotencyKey = "application-retention-after:" + job.Id.ToString("D"),
                        RequestJson = JsonSerializer.Serialize(new { resourceId = resource.ExternalId, policyId = policy.Id, policyVersion = policy.Version, parameters = new Dictionary<string, string> {
                            ["retentionDays"] = policy.RetentionDays.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            ["retentionCount"] = policy.RetentionCount.ToString(System.Globalization.CultureInfo.InvariantCulture) }, planHash = (string?)null }) };
                    db.OperationJobs.Add(cleanupJob);
                    db.OutboxMessages.Add(new OutboxMessage { MessageType = "OperationRequested", PayloadJson = JsonSerializer.Serialize(new { jobId = cleanupJob.Id }) });
                    db.AuditEvents.Add(new AuditEvent { ActorSubject = cleanupJob.ActorSubject, Action = cleanupJob.JobType,
                        TargetType = "backup-policy", TargetId = policy.Id.ToString("D"), Result = "Accepted", JobId = cleanupJob.Id, TraceId = cleanupJob.Id.ToString("N") });
                }
            }
        }
        if (job.JobType == "applications.verify-backup" && state is "Succeeded" or "Failed")
        {
            using var verificationRequest = JsonDocument.Parse(job.RequestJson);
            var parameters = verificationRequest.RootElement.GetProperty("parameters");
            var resource = verificationRequest.RootElement.GetProperty("resourceId").GetString();
            if (parameters.TryGetProperty("backupId", out var backupId) && Guid.TryParseExact(backupId.GetString(), "N", out var originalJob))
            {
                var original = await db.BackupRecords.SingleOrDefaultAsync(item => item.JobId == originalJob &&
                    db.ManagedResources.Any(managed => managed.Id == item.InstanceResourceId && managed.ExternalId == resource), cancellationToken);
                if (original is not null)
                {
                    original.VerifiedAtUtc = state == "Succeeded" ? DateTimeOffset.UtcNow : null;
                    original.ErrorCode = state == "Succeeded" ? null : "APPLICATION_BACKUP_VERIFICATION_FAILED";
                }
            }
        }
        var scheduleRun = await db.ScheduledTaskRuns.SingleOrDefaultAsync(item => item.JobId == job.Id, cancellationToken);
        if (job.JobType == "applications.cleanup-backups" && state is "Succeeded" or "Failed" or "Canceled" && !string.IsNullOrWhiteSpace(job.ResultJson))
        {
            using var cleanupRequest = JsonDocument.Parse(job.RequestJson);
            using var cleanupResult = JsonDocument.Parse(job.ResultJson);
            if (cleanupResult.RootElement.TryGetProperty("removedBackupIds", out var removed) && removed.ValueKind == JsonValueKind.Array)
            {
                var resource = cleanupRequest.RootElement.GetProperty("resourceId").GetString();
                var removedJobs = removed.EnumerateArray().Take(1024).Select(item =>
                    Guid.TryParseExact(item.GetString(), "N", out var parsed) ? (Guid?)parsed : null)
                    .Where(item => item.HasValue).Select(item => item!.Value).Distinct().ToArray();
                var expired = await db.BackupRecords.Where(item => removedJobs.Contains(item.JobId) &&
                    db.ManagedResources.Any(managed => managed.Id == item.InstanceResourceId && managed.ExternalId == resource)).ToArrayAsync(cancellationToken);
                foreach (var record in expired) record.Status = "Expired";
            }
        }
        if (scheduleRun is not null && state is "Succeeded" or "Failed" or "Canceled")
        {
            scheduleRun.Result = state;
            scheduleRun.CompletedAtUtc = DateTimeOffset.UtcNow;
            scheduleRun.StartedAtUtc ??= job.StartedAtUtc;
        }
        await ProjectApplicationOperation(db, job, state, errorCode, cancellationToken);
        await ProjectContainerUpdate(db, job, state, errorCode, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task ProjectContainerUpdate(
        PlatformDbContext db,
        OperationJob job,
        string state,
        string? errorCode,
        CancellationToken cancellationToken)
    {
        if (job.JobType != "containers.update" || state is not ("Succeeded" or "Failed" or "Canceled") ||
            await db.ContainerUpdateRuns.AnyAsync(item => item.JobId == job.Id, cancellationToken)) return;

        using var request = JsonDocument.Parse(job.RequestJson);
        var resourceId = request.RootElement.TryGetProperty("resourceId", out var resource) ? resource.GetString() ?? string.Empty : string.Empty;
        var result = ReadJsonObject(job.ResultJson);
        db.ContainerUpdateRuns.Add(new ContainerUpdateRun
        {
            ExternalContainerId = ReadString(result, "containerId") ?? resourceId,
            ContainerName = ReadString(result, "containerName") ?? resourceId,
            Image = ReadString(result, "image") ?? "unknown",
            OldImageDigest = ReadString(result, "oldImageId"),
            NewImageDigest = ReadString(result, "imageId"),
            VersionPolicy = ReadString(result, "versionPolicy"),
            JobId = job.Id,
            StartedAtUtc = job.StartedAtUtc ?? job.CreatedAtUtc,
            CompletedAtUtc = job.CompletedAtUtc ?? DateTimeOffset.UtcNow,
            Result = state,
            WasRolledBack = ReadBoolean(result, "rolledBack"),
            ErrorSummary = errorCode
        });
    }

    private static async Task ProjectApplicationOperation(
        PlatformDbContext db,
        OperationJob job,
        string state,
        string? errorCode,
        CancellationToken cancellationToken)
    {
        if (!job.JobType.StartsWith("applications.", StringComparison.Ordinal)) return;
        var action = job.JobType["applications.".Length..];
        if (action is "backup" or "verify-backup" or "cleanup-backups") return;
        using var request = JsonDocument.Parse(job.RequestJson);
        var parameters = request.RootElement.TryGetProperty("parameters", out var values) && values.ValueKind == JsonValueKind.Object
            ? values.EnumerateObject().ToDictionary(item => item.Name, item => item.Value.ToString(), StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal);
        if (!parameters.TryGetValue("slug", out var slug) || string.IsNullOrWhiteSpace(slug)) return;

        var installation = await db.ApplicationInstallations.SingleOrDefaultAsync(item => item.CatalogAppId == slug, cancellationToken);
        if (installation is null && state == "Canceled" && job.StartedAtUtc is null) return;
        if (installation is null)
        {
            var image = parameters.GetValueOrDefault("image", "unknown");
            installation = new ApplicationInstallation
            {
                CatalogAppId = slug,
                TemplateId = parameters.GetValueOrDefault("templateId", "managed-compose"),
                TemplateVersion = parameters.GetValueOrDefault("templateVersion", "1"),
                DisplayName = parameters.GetValueOrDefault("displayName", slug),
                InstalledVersion = image,
                DesiredVersion = image,
                InstalledBySubject = job.ActorSubject
            };
            db.ApplicationInstallations.Add(installation);
        }

        var terminal = state is "Succeeded" or "Failed" or "Canceled";
        var result = ReadJsonObject(job.ResultJson);
        if (action == "configure")
        {
            if (state == "Succeeded")
            {
                installation.State = ReadString(result, "state") ?? installation.State;
                installation.AutoUpdateEnabled = ReadBoolean(result, "autoUpdate");
                installation.DesiredVersion = ReadString(result, "desiredImage") ?? installation.DesiredVersion;
                if (ReadBoolean(result, "applied")) installation.InstalledVersion = ReadString(result, "actualImage") ?? installation.InstalledVersion;
                var summary = System.Text.Json.Nodes.JsonNode.Parse(installation.ConfigSummaryJson)?.AsObject() ?? new System.Text.Json.Nodes.JsonObject();
                summary["desiredImage"] = installation.DesiredVersion;
                summary["autoUpdate"] = installation.AutoUpdateEnabled;
                summary["versionPolicy"] = ReadString(result, "versionPolicy") ?? "*";
                summary["maintenanceWindow"] = ReadString(result, "maintenanceWindow");
                summary["configurationSaved"] = true;
                summary["applied"] = ReadBoolean(result, "applied");
                installation.ConfigSummaryJson = summary.ToJsonString();
                installation.UpdatedAtUtc = DateTimeOffset.UtcNow;
                installation.Version++;
            }
            return;
        }
        var rolledBack = ReadBoolean(result, "rolledBack");
        installation.State = terminal
            ? state switch
            {
                "Succeeded" when action == "stop" => "Stopped",
                "Succeeded" when action == "uninstall" => "Uninstalled",
                "Succeeded" => "Running",
                "Failed" when rolledBack => "Running",
                "Failed" when action == "install" => "Failed",
                _ => installation.State
            }
            : action switch
            {
                "install" => "Installing",
                "update" or "reinstall" => "Updating",
                "stop" => "Stopping",
                "start" or "restart" => "Starting",
                "uninstall" => "Uninstalling",
                _ => installation.State
            };
        installation.AutoUpdateEnabled = parameters.TryGetValue("autoupdate", out var autoUpdate) && bool.TryParse(autoUpdate, out var enabled)
            ? enabled
            : ReadBoolean(result, "autoUpdate") || installation.AutoUpdateEnabled;
        installation.DesiredVersion = parameters.TryGetValue("image", out var desiredImage) ? desiredImage : installation.DesiredVersion;
        installation.InstalledVersion = ReadString(result, "version") ?? ReadString(result, "imageId") ?? installation.InstalledVersion;
        installation.ConfigSummaryJson = JsonSerializer.Serialize(new
        {
            image = parameters.GetValueOrDefault("image") ?? ReadString(result, "image"),
            ports = parameters.GetValueOrDefault("ports"),
            autoUpdate = installation.AutoUpdateEnabled,
            versionPolicy = parameters.GetValueOrDefault("versionRange") ?? ReadString(result, "versionPolicy") ?? "*",
            maintenanceWindow = parameters.GetValueOrDefault("maintenanceWindow", "Sun@20:00-23:59"),
            dependencies = parameters.GetValueOrDefault("dependencies")
        });
        installation.UpdatedAtUtc = DateTimeOffset.UtcNow;
        installation.Version++;

        if (terminal && action is "update" or "reinstall" &&
            !await db.ApplicationUpdateRuns.AnyAsync(item => item.JobId == job.Id, cancellationToken))
        {
            var planHash = request.RootElement.TryGetProperty("planHash", out var plan) && plan.ValueKind == JsonValueKind.String
                ? plan.GetString() ?? string.Empty
                : string.Empty;
            db.ApplicationUpdateRuns.Add(new ApplicationUpdateRun
            {
                ApplicationId = installation.Id,
                OldImageDigest = ReadString(result, "oldImageId"),
                NewImageDigest = ReadString(result, "imageId") ?? parameters.GetValueOrDefault("image", installation.InstalledVersion),
                VersionPolicy = ReadString(result, "versionPolicy") ?? parameters.GetValueOrDefault("versionRange"),
                PlanHash = planHash,
                JobId = job.Id,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                Result = state,
                WasRolledBack = rolledBack,
                ErrorSummary = errorCode
            });
        }
        if (terminal && state == "Succeeded" && action == "uninstall")
        {
            var links = await db.ApplicationResources.Where(item => item.ApplicationId == installation.Id).ToArrayAsync(cancellationToken);
            db.ApplicationResources.RemoveRange(links);
        }
    }

    private static JsonElement? ReadJsonObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.Clone() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadString(JsonElement? element, string property) =>
        element is { } value && value.TryGetProperty(property, out var item) && item.ValueKind == JsonValueKind.String
            ? item.GetString()
            : null;

    private static bool ReadBoolean(JsonElement? element, string property) =>
        element is { } value && value.TryGetProperty(property, out var item) && item.ValueKind is JsonValueKind.True or JsonValueKind.False && item.GetBoolean();

    private static bool TryReadBackupResult(string? json, out BackupResult result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!(root.TryGetProperty("primaryRelativePath", out var path) || root.TryGetProperty("relativePath", out path)) ||
                !(root.TryGetProperty("totalSizeBytes", out var size) || root.TryGetProperty("sizeBytes", out size)) ||
                !root.TryGetProperty("checksumAlgorithm", out var algorithm) ||
                !root.TryGetProperty("checksum", out var checksum)) return false;
            result = new BackupResult(
                path.GetString(),
                size.GetInt64(),
                algorithm.GetString(),
                checksum.GetString(),
                root.TryGetProperty("verified", out var verified) && verified.GetBoolean());
            return !string.IsNullOrWhiteSpace(result.RelativePath) &&
                   !string.IsNullOrWhiteSpace(result.ChecksumAlgorithm) &&
                   !string.IsNullOrWhiteSpace(result.Checksum);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private readonly record struct BackupResult(
        string? RelativePath,
        long SizeBytes,
        string? ChecksumAlgorithm,
        string? Checksum,
        bool Verified);

    private static async Task ApplyRetention(PlatformDbContext db, CancellationToken cancellationToken)
    {
        var metricCutoff = DateTimeOffset.UtcNow.AddDays(-14);
        var eventCutoff = DateTimeOffset.UtcNow.AddDays(-90);
        await MetricPartitions.MaintainAsync(db, DateTimeOffset.UtcNow, cancellationToken);
        await db.OperationJobEvents.Where(item => item.OccurredAtUtc < eventCutoff).ExecuteDeleteAsync(cancellationToken);
        await db.DockerEvents.Where(item => item.OccurredAtUtc < metricCutoff).ExecuteDeleteAsync(cancellationToken);
    }

    private static Task<int> ApplyRollups(PlatformDbContext db, CancellationToken cancellationToken) =>
        MetricAggregation.ApplyAsync(db, DateTimeOffset.UtcNow, cancellationToken);
}
