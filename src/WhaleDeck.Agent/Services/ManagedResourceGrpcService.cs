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
    BoundedProcessRunner processes)
    : ManagedResourceService.ManagedResourceServiceBase
{
    private const string PrivilegedHelper = "/usr/local/libexec/whaledeck-privileged";
    private static readonly string[] EditableDockerSettings =
        ["registry-mirrors", "log-driver", "log-opts", "live-restore", "features", "dns", "proxies"];

    public override Task<PlanResponse> PlanAction(RegisteredActionRequest request, ServerCallContext context) =>
        Task.FromResult(planner.Create(request));

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
        if (root is null || !Directory.Exists(root)) return response;
        var containers = await docker.ListContainersAsync(true, context.CancellationToken);
        foreach (var directory in Directory.EnumerateDirectories(root).Order(StringComparer.Ordinal))
        {
            var manifestPath = Path.Combine(directory, "manifest.json");
            if (!File.Exists(manifestPath)) continue;
            try
            {
                using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
                var slug = Path.GetFileName(directory);
                var image = manifest.RootElement.TryGetProperty("image", out var imageValue) ? imageValue.GetString() ?? string.Empty : string.Empty;
                var project = $"whaledeck-app-{slug}";
                var members = containers.Where(item => item.Labels.TryGetValue("com.docker.compose.project", out var value) &&
                                                       (string.Equals(value, project, StringComparison.Ordinal) ||
                                                        string.Equals(value, slug, StringComparison.Ordinal))).ToArray();
                var state = members.Length == 0
                    ? "Stopped"
                    : members.Any(item => string.Equals(item.State, "restarting", StringComparison.OrdinalIgnoreCase))
                        ? "Restarting"
                        : members.All(item => string.Equals(item.State, "running", StringComparison.OrdinalIgnoreCase))
                            ? "Running"
                            : "Stopped";
                var snapshot = new ResourceSnapshot
                {
                    ResourceId = $"application:{slug}",
                    DisplayName = slug,
                    ResourceType = "ComposeApplication",
                    State = state,
                    Version = image,
                    ProtectedResource = false
                };
                snapshot.Attributes["composeProject"] = project;
                snapshot.Attributes["containerCount"] = members.Length.ToString(System.Globalization.CultureInfo.InvariantCulture);
                snapshot.Attributes["autoUpdate"] = manifest.RootElement.TryGetProperty("autoUpdate", out var autoUpdate) && autoUpdate.GetBoolean() ? "true" : "false";
                snapshot.Attributes["versionPolicy"] = manifest.RootElement.TryGetProperty("versionPolicy", out var versionPolicy) ? versionPolicy.GetString() ?? "*" : "*";
                snapshot.Attributes["maintenanceWindow"] = manifest.RootElement.TryGetProperty("maintenanceWindow", out var maintenanceWindow) ? maintenanceWindow.GetString() ?? string.Empty : string.Empty;
                response.Resources.Add(snapshot);
            }
            catch (JsonException)
            {
                // Invalid manifests stay invisible to normal management and are handled by diagnostics.
            }
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

    public override Task<OperationHandle> RunComposeAction(RegisteredActionRequest request, ServerCallContext context) =>
        Task.FromResult(QueueRegistered(request, "Compose"));

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
