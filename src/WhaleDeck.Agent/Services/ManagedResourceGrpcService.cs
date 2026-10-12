using System.Text.Json;
using System.Text.Json.Nodes;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Agent.Services;

public sealed class ManagedResourceGrpcService(
    ResourceRegistry registry,
    OperationStore operations,
    PlanStore plans,
    ManagedResourcePlanner planner,
    OperationCoordinator coordinator,
    ManagedActionExecutor executor,
    DockerEngine docker,
    BoundedProcessRunner processes,
    ComposeProjectManager composeProjects,
    ApplicationConfigurationManager applicationConfiguration,
    ApplicationBackupManager applicationBackups)
    : ManagedResourceService.ManagedResourceServiceBase
{
    private const string PrivilegedHelper = "/usr/local/libexec/whaledeck-privileged";
    private static readonly string[] EditableDockerSettings =
        ["registry-mirrors", "log-driver", "log-opts", "live-restore", "features", "dns", "proxies"];

    public override async Task<PlanResponse> PlanAction(RegisteredActionRequest request, ServerCallContext context)
    {
        if (request.Resource.ResourceType == "applications" && request.Action is "backup" or "verify-backup" or "cleanup-backups")
        {
            var backupPreview = await applicationBackups.PreviewAsync(request, context.CancellationToken);
            var backupPlan = plans.Create(request.Resource.ResourceId, request.Action, request.Parameters, backupPreview.Fingerprint);
            backupPlan.Changes.Clear();
            backupPlan.Changes.AddRange(backupPreview.Changes);
            backupPlan.Warnings.Add(backupPreview.Warning);
            return backupPlan;
        }
        if (request.Resource.ResourceType == "applications" && request.Action == "configure")
        {
            var candidate = await applicationConfiguration.PreviewAsync(request, context.CancellationToken);
            var configurationPlan = plans.Create(request.Resource.ResourceId, request.Action,
                ManagedResourcePlanner.SanitizeParameters(request.Parameters), candidate.Fingerprint);
            configurationPlan.Changes.Clear();
            configurationPlan.Changes.AddRange(candidate.Candidate.Changes);
            configurationPlan.Changes.Add(candidate.Candidate.ApplyImmediately ? "Save configuration and redeploy services" : "Save configuration only; running services are unchanged");
            configurationPlan.Warnings.Add("Existing secrets are not displayed. Data is retained; failed dev edits are repaired by a new confirmed edit, not automatic rollback.");
            return configurationPlan;
        }
        if (request.Resource.ResourceType == "applications" && request.Action != "install")
        {
            var lifecyclePreview = await PreviewApplicationLifecycleAsync(request, context.CancellationToken);
            var lifecyclePlan = plans.Create(request.Resource.ResourceId, request.Action,
                ManagedResourcePlanner.SanitizeParameters(request.Parameters), lifecyclePreview.Fingerprint);
            lifecyclePlan.Changes.Clear();
            lifecyclePlan.Changes.Add($"{request.Action}: {lifecyclePreview.Project}; current configuration and members bound");
            lifecyclePlan.Changes.AddRange(lifecyclePreview.Changes);
            if (request.Action == "uninstall") lifecyclePlan.Warnings.Add("Bind data and configuration are retained; named volumes are removed only when explicitly requested.");
            return lifecyclePlan;
        }
        if (request.Resource.ResourceType != "compose-projects") return planner.Create(request);
        var preview = await composeProjects.PreviewAsync(request, context.CancellationToken);
        var response = plans.Create(request.Resource.ResourceId, request.Action, request.Parameters, preview.Fingerprint);
        response.Changes.Clear();
        response.Changes.Add($"{request.Action}: {preview.Project}; source: compose.yml; environment: .env (redacted)");
        response.Changes.AddRange(preview.Changes);
        response.Warnings.Add("Existing volumes, configuration and orphan services are retained. Pull does not restart services; redeploy does not pull images.");
        return response;
    }

    public override Task<ApplicationConfigurationResponse> GetApplicationConfiguration(RegisteredResourceRequest request, ServerCallContext context) =>
        applicationConfiguration.ReadAsync(request.Resource.ResourceId, context.CancellationToken);

    public override async Task<ResourceCollectionResponse> ListDatabaseInstances(Empty request, ServerCallContext context)
    {
        var containers = await docker.ListContainersAsync(true, context.CancellationToken);
        var response = new ResourceCollectionResponse();
        foreach (var resource in registry.All.Where(item => item.Id.StartsWith("database-platform.", StringComparison.Ordinal) && item.Type == "Container"))
        {
            var container = containers.FirstOrDefault(item => item.Names.Any(name => string.Equals(name.TrimStart('/'), resource.ExternalId, StringComparison.Ordinal)));
            var snapshot = ToSnapshot(resource);
            snapshot.State = container?.State ?? "Missing";
            snapshot.Version = container?.Image ?? string.Empty;
            snapshot.Attributes["status"] = container?.Status ?? "Not found";
            response.Resources.Add(snapshot);
        }
        return response;
    }

    public override async Task<ResourceCollectionResponse> ListSystemdUnits(Empty request, ServerCallContext context)
    {
        var response = new ResourceCollectionResponse();
        foreach (var resource in registry.All.Where(item => item.Type == "SystemdUnit"))
        {
            var snapshot = ToSnapshot(resource);
            try
            {
                var status = await processes.RunAsync("/usr/bin/systemctl",
                    ["show", resource.ExternalId, "--property=ActiveState", "--property=SubState", "--property=UnitFileState", "--value"],
                    null, TimeSpan.FromSeconds(10), "SYSTEMD_QUERY_FAILED", context.CancellationToken);
                var values = status.StandardOutput.Split('\n', StringSplitOptions.TrimEntries);
                snapshot.State = values.ElementAtOrDefault(0) ?? "Unknown";
                snapshot.Version = values.ElementAtOrDefault(2) ?? string.Empty;
                snapshot.Attributes["subState"] = values.ElementAtOrDefault(1) ?? string.Empty;
            }
            catch (InvalidOperationException)
            {
                snapshot.State = "Unknown";
            }
            response.Resources.Add(snapshot);
        }
        return response;
    }

    public override async Task<ResourceCollectionResponse> ListComposeProjects(Empty request, ServerCallContext context)
    {
        var response = new ResourceCollectionResponse();
        var root = registry.All.SingleOrDefault(item => item.Id == "whaledeck.apps")?.Path;
        var containers = await docker.ListContainersAsync(true, context.CancellationToken);
        var managedProjects = new HashSet<string>(StringComparer.Ordinal);
        foreach (var directory in root is not null && Directory.Exists(root)
                     ? Directory.EnumerateDirectories(root).Order(StringComparer.Ordinal).Take(256) : [])
        {
            var manifestPath = Path.Combine(directory, "manifest.json");
            if (!File.Exists(manifestPath)) continue;
            try
            {
                var slug = Path.GetFileName(directory);
                ComposeProjectManager.ResolveDirectory(root!, slug);
                using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
                var image = manifest.RootElement.TryGetProperty("image", out var imageValue) ? imageValue.GetString() ?? string.Empty : string.Empty;
                var project = $"whaledeck-app-{slug}";
                var members = ComposeProjectManager.Members(containers, project);
                var state = ComposeProjectManager.State(members);
                var protectedResource = members.Any(item => item.Names.Any(name => registry.IsProtectedContainer(name, item.Labels)));
                var snapshot = new ResourceSnapshot
                {
                    ResourceId = $"application:{slug}",
                    DisplayName = slug,
                    ResourceType = "ComposeApplication",
                    State = state,
                    Version = members.FirstOrDefault()?.Image ?? image,
                    ProtectedResource = protectedResource
                };
                snapshot.Attributes["composeProject"] = project;
                snapshot.Attributes["ports"] = string.Join(", ", members.SelectMany(item => DockerGrpcService.BuildContainerSummary(item, registry).Ports).Distinct(StringComparer.Ordinal).Take(32));
                snapshot.Attributes["containerCount"] = members.Length.ToString(System.Globalization.CultureInfo.InvariantCulture);
                snapshot.Attributes["autoUpdate"] = manifest.RootElement.TryGetProperty("autoUpdate", out var autoUpdate) && autoUpdate.GetBoolean() ? "true" : "false";
                snapshot.Attributes["versionPolicy"] = manifest.RootElement.TryGetProperty("versionPolicy", out var versionPolicy) ? versionPolicy.GetString() ?? "*" : "*";
                snapshot.Attributes["desiredImage"] = image;
                snapshot.Attributes["maintenanceWindow"] = manifest.RootElement.TryGetProperty("maintenanceWindow", out var maintenanceWindow) ? maintenanceWindow.GetString() ?? string.Empty : string.Empty;
                snapshot.Attributes["configurationSource"] = "compose.yml";
                snapshot.Attributes["workingDirectory"] = directory;
                snapshot.Attributes["services"] = string.Join(',', members.Select(item => ComposeProjectManager.Label(item, "com.docker.compose.service", "unknown")).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
                snapshot.Attributes["allowedActions"] = protectedResource ? string.Empty : string.Join(',', ComposeProjectManager.Actions);
                managedProjects.Add(project);
                response.Resources.Add(snapshot);
            }
            catch (JsonException)
            {
                // Invalid manifests stay invisible to normal management and are handled by diagnostics.
            }
            catch (InvalidOperationException) { /* Unapproved installations remain non-manageable. */ }
        }
        foreach (var project in containers.Where(item => item.Labels.ContainsKey("com.docker.compose.project"))
                     .GroupBy(item => item.Labels["com.docker.compose.project"], StringComparer.Ordinal)
                     .Where(group => !managedProjects.Contains(group.Key)).OrderBy(group => group.Key, StringComparer.Ordinal).Take(256))
        {
            var snapshot = new ResourceSnapshot
            {
                ResourceId = "compose:" + project.Key, DisplayName = project.Key, ResourceType = "ComposeProject",
                State = ComposeProjectManager.State(project.ToArray()),
                Version = string.Join(", ", project.Select(item => item.Image).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Take(16)),
                ProtectedResource = project.Any(item => item.Names.Any(name => registry.IsProtectedContainer(name, item.Labels)))
            };
            snapshot.Attributes["containerCount"] = project.Count().ToString(System.Globalization.CultureInfo.InvariantCulture);
            snapshot.Attributes["composeProject"] = project.Key;
            snapshot.Attributes["ports"] = string.Join(", ", project.SelectMany(item => DockerGrpcService.BuildContainerSummary(item, registry).Ports).Distinct(StringComparer.Ordinal).Take(32));
            snapshot.Attributes["services"] = string.Join(',', project.Select(item => ComposeProjectManager.Label(item, "com.docker.compose.service", "unknown")).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
            snapshot.Attributes["configurationSource"] = "Unregistered (read-only)";
            snapshot.Attributes["allowedActions"] = string.Empty;
            response.Resources.Add(snapshot);
        }
        return response;
    }

    public override async Task<ResourceSnapshot> GetConfigRepositoryStatus(RegisteredResourceRequest request, ServerCallContext context)
    {
        var resource = registry.Require(request.Resource.ResourceId, "status");
        var result = await processes.RunAsync(
            "/usr/bin/sudo",
            ["-n", PrivilegedHelper, "config-repository-status", resource.Path!],
            null,
            TimeSpan.FromMinutes(2),
            "CONFIG_REPOSITORY_STATUS_FAILED",
            context.CancellationToken);
        using var document = JsonDocument.Parse(result.StandardOutput);
        var root = document.RootElement;
        var branch = root.GetProperty("branch").GetString() ?? string.Empty;
        var commit = root.GetProperty("commit").GetString() ?? string.Empty;
        var dirty = root.GetProperty("dirty").GetBoolean();
        var response = ToSnapshot(resource);
        response.State = dirty ? "Dirty" : "Clean";
        response.Version = commit;
        response.Attributes["branch"] = branch;
        return response;
    }

    public override async Task<DockerSettingsResponse> GetDockerSettings(Empty request, ServerCallContext context)
    {
        var source = JsonNode.Parse(await File.ReadAllTextAsync("/etc/docker/daemon.json", context.CancellationToken))?.AsObject()
            ?? throw new RpcException(new Status(StatusCode.FailedPrecondition, "Docker settings are not a JSON object."));
        var editable = new JsonObject();
        foreach (var key in EditableDockerSettings)
        {
            if (source.TryGetPropertyValue(key, out var value)) editable[key] = value?.DeepClone();
        }
        var response = new DockerSettingsResponse
        {
            SettingsJson = editable.ToJsonString(new JsonSerializerOptions { WriteIndented = true })
        };
        response.EditableKeys.AddRange(EditableDockerSettings);
        return response;
    }

    public override Task<OperationHandle> RunDatabaseAction(RegisteredActionRequest request, ServerCallContext context) =>
        Task.FromResult(QueueRegistered(request, "Database"));

    public override Task<OperationHandle> RunSystemdAction(RegisteredActionRequest request, ServerCallContext context) =>
        Task.FromResult(QueueRegistered(request, "Systemd"));

    public override async Task<OperationHandle> RunComposeAction(RegisteredActionRequest request, ServerCallContext context)
    {
        if (request.Resource.ResourceType == "applications" && request.Action is "backup" or "verify-backup" or "cleanup-backups")
        {
            var backupKey = BuildIdempotencyKey(request.Context, "ApplicationBackup", request.Action, request.Resource.ResourceId);
            if (!Guid.TryParse(request.Context.JobId, out var backupJobId))
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Application backup requires a persisted job ID."));
            var backupOperation = operations.GetOrCreate(backupKey, "ApplicationBackup:" + request.Action, out var backupCreated, backupJobId);
            if (!backupCreated) return backupOperation;
            try
            {
                var backupPreview = await applicationBackups.PreviewAsync(request, context.CancellationToken);
                plans.VerifyAndConsume(request.PlanHash, request.Resource.ResourceId, request.Action, request.Parameters, backupPreview.Fingerprint);
                coordinator.Start(backupOperation, "APPLICATION_BACKUP_FAILED", token => applicationBackups.ExecuteAsync(request, backupPreview, backupOperation, token),
                    completionWinsCancellation: true);
                return backupOperation;
            }
            catch
            {
                backupOperation.State = OperationState.Failed;
                backupOperation.Phase = "ValidationFailed";
                backupOperation.ErrorCode = "APPLICATION_BACKUP_PLAN_INVALID";
                operations.Save(backupOperation);
                throw;
            }
        }
        if (request.Resource.ResourceType == "applications" && request.Action == "configure")
        {
            var configurationKey = BuildIdempotencyKey(request.Context, "ApplicationConfiguration", request.Action, request.Resource.ResourceId);
            var configurationOperation = operations.GetOrCreate(configurationKey, "ApplicationConfiguration:configure", out var configurationCreated);
            if (!configurationCreated) return configurationOperation;
            try
            {
                var candidate = await applicationConfiguration.PreviewAsync(request, context.CancellationToken);
                plans.VerifyAndConsume(request.PlanHash, request.Resource.ResourceId, request.Action,
                    ManagedResourcePlanner.SanitizeParameters(request.Parameters), candidate.Fingerprint);
                coordinator.Start(configurationOperation, "APPLICATION_CONFIGURATION_FAILED",
                    token => applicationConfiguration.ExecuteAsync(request, candidate, token));
                return configurationOperation;
            }
            catch
            {
                configurationOperation.State = OperationState.Failed;
                configurationOperation.Phase = "ValidationFailed";
                configurationOperation.ErrorCode = "APPLICATION_CONFIGURATION_PLAN_INVALID";
                operations.Save(configurationOperation);
                throw;
            }
        }
        if (request.Resource.ResourceType == "applications" && request.Action != "install")
        {
            var lifecycleKey = BuildIdempotencyKey(request.Context, "ApplicationLifecycle", request.Action, request.Parameters.GetValueOrDefault("slug", ""));
            var lifecycleOperation = operations.GetOrCreate(lifecycleKey, "ApplicationLifecycle:" + request.Action, out var lifecycleCreated);
            if (!lifecycleCreated) return lifecycleOperation;
            try
            {
                var lifecyclePreview = await PreviewApplicationLifecycleAsync(request, context.CancellationToken);
                if (ManagedResourcePlanner.RequiresPlan(request.Action)) plans.VerifyAndConsume(request.PlanHash,
                    request.Resource.ResourceId, request.Action, ManagedResourcePlanner.SanitizeParameters(request.Parameters), lifecyclePreview.Fingerprint);
                coordinator.Start(lifecycleOperation, "APPLICATION_LIFECYCLE_FAILED", async token =>
                {
                    if ((await PreviewApplicationLifecycleAsync(request, token)).Fingerprint != lifecyclePreview.Fingerprint)
                        throw new InvalidOperationException("Application changed after lifecycle confirmation.");
                    return await executor.ExecuteAsync("Compose", registry.Require("whaledeck.apps", request.Action), request.Action, request.Parameters, token);
                });
                return lifecycleOperation;
            }
            catch
            {
                lifecycleOperation.State = OperationState.Failed;
                lifecycleOperation.Phase = "ValidationFailed";
                lifecycleOperation.ErrorCode = "APPLICATION_LIFECYCLE_PLAN_INVALID";
                operations.Save(lifecycleOperation);
                throw;
            }
        }
        if (request.Resource.ResourceType != "compose-projects") return QueueRegistered(request, "Compose");
        var key = BuildIdempotencyKey(request.Context, "ComposeProject", request.Action, request.Resource.ResourceId);
        var operation = operations.GetOrCreate(key, $"ComposeProject:{request.Action}", out var created);
        if (!created) return operation;
        try
        {
            var preview = await composeProjects.PreviewAsync(request, context.CancellationToken);
            plans.VerifyAndConsume(request.PlanHash, request.Resource.ResourceId, request.Action, request.Parameters, preview.Fingerprint);
            coordinator.Start(operation, "AGENT_COMPOSE_PROJECT_FAILED",
                cancellationToken => composeProjects.ExecuteAsync(request, preview, cancellationToken));
            return operation;
        }
        catch
        {
            operation.State = OperationState.Failed;
            operation.Phase = "ValidationFailed";
            operation.ErrorCode = "AGENT_COMPOSE_PLAN_INVALID";
            operations.Save(operation);
            throw;
        }
    }

    private Task<ComposeProjectPreview> PreviewApplicationLifecycleAsync(RegisteredActionRequest request, CancellationToken cancellationToken)
    {
        registry.Require("whaledeck.apps", request.Action);
        var resourceId = "application:" + (request.Parameters.GetValueOrDefault("slug") ?? throw new InvalidOperationException("An application slug is required."));
        _ = ComposeProjectManager.ParseResourceId(resourceId);
        if (request.Resource.ResourceId.Length > 0 && request.Resource.ResourceId != resourceId)
            throw new InvalidOperationException("The application resource and slug differ.");
        return composeProjects.PreviewAsync(new RegisteredActionRequest
        {
            Action = "redeploy", Resource = new ResourceReference { ResourceId = resourceId, ResourceType = "compose-projects" }
        }, cancellationToken);
    }

    public override Task<OperationHandle> RunConfigRepositoryAction(RegisteredActionRequest request, ServerCallContext context) =>
        Task.FromResult(QueueRegistered(request, "ConfigRepository"));

    public override Task<OperationHandle> RunPlatformMaintenance(RegisteredActionRequest request, ServerCallContext context) =>
        Task.FromResult(QueueRegistered(request, "PlatformMaintenance", maintenance: true));

    public override async Task DownloadDiagnosticBundle(
        DiagnosticBundleRequest request,
        IServerStreamWriter<DiagnosticBundleChunk> responseStream,
        ServerCallContext context)
    {
        if (request.BundleId.Length != 32 || request.BundleId.Any(character => character is not (>= 'a' and <= 'f') && character is not (>= '0' and <= '9')))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "The diagnostic bundle id is invalid."));
        var path = Path.Combine("/var/lib/whaledeck-agent/diagnostics", request.BundleId + ".tar.gz");
        var info = new FileInfo(path);
        if (!info.Exists || info.LastWriteTimeUtc < DateTime.UtcNow.AddHours(-24))
            throw new RpcException(new Status(StatusCode.NotFound, "The diagnostic bundle is unavailable or expired."));
        if (info.Length > 10 * 1024 * 1024)
            throw new RpcException(new Status(StatusCode.ResourceExhausted, "The diagnostic bundle exceeds the approved size limit."));
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, context.CancellationToken);
            if (read == 0) break;
            await responseStream.WriteAsync(new DiagnosticBundleChunk
            {
                Content = Google.Protobuf.ByteString.CopyFrom(buffer, 0, read)
            });
        }
    }

    private OperationHandle QueueRegistered(RegisteredActionRequest request, string category, bool maintenance = false)
    {
        var resourceId = ManagedResourcePlanner.NormalizeResourceId(category, request.Action, request.Resource.ResourceId);
        var resource = registry.Require(resourceId, request.Action);
        var idempotencyKey = BuildIdempotencyKey(request.Context, category, request.Action, request.Resource.ResourceId);
        var operation = operations.GetOrCreate(idempotencyKey, $"{category}:{request.Action}", out var created);
        if (!created) return operation;
        try
        {
            if (ManagedResourcePlanner.RequiresPlan(request.Action))
            {
                plans.VerifyAndConsume(request.PlanHash, request.Resource.ResourceId, request.Action,
                    ManagedResourcePlanner.SanitizeParameters(request.Parameters));
            }
            coordinator.Start(
                operation,
                category == "Database" && request.Action is "run" or "verify"
                    ? "AGENT_BACKUP_FAILED"
                    : $"AGENT_{category.ToUpperInvariant()}_FAILED",
                cancellationToken => executor.ExecuteAsync(category, resource, request.Action, request.Parameters, cancellationToken),
                maintenance ? "Whale Deck 正在执行平台维护。" : null);
            return operation;
        }
        catch
        {
            operation.State = OperationState.Failed;
            operation.Phase = "ValidationFailed";
            operation.ErrorCode = "AGENT_ACTION_VALIDATION_FAILED";
            operations.Save(operation, maintenance ? "Whale Deck 平台维护预检失败。" : null);
            throw;
        }
    }

    private static string BuildIdempotencyKey(RequestContext? context, string category, string action, string resourceId) =>
        context is null || string.IsNullOrWhiteSpace(context.JobId)
            ? string.Empty
            : $"{context.JobId}:{context.IdempotencyKey}:{category}:{action}:{resourceId}";

    private static ResourceCollectionResponse ToCollection(IEnumerable<RegisteredResource> resources)
    {
        var response = new ResourceCollectionResponse();
        response.Resources.AddRange(resources.Select(ToSnapshot));
        return response;
    }

    private static ResourceSnapshot ToSnapshot(RegisteredResource resource)
    {
        var snapshot = new ResourceSnapshot
        {
            ResourceId = resource.Id,
            DisplayName = resource.ExternalId,
            ResourceType = resource.Type,
            State = "Registered",
            Version = string.Empty,
            ProtectedResource = resource.ProtectionLevel != "Managed"
        };
        snapshot.Attributes["protectionLevel"] = resource.ProtectionLevel;
        snapshot.Attributes["allowedActions"] = string.Join(',', resource.AllowedActions ?? []);
        return snapshot;
    }

}
