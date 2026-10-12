using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Docker.DotNet;
using Docker.DotNet.Models;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Agent.Services;

public sealed class DockerEngine : IDisposable, IContainerUsageSource
{
    private static readonly Regex ContainerNamePattern = new("^[a-zA-Z0-9][a-zA-Z0-9_.-]{0,63}$", RegexOptions.CultureInvariant);
    private static readonly Regex ImagePattern = new("^[a-zA-Z0-9][a-zA-Z0-9./_:@-]{0,254}$", RegexOptions.CultureInvariant);
    private static readonly Regex EnvironmentNamePattern = new("^[A-Z_][A-Z0-9_]{0,127}$", RegexOptions.CultureInvariant);
    private static readonly Regex PortPattern = new("^(0\\.0\\.0\\.0|127\\.0\\.0\\.1):[1-9][0-9]{0,4}:[1-9][0-9]{0,4}(/(tcp|udp))?$", RegexOptions.CultureInvariant);
    private static readonly Regex VolumeNamePattern = new("^[a-zA-Z0-9][a-zA-Z0-9_.-]{0,127}$", RegexOptions.CultureInvariant);
    private static readonly Regex NetworkNamePattern = new("^[a-zA-Z0-9][a-zA-Z0-9_.-]{0,127}$", RegexOptions.CultureInvariant);
    private static readonly Regex LabelNamePattern = new("^[a-zA-Z0-9][a-zA-Z0-9./_-]{0,127}$", RegexOptions.CultureInvariant);
    private readonly DockerClient _client;
    private readonly ResourceRegistry _registry;
    private readonly OperationStore _operations;
    private readonly PlanStore _plans;

    public DockerEngine(ResourceRegistry registry, OperationStore operations, PlanStore plans)
    {
        _registry = registry;
        _operations = operations;
        _plans = plans;
        _client = new DockerClientConfiguration(new Uri("unix:///var/run/docker.sock")).CreateClient();
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken) =>
        await TryAsync(async () => { await _client.System.PingAsync(cancellationToken); });

    public Task<SystemInfoResponse> GetInfoAsync(CancellationToken cancellationToken) =>
        _client.System.GetSystemInfoAsync(cancellationToken);

    public Task<IList<ContainerListResponse>> ListContainersAsync(bool all, CancellationToken cancellationToken) =>
        _client.Containers.ListContainersAsync(new ContainersListParameters { All = all }, cancellationToken);

    public async Task<Docker.DotNet.Models.ContainerStatsResponse> ReadContainerUsageAsync(string id, CancellationToken cancellationToken)
    {
        var progress = new NativeStatsProgress();
        await _client.Containers.GetContainerStatsAsync(id, new ContainerStatsParameters { Stream = false, OneShot = true }, progress, cancellationToken);
        return progress.Value ?? throw new InvalidOperationException("Docker did not provide container statistics.");
    }

    private sealed class NativeStatsProgress : IProgress<Docker.DotNet.Models.ContainerStatsResponse>
    {
        public Docker.DotNet.Models.ContainerStatsResponse? Value { get; private set; }
        public void Report(Docker.DotNet.Models.ContainerStatsResponse value) => Value = value;
    }

    public Task MonitorEventsAsync(IProgress<Message> progress, CancellationToken cancellationToken) =>
        _client.System.MonitorEventsAsync(new ContainerEventsParameters(), progress, cancellationToken);

    public async Task<Docker.DotNet.Models.ContainerInspectResponse> InspectContainerAsync(string containerId, CancellationToken cancellationToken)
    {
        var container = await ResolveContainerAsync(containerId, cancellationToken);
        return await _client.Containers.InspectContainerAsync(container.ID, cancellationToken);
    }

    public async Task<bool> IsApplicationHealthyAsync(string slug, CancellationToken cancellationToken)
    {
        var expectedName = "whaledeck-app-" + slug;
        var containers = await ListContainersAsync(true, cancellationToken);
        var members = containers.Where(item => item.Names.Any(name =>
            string.Equals(name.TrimStart('/'), expectedName, StringComparison.Ordinal))).ToArray();
        if (members.Length == 0 || !members.All(item =>
            string.Equals(item.State, "running", StringComparison.OrdinalIgnoreCase) &&
            !(item.Status ?? string.Empty).Contains("unhealthy", StringComparison.OrdinalIgnoreCase) &&
            !(item.Status ?? string.Empty).Contains("health: starting", StringComparison.OrdinalIgnoreCase))) return false;
        foreach (var member in members)
            if (!IsStableContainerState((await _client.Containers.InspectContainerAsync(member.ID, cancellationToken)).State, DateTimeOffset.UtcNow))
                return false;
        return true;
    }

    public static bool IsStableContainerState(Docker.DotNet.Models.ContainerState? state, DateTimeOffset now) =>
        state?.Running == true && !state.Restarting && !state.Dead && !state.Paused &&
        DateTimeOffset.TryParse(state.StartedAt, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind, out var startedAt) &&
        now - startedAt >= TimeSpan.FromSeconds(6) &&
        (state.Health is null || string.Equals(state.Health.Status, "healthy", StringComparison.OrdinalIgnoreCase));

    public Task<IList<ImagesListResponse>> ListImagesAsync(CancellationToken cancellationToken) =>
        _client.Images.ListImagesAsync(new ImagesListParameters { All = true }, cancellationToken);

    public async Task<ImageInspectResponse> InspectImageAsync(string image, bool pullIfMissing, CancellationToken cancellationToken)
    {
        ValidateImage(image);
        try
        {
            return await _client.Images.InspectImageAsync(image, cancellationToken);
        }
        catch (DockerApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound && pullIfMissing)
        {
            await PullImageAsync(image, cancellationToken);
            return await _client.Images.InspectImageAsync(image, cancellationToken);
        }
    }

    public Task<IList<NetworkResponse>> ListNetworksAsync(CancellationToken cancellationToken) =>
        _client.Networks.ListNetworksAsync(cancellationToken: cancellationToken);

    public async Task<IList<VolumeResponse>> ListVolumesAsync(CancellationToken cancellationToken)
    {
        var response = await _client.Volumes.ListAsync(cancellationToken: cancellationToken);
        return response.Volumes ?? [];
    }

    public async Task<PlanResponse> PlanAsync(
        string resourceId,
        string action,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken cancellationToken)
    {
        ContainerConfiguration? createConfiguration = null;
        ContainerUpdatePlan? updatePlan = null;
        BatchContainerPlan? batchPlan = null;
        string? dockerResourceChange = null;
        string? dockerResourceWarning = null;
        string? deleteName = null;
        long? unusedImageBytes = null;
        if (action == "create")
        {
            createConfiguration = ParseContainerConfiguration(parameters);
            await ValidateContainerCreationAsync(createConfiguration, cancellationToken);
        }
        else if (action == "delete")
        {
            var container = await ResolveContainerAsync(resourceId, cancellationToken);
            deleteName = container.Names.FirstOrDefault()?.TrimStart('/') ?? container.ID;
            if (_registry.IsProtectedContainer(deleteName, container.Labels))
                throw new InvalidOperationException("Protected containers require the platform maintenance workflow.");
        }
        else if (action == "update")
        {
            updatePlan = await ValidateContainerUpdateAsync(resourceId, parameters, cancellationToken);
        }
        else if (action == "rebuild")
        {
            updatePlan = await ValidateContainerRebuildAsync(resourceId, cancellationToken);
        }
        else if (action == "prune")
        {
            unusedImageBytes = (await ListImagesAsync(cancellationToken)).Where(item => item.Containers == 0).Sum(item => item.Size);
        }
        else if (action is "batch-start" or "batch-stop" or "batch-restart" or "batch-delete")
        {
            batchPlan = await ValidateBatchAsync(action, parameters, cancellationToken);
        }
        else if (action == "network-create")
        {
            var name = ValidateDockerResourceName(Required(parameters, "name"), "network");
            if ((await ListNetworksAsync(cancellationToken)).Any(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("A Docker network with the requested name already exists.");
            _ = ParseBoolean(parameters, "internal", false);
            _ = ParseBoolean(parameters, "attachable", true);
            dockerResourceChange = $"创建自定义 bridge 网络 {name}";
        }
        else if (action == "network-delete")
        {
            var network = await ResolveNetworkAsync(resourceId, cancellationToken);
            ValidateNetworkDeletion(network);
            dockerResourceChange = $"删除空闲网络 {network.Name}";
            dockerResourceWarning = "删除后使用该网络名称的部署配置将无法启动，直到重新创建网络。";
        }
        else if (action == "volume-delete")
        {
            var volume = await ResolveVolumeAsync(resourceId, cancellationToken);
            await ValidateVolumeDeletionAsync(volume, cancellationToken);
            if (!parameters.TryGetValue("confirmationName", out var confirmationName) ||
                !string.Equals(confirmationName, volume.Name, StringComparison.Ordinal))
                throw new InvalidOperationException("Deleting a Docker volume requires its exact name as confirmation.");
            dockerResourceChange = $"删除孤立数据卷 {volume.Name}";
            dockerResourceWarning = "数据卷中的内容将永久删除，且本操作不提供恢复。";
        }

        // Persist the confirmation token only after every preflight check has passed.
        // This prevents rejected requests from leaving usable-but-invalid plan files behind.
        var response = _plans.Create(resourceId, action, parameters);
        if (createConfiguration is not null)
        {
            response.Changes.Clear();
            response.Changes.Add($"创建容器 {createConfiguration.Name}");
            response.Changes.Add($"使用镜像 {createConfiguration.Image}");
            if (createConfiguration.Ports.Count > 0) response.Changes.Add($"发布 {createConfiguration.Ports.Count} 个端口");
            if (createConfiguration.Mounts.Count > 0) response.Changes.Add($"挂载 {createConfiguration.Mounts.Count} 个命名卷");
            if (createConfiguration.Networks.Length > 0) response.Changes.Add($"连接网络：{string.Join(", ", createConfiguration.Networks)}");
            if (createConfiguration.AutoUpdate) response.Warnings.Add("此容器已允许进入自动镜像更新计划。");
        }
        else if (updatePlan is not null)
        {
            response.Changes.Clear();
            if (action == "rebuild")
            {
                response.Changes.Add($"按现有配置重建容器 {updatePlan.Name}");
                response.Changes.Add($"使用宿主机当前可用的镜像 {updatePlan.Image}");
                response.Changes.Add("重建成功后删除旧容器；健康检查失败时自动回滚。");
                response.Warnings.Add("重建不会拉取镜像；若本地镜像标签已变化，将使用标签当前指向的镜像。服务会短暂中断。");
            }
            else
            {
                response.Changes.Add($"检查并更新容器 {updatePlan.Name}");
                response.Changes.Add($"重新拉取镜像 {updatePlan.Image}");
                response.Changes.Add("更新成功后删除旧容器；健康检查失败时自动回滚。");
                response.Warnings.Add("容器更新期间会有短暂服务中断。");
            }
        }
        else if (deleteName is not null)
        {
            response.Changes.Clear();
            response.Changes.Add($"删除容器 {deleteName}");
            response.Warnings.Add(parameters.TryGetValue("preserveVolumes", out var preserve) && preserve == "false"
                ? "匿名卷将随容器删除；命名卷仍会保留。"
                : "数据卷与镜像将保留。");
        }
        else if (unusedImageBytes.HasValue)
        {
            response.Changes.Clear();
            response.Changes.Add($"清理未使用镜像，预计最多回收 {unusedImageBytes.Value} 字节");
        }
        else if (batchPlan is not null)
        {
            response.Changes.Clear();
            response.Changes.Add($"批量{BatchActionLabel(batchPlan.Action)} {batchPlan.Affected.Count} 个普通容器");
            response.Changes.Add(string.Join("、", batchPlan.Affected.Take(12)) +
                (batchPlan.Affected.Count > 12 ? $" 等 {batchPlan.Affected.Count} 个" : string.Empty));
            if (batchPlan.Skipped.Count > 0)
                response.Warnings.Add($"将跳过 {batchPlan.Skipped.Count} 个受保护或不存在的容器：{string.Join("、", batchPlan.Skipped.Take(8))}");
        }
        else if (dockerResourceChange is not null)
        {
            response.Changes.Clear();
            response.Changes.Add(dockerResourceChange);
            if (dockerResourceWarning is not null) response.Warnings.Add(dockerResourceWarning);
        }
        return response;
    }

    public async Task<OperationHandle> RunActionAsync(
        string resourceId,
        string action,
        IReadOnlyDictionary<string, string> parameters,
        string? planHash,
        string jobId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var operationKey = string.IsNullOrWhiteSpace(jobId)
            ? string.Empty
            : $"{jobId}:{idempotencyKey}:Docker:{action}:{resourceId}";
        var operation = _operations.GetOrCreate(operationKey, $"Docker{action}", out var created);
        if (!created) return operation;
        operation.State = OperationState.Running;
        operation.Phase = "Validating";
        operation.ProgressPercent = 10;
        _operations.Save(operation);

        try
        {
            if (action is "create" or "prune" or "update" or "rebuild" or "network-create" or "network-delete" or "volume-delete" or
                "batch-start" or "batch-stop" or "batch-restart" or "batch-delete")
                _plans.VerifyAndConsume(planHash ?? string.Empty, resourceId, action, NormalizePlanParameters(parameters));
            switch (action)
            {
                case "pull":
                    await PullImageAsync(Required(parameters, "image"), cancellationToken);
                    break;
                case "create":
                    await CreateManagedContainerAsync(parameters, cancellationToken);
                    break;
                case "update":
                    operation.ResultJson = await UpdateManagedContainerAsync(resourceId, parameters, jobId, cancellationToken);
                    break;
                case "rebuild":
                    operation.ResultJson = await RebuildManagedContainerAsync(resourceId, jobId, cancellationToken);
                    break;
                case "prune":
                    await PruneAsync(parameters, cancellationToken);
                    break;
                case "batch-start":
                case "batch-stop":
                case "batch-restart":
                case "batch-delete":
                    operation.ResultJson = await ExecuteBatchAsync(action, parameters, cancellationToken);
                    break;
                case "network-create":
                    operation.ResultJson = await CreateNetworkAsync(parameters, cancellationToken);
                    break;
                case "network-delete":
                    operation.ResultJson = await DeleteNetworkAsync(resourceId, cancellationToken);
                    break;
                case "volume-delete":
                    operation.ResultJson = await DeleteVolumeAsync(resourceId, cancellationToken);
                    break;
                default:
                    throw new InvalidOperationException("Unsupported Docker action.");
            }
            operation.State = OperationState.Succeeded;
            operation.Phase = "Completed";
            operation.ProgressPercent = 100;
        }
        catch (ContainerUpdateException exception)
        {
            operation.State = OperationState.Failed;
            operation.Phase = exception.RolledBack ? "RolledBack" : "Failed";
            operation.ErrorCode = "CONTAINER_UPDATE_FAILED";
            operation.ResultJson = exception.ResultJson;
        }
        catch
        {
            operation.State = OperationState.Failed;
            operation.Phase = "Failed";
            operation.ErrorCode = "DOCKER_OPERATION_FAILED";
            throw;
        }
        finally
        {
            _operations.Save(operation);
        }
        return operation;
    }

    private async Task PullImageAsync(string image, CancellationToken cancellationToken)
    {
        ValidateImage(image);
        var (name, tag) = SplitImage(image);
        await _client.Images.CreateImageAsync(new ImagesCreateParameters { FromImage = name, Tag = tag },
            new AuthConfig(), new Progress<JSONMessage>(), cancellationToken);
    }

    private async Task CreateManagedContainerAsync(IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        var configuration = ParseContainerConfiguration(parameters);
        await ValidateContainerCreationAsync(configuration, cancellationToken);
        await PullImageAsync(configuration.Image, cancellationToken);
        string? createdId = null;
        try
        {
            var labels = new Dictionary<string, string>(configuration.Labels, StringComparer.Ordinal)
            {
                ["io.whaledeck.managed"] = "true",
                ["io.whaledeck.protected"] = "false",
                ["io.whaledeck.autoupdate"] = configuration.AutoUpdate ? "true" : "false",
                ["io.whaledeck.update.version-policy"] = configuration.VersionPolicy,
                ["io.whaledeck.update.maintenance-window"] = configuration.MaintenanceWindow
            };
            var response = await _client.Containers.CreateContainerAsync(new CreateContainerParameters
            {
                Name = configuration.Name,
                Image = configuration.Image,
                Env = configuration.Environment,
                Cmd = configuration.Command,
                Entrypoint = configuration.Entrypoint,
                Labels = labels,
                ExposedPorts = configuration.Ports.ToDictionary(item => item.Key, _ => new EmptyStruct()),
                Healthcheck = configuration.Healthcheck,
                HostConfig = new HostConfig
                {
                    CapDrop = ["ALL"],
                    SecurityOpt = ["no-new-privileges:true"],
                    Init = true,
                    Memory = configuration.MemoryMb * 1024 * 1024,
                    NanoCPUs = checked((long)(configuration.Cpus * 1_000_000_000)),
                    RestartPolicy = new RestartPolicy { Name = configuration.RestartPolicy },
                    NetworkMode = configuration.Networks.FirstOrDefault() ?? "bridge",
                    PortBindings = configuration.Ports,
                    Mounts = configuration.Mounts,
                    LogConfig = new LogConfig
                    {
                        Type = "local",
                        Config = new Dictionary<string, string>
                        {
                            ["max-size"] = "10m",
                            ["max-file"] = "5",
                            ["compress"] = "true"
                        }
                    }
                }
            }, cancellationToken);
            createdId = response.ID;
            if (response.Warnings is { Count: > 0 }) throw new InvalidOperationException("Docker returned warnings while creating the container.");
            foreach (var network in configuration.Networks.Skip(1))
            {
                await _client.Networks.ConnectNetworkAsync(network, new NetworkConnectParameters
                {
                    Container = response.ID,
                    EndpointConfig = new EndpointSettings()
                }, cancellationToken);
            }
            await _client.Containers.StartContainerAsync(response.ID, new ContainerStartParameters(), cancellationToken);
        }
        catch
        {
            if (!string.IsNullOrWhiteSpace(createdId))
            {
                try
                {
                    await _client.Containers.RemoveContainerAsync(createdId,
                        new ContainerRemoveParameters { Force = true, RemoveVolumes = false }, CancellationToken.None);
                }
                catch (DockerApiException)
                {
                    // The original create failure remains the actionable error.
                }
            }
            throw;
        }
    }

    private async Task<ContainerUpdatePlan> ValidateContainerUpdateAsync(
        string resourceId,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken cancellationToken)
    {
        var container = await ResolveContainerAsync(resourceId, cancellationToken);
        var name = container.Names.FirstOrDefault()?.TrimStart('/') ?? container.ID;
        if (_registry.IsProtectedContainer(name, container.Labels))
            throw new InvalidOperationException("Protected containers require the platform maintenance workflow.");
        if (container.Labels.TryGetValue("com.docker.compose.project", out var composeProject) && !string.IsNullOrWhiteSpace(composeProject))
            throw new InvalidOperationException("Compose containers must be updated through their application workflow.");
        if (!IsAutoUpdateEnabled(container.Labels))
            throw new InvalidOperationException("Container updates require the autoupdate=true label.");

        var metadata = await _client.Containers.InspectContainerAsync(container.ID, cancellationToken);
        var image = metadata.Config?.Image ?? throw new InvalidOperationException("The container image reference is unavailable.");
        ValidateImage(image);
        if (image.Contains('@', StringComparison.Ordinal))
            throw new InvalidOperationException("Digest-pinned containers cannot discover an update automatically.");
        if (metadata.HostConfig?.AutoRemove == true)
            throw new InvalidOperationException("Auto-remove containers cannot be updated with rollback protection.");

        var policy = container.Labels.TryGetValue("io.whaledeck.update.version-policy", out var configuredPolicy)
            ? configuredPolicy
            : "*";
        var tag = SplitImage(image).Tag.TrimStart('v');
        if (!VersionMatchesPolicy(tag, policy))
            throw new InvalidOperationException("The current image tag is outside the configured update version policy.");
        var automatic = parameters.TryGetValue("automatic", out var automaticValue) &&
            bool.TryParse(automaticValue, out var automaticEnabled) && automaticEnabled;
        if (automatic)
        {
            var window = container.Labels.TryGetValue("io.whaledeck.update.maintenance-window", out var configuredWindow)
                ? configuredWindow
                : "Sun@20:00-23:59";
            if (!IsInMaintenanceWindow(window, DateTimeOffset.UtcNow))
                throw new InvalidOperationException("The container is outside its automatic update maintenance window.");
        }
        return new ContainerUpdatePlan(container.ID, name, image, metadata.Image ?? string.Empty, policy);
    }

    private async Task<ContainerUpdatePlan> ValidateContainerRebuildAsync(
        string resourceId,
        CancellationToken cancellationToken)
    {
        var container = await ResolveContainerAsync(resourceId, cancellationToken);
        var name = container.Names.FirstOrDefault()?.TrimStart('/') ?? container.ID;
        if (_registry.IsProtectedContainer(name, container.Labels))
            throw new InvalidOperationException("Protected containers require the platform maintenance workflow.");
        if (container.Labels.TryGetValue("com.docker.compose.project", out var composeProject) && !string.IsNullOrWhiteSpace(composeProject))
            throw new InvalidOperationException("Compose containers must be rebuilt through their application workflow.");

        var metadata = await _client.Containers.InspectContainerAsync(container.ID, cancellationToken);
        var image = metadata.Config?.Image ?? throw new InvalidOperationException("The container image reference is unavailable.");
        ValidateImage(image);
        if (metadata.HostConfig?.AutoRemove == true)
            throw new InvalidOperationException("Auto-remove containers cannot be rebuilt with rollback protection.");
        _ = await _client.Images.InspectImageAsync(image, cancellationToken);
        return new ContainerUpdatePlan(container.ID, name, image, metadata.Image ?? string.Empty, "manual-rebuild");
    }

    private async Task<string> UpdateManagedContainerAsync(
        string resourceId,
        IReadOnlyDictionary<string, string> parameters,
        string jobId,
        CancellationToken cancellationToken)
    {
        var plan = await ValidateContainerUpdateAsync(resourceId, parameters, cancellationToken);
        var original = await _client.Containers.InspectContainerAsync(plan.Id, cancellationToken);
        await PullImageAsync(plan.Image, cancellationToken);
        var replacementImage = await _client.Images.InspectImageAsync(plan.Image, cancellationToken);
        var newImageId = replacementImage.ID ?? string.Empty;
        if (string.Equals(plan.OldImageId, newImageId, StringComparison.Ordinal))
        {
            return JsonSerializer.Serialize(new
            {
                containerId = plan.Id,
                containerName = plan.Name,
                image = plan.Image,
                oldImageId = plan.OldImageId,
                imageId = newImageId,
                versionPolicy = plan.VersionPolicy,
                updated = false,
                rolledBack = false
            });
        }

        return await ReplaceContainerAsync(plan, original, newImageId, "update", jobId, cancellationToken);
    }

    private async Task<string> RebuildManagedContainerAsync(
        string resourceId,
        string jobId,
        CancellationToken cancellationToken)
    {
        var plan = await ValidateContainerRebuildAsync(resourceId, cancellationToken);
        var original = await _client.Containers.InspectContainerAsync(plan.Id, cancellationToken);
        var localImage = await _client.Images.InspectImageAsync(plan.Image, cancellationToken);
        return await ReplaceContainerAsync(plan, original, localImage.ID ?? plan.OldImageId, "rebuild", jobId, cancellationToken);
    }

    private async Task<string> ReplaceContainerAsync(
        ContainerUpdatePlan plan,
        Docker.DotNet.Models.ContainerInspectResponse original,
        string newImageId,
        string action,
        string jobId,
        CancellationToken cancellationToken)
    {

        var wasRunning = original.State?.Running == true;
        var rollbackName = BuildRollbackName(plan.Name, jobId);
        string? replacementId = null;
        var oldRenamed = false;
        try
        {
            if (wasRunning)
            {
                await _client.Containers.StopContainerAsync(plan.Id,
                    new ContainerStopParameters { WaitBeforeKillSeconds = 20 }, cancellationToken);
            }
            await _client.Containers.RenameContainerAsync(plan.Id,
                new ContainerRenameParameters { NewName = rollbackName }, cancellationToken);
            oldRenamed = true;

            var create = BuildReplacementParameters(original, plan.Name, plan.Image);
            var created = await _client.Containers.CreateContainerAsync(create, cancellationToken);
            replacementId = created.ID;
            if (created.Warnings is { Count: > 0 })
                throw new InvalidOperationException("Docker returned warnings while creating the replacement container.");
            if (wasRunning)
            {
                await _client.Containers.StartContainerAsync(replacementId, new ContainerStartParameters(), cancellationToken);
                await WaitForContainerHealthAsync(replacementId, original.Config?.Healthcheck is not null,
                    TimeSpan.FromSeconds(60), cancellationToken);
            }
            await _client.Containers.RemoveContainerAsync(plan.Id,
                new ContainerRemoveParameters { Force = false, RemoveVolumes = false }, cancellationToken);
            return JsonSerializer.Serialize(new
            {
                containerId = replacementId,
                containerName = plan.Name,
                image = plan.Image,
                oldImageId = plan.OldImageId,
                imageId = newImageId,
                versionPolicy = plan.VersionPolicy,
                action,
                updated = true,
                rolledBack = false
            });
        }
        catch (Exception exception)
        {
            var rolledBack = false;
            try
            {
                if (!string.IsNullOrWhiteSpace(replacementId))
                {
                    await _client.Containers.RemoveContainerAsync(replacementId,
                        new ContainerRemoveParameters { Force = true, RemoveVolumes = false }, CancellationToken.None);
                }
                if (oldRenamed)
                {
                    await _client.Containers.RenameContainerAsync(plan.Id,
                        new ContainerRenameParameters { NewName = plan.Name }, CancellationToken.None);
                    if (wasRunning)
                        await _client.Containers.StartContainerAsync(plan.Id, new ContainerStartParameters(), CancellationToken.None);
                    rolledBack = true;
                }
            }
            catch (DockerApiException)
            {
                rolledBack = false;
            }
            var result = JsonSerializer.Serialize(new
            {
                containerId = plan.Id,
                containerName = plan.Name,
                image = plan.Image,
                oldImageId = plan.OldImageId,
                imageId = newImageId,
                versionPolicy = plan.VersionPolicy,
                action,
                updated = false,
                rolledBack
            });
            throw new ContainerUpdateException("Container update failed.", result, rolledBack, exception);
        }
    }

    private static CreateContainerParameters BuildReplacementParameters(
        Docker.DotNet.Models.ContainerInspectResponse original,
        string name,
        string image)
    {
        var config = original.Config ?? throw new InvalidOperationException("The original container configuration is unavailable.");
        var host = original.HostConfig ?? throw new InvalidOperationException("The original host configuration is unavailable.");
        var endpoints = original.NetworkSettings?.Networks?.ToDictionary(
            item => item.Key,
            item => new EndpointSettings
            {
                Aliases = item.Value.Aliases,
                DriverOpts = item.Value.DriverOpts,
                Links = item.Value.Links,
                IPAMConfig = item.Value.IPAMConfig
            }, StringComparer.Ordinal);
        return new CreateContainerParameters
        {
            Name = name,
            Image = image,
            Hostname = config.Hostname,
            Domainname = config.Domainname,
            User = config.User,
            AttachStdin = config.AttachStdin,
            AttachStdout = config.AttachStdout,
            AttachStderr = config.AttachStderr,
            ExposedPorts = config.ExposedPorts,
            Tty = config.Tty,
            OpenStdin = config.OpenStdin,
            StdinOnce = config.StdinOnce,
            Env = config.Env,
            Cmd = config.Cmd,
            Healthcheck = config.Healthcheck,
            ArgsEscaped = config.ArgsEscaped,
            Volumes = config.Volumes,
            WorkingDir = config.WorkingDir,
            Entrypoint = config.Entrypoint,
            NetworkDisabled = config.NetworkDisabled,
            MacAddress = config.MacAddress,
            OnBuild = config.OnBuild,
            Labels = config.Labels,
            StopSignal = config.StopSignal,
            StopTimeout = config.StopTimeout,
            Shell = config.Shell,
            HostConfig = host,
            NetworkingConfig = endpoints is null ? null : new NetworkingConfig { EndpointsConfig = endpoints }
        };
    }

    private async Task WaitForContainerHealthAsync(
        string containerId,
        bool hasHealthcheck,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        do
        {
            var state = (await _client.Containers.InspectContainerAsync(containerId, cancellationToken)).State;
            if (state?.Running != true) throw new InvalidOperationException("The replacement container stopped during startup.");
            var health = state.Health?.Status;
            if (IsStableContainerState(state, DateTimeOffset.UtcNow) &&
                (!hasHealthcheck || string.Equals(health, "healthy", StringComparison.OrdinalIgnoreCase))) return;
            if (string.Equals(health, "unhealthy", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The replacement container became unhealthy.");
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }
        while (DateTimeOffset.UtcNow < deadline);
        throw new TimeoutException("The replacement container did not become healthy before the timeout.");
    }

    private async Task PruneAsync(IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        var pruneImages = !parameters.TryGetValue("images", out var images) || !bool.TryParse(images, out var parsedImages) || parsedImages;
        var pruneVolumes = parameters.TryGetValue("volumes", out var volumes) && bool.TryParse(volumes, out var parsedVolumes) && parsedVolumes;
        if (pruneImages)
        {
            await _client.Images.PruneImagesAsync(new ImagesPruneParameters
            {
                Filters = new Dictionary<string, IDictionary<string, bool>> { ["dangling"] = new Dictionary<string, bool> { ["true"] = true } }
            }, cancellationToken);
        }
        if (pruneVolumes)
        {
            await _client.Volumes.PruneAsync(new VolumesPruneParameters
            {
                Filters = new Dictionary<string, IDictionary<string, bool>> { ["label!"] = new Dictionary<string, bool> { ["io.whaledeck.protected=true"] = true } }
            }, cancellationToken);
        }
    }

    private async Task<string> CreateNetworkAsync(
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken cancellationToken)
    {
        var name = ValidateDockerResourceName(Required(parameters, "name"), "network");
        if ((await ListNetworksAsync(cancellationToken)).Any(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("A Docker network with the requested name already exists.");
        var response = await _client.Networks.CreateNetworkAsync(new NetworksCreateParameters
        {
            Name = name,
            Driver = "bridge",
            Internal = ParseBoolean(parameters, "internal", false),
            Attachable = ParseBoolean(parameters, "attachable", true),
            CheckDuplicate = true,
            Labels = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["io.whaledeck.managed"] = "true",
                ["io.whaledeck.protected"] = "false"
            }
        }, cancellationToken);
        if (!string.IsNullOrWhiteSpace(response.Warning))
            throw new InvalidOperationException("Docker returned a warning while creating the network.");
        return JsonSerializer.Serialize(new { networkId = response.ID, name, driver = "bridge" });
    }

    private async Task<string> DeleteNetworkAsync(string resourceId, CancellationToken cancellationToken)
    {
        var network = await ResolveNetworkAsync(resourceId, cancellationToken);
        ValidateNetworkDeletion(network);
        await _client.Networks.DeleteNetworkAsync(network.ID, cancellationToken);
        return JsonSerializer.Serialize(new { networkId = network.ID, name = network.Name, deleted = true });
    }

    private async Task<string> DeleteVolumeAsync(string resourceId, CancellationToken cancellationToken)
    {
        var volume = await ResolveVolumeAsync(resourceId, cancellationToken);
        await ValidateVolumeDeletionAsync(volume, cancellationToken);
        await _client.Volumes.RemoveAsync(volume.Name, false, cancellationToken);
        return JsonSerializer.Serialize(new { volume = volume.Name, deleted = true });
    }

    private async Task<NetworkResponse> ResolveNetworkAsync(string resourceId, CancellationToken cancellationToken)
    {
        var value = resourceId.StartsWith("network:", StringComparison.Ordinal) ? resourceId["network:".Length..] : resourceId;
        var matches = (await ListNetworksAsync(cancellationToken)).Where(item =>
            item.ID.StartsWith(value, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(item.Name, value, StringComparison.Ordinal)).ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException("Docker network was not found."),
            _ => throw new InvalidOperationException("Docker network id is ambiguous.")
        };
    }

    private static void ValidateNetworkDeletion(NetworkResponse network)
    {
        if (network.Name is "bridge" or "host" or "none" || network.Ingress || network.ConfigOnly ||
            IsProtectedDockerResourceName(network.Name) ||
            network.Labels.TryGetValue("io.whaledeck.protected", out var protectedValue) &&
            string.Equals(protectedValue, "true", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Built-in and protected platform networks cannot be deleted.");
        if (network.Containers is { Count: > 0 })
            throw new InvalidOperationException("The Docker network is still attached to one or more containers.");
    }

    private async Task<VolumeResponse> ResolveVolumeAsync(string resourceId, CancellationToken cancellationToken)
    {
        var value = resourceId.StartsWith("volume:", StringComparison.Ordinal) ? resourceId["volume:".Length..] : resourceId;
        var volume = (await ListVolumesAsync(cancellationToken)).SingleOrDefault(item => string.Equals(item.Name, value, StringComparison.Ordinal));
        return volume ?? throw new InvalidOperationException("Docker volume was not found.");
    }

    private async Task ValidateVolumeDeletionAsync(VolumeResponse volume, CancellationToken cancellationToken)
    {
        if (IsProtectedDockerResourceName(volume.Name) ||
            volume.Labels.TryGetValue("io.whaledeck.protected", out var protectedValue) &&
            string.Equals(protectedValue, "true", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Protected platform volumes cannot be deleted.");
        var containers = await ListContainersAsync(true, cancellationToken);
        if (containers.Any(container => (container.Mounts ?? []).Any(mount => string.Equals(mount.Name, volume.Name, StringComparison.Ordinal))))
            throw new InvalidOperationException("The Docker volume is still referenced by a container.");
    }

    public async Task<OperationHandle> ChangeContainerStateAsync(
        string containerId,
        ContainerAction action,
        uint timeoutSeconds,
        bool preserveVolumes,
        string? planHash,
        string jobId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var containers = await ListContainersAsync(true, cancellationToken);
        var container = containers.FirstOrDefault(item =>
            item.ID.StartsWith(containerId, StringComparison.OrdinalIgnoreCase) ||
            item.Names.Any(name => string.Equals(name.TrimStart('/'), containerId.TrimStart('/'), StringComparison.Ordinal)));
        if (container is null)
        {
            throw new InvalidOperationException("Container was not found.");
        }

        var name = container.Names.FirstOrDefault()?.TrimStart('/') ?? container.ID;
        var protectedResource = _registry.IsProtectedContainer(name, container.Labels);
        if (protectedResource && action is ContainerAction.Stop or ContainerAction.Restart or ContainerAction.Delete)
        {
            throw new InvalidOperationException("Protected containers require the platform maintenance workflow.");
        }
        var operationKey = string.IsNullOrWhiteSpace(jobId)
            ? string.Empty
            : $"{jobId}:{idempotencyKey}:Container:{action}:{container.ID}";
        var operation = _operations.GetOrCreate(operationKey, $"Container{action}", out var created);
        if (!created) return operation;
        operation.State = OperationState.Running;
        operation.ProgressPercent = 25;
        _operations.Save(operation);

        try
        {
            if (action is ContainerAction.Delete)
            {
                _plans.VerifyAndConsume(planHash ?? string.Empty, containerId, "delete", new Dictionary<string, string>
                {
                    ["timeoutSeconds"] = timeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["preserveVolumes"] = preserveVolumes.ToString().ToLowerInvariant()
                });
            }

            switch (action)
            {
                case ContainerAction.Start:
                    await _client.Containers.StartContainerAsync(container.ID, new ContainerStartParameters(), cancellationToken);
                    break;
                case ContainerAction.Stop:
                    await _client.Containers.StopContainerAsync(container.ID,
                        new ContainerStopParameters { WaitBeforeKillSeconds = timeoutSeconds is 0 ? 10 : timeoutSeconds }, cancellationToken);
                    break;
                case ContainerAction.Restart:
                    await _client.Containers.RestartContainerAsync(container.ID,
                        new ContainerRestartParameters { WaitBeforeKillSeconds = timeoutSeconds is 0 ? 10 : timeoutSeconds }, cancellationToken);
                    break;
                case ContainerAction.Delete:
                    await _client.Containers.RemoveContainerAsync(container.ID,
                        new ContainerRemoveParameters { RemoveVolumes = !preserveVolumes, Force = false }, cancellationToken);
                    break;
                default:
                    throw new InvalidOperationException("Unsupported container action.");
            }

            operation.State = OperationState.Succeeded;
            operation.ProgressPercent = 100;
            operation.Phase = "Completed";
        }
        catch
        {
            operation.State = OperationState.Failed;
            operation.ErrorCode = "DOCKER_OPERATION_FAILED";
            operation.Phase = "Failed";
            throw;
        }
        finally
        {
            _operations.Save(operation);
        }

        return operation;
    }

    public void Dispose() => _client.Dispose();

    private async Task<BatchContainerPlan> ValidateBatchAsync(
        string action,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken cancellationToken)
    {
        var requested = ParseBatchContainerIds(parameters);
        var containers = await ListContainersAsync(true, cancellationToken);
        var affected = new List<string>();
        var skipped = new List<string>();
        foreach (var requestedId in requested)
        {
            var container = FindContainer(containers, requestedId);
            if (container is null)
            {
                skipped.Add(requestedId);
                continue;
            }
            var name = container.Names.FirstOrDefault()?.TrimStart('/') ?? container.ID;
            if (_registry.IsProtectedContainer(name, container.Labels))
            {
                skipped.Add(name);
                continue;
            }
            affected.Add(name);
        }
        if (affected.Count == 0) throw new InvalidOperationException("The batch does not contain an eligible container.");
        return new BatchContainerPlan(action["batch-".Length..], affected, skipped);
    }

    private async Task<string> ExecuteBatchAsync(
        string action,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken cancellationToken)
    {
        var requested = ParseBatchContainerIds(parameters);
        var containers = await ListContainersAsync(true, cancellationToken);
        var completed = new List<string>();
        var skipped = new List<string>();
        var failures = new List<object>();
        var timeout = checked((uint)ParseLong(parameters, "timeoutSeconds", 10, 1, 300));
        var preserveVolumes = ParseBoolean(parameters, "preserveVolumes", true);
        foreach (var requestedId in requested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var container = FindContainer(containers, requestedId);
            if (container is null)
            {
                skipped.Add(requestedId);
                continue;
            }
            var name = container.Names.FirstOrDefault()?.TrimStart('/') ?? container.ID;
            if (_registry.IsProtectedContainer(name, container.Labels))
            {
                skipped.Add(name);
                continue;
            }
            try
            {
                switch (action)
                {
                    case "batch-start":
                        await _client.Containers.StartContainerAsync(container.ID, new ContainerStartParameters(), cancellationToken);
                        break;
                    case "batch-stop":
                        await _client.Containers.StopContainerAsync(container.ID,
                            new ContainerStopParameters { WaitBeforeKillSeconds = timeout }, cancellationToken);
                        break;
                    case "batch-restart":
                        await _client.Containers.RestartContainerAsync(container.ID,
                            new ContainerRestartParameters { WaitBeforeKillSeconds = timeout }, cancellationToken);
                        break;
                    case "batch-delete":
                        await _client.Containers.RemoveContainerAsync(container.ID,
                            new ContainerRemoveParameters { RemoveVolumes = !preserveVolumes, Force = false }, cancellationToken);
                        break;
                    default:
                        throw new InvalidOperationException("Unsupported batch container action.");
                }
                completed.Add(name);
            }
            catch (DockerApiException exception)
            {
                failures.Add(new { container = name, status = (int)exception.StatusCode });
            }
        }
        return JsonSerializer.Serialize(new
        {
            action = action["batch-".Length..],
            completed,
            skipped,
            failures,
            partial = failures.Count > 0 || skipped.Count > 0
        });
    }

    private static string[] ParseBatchContainerIds(IReadOnlyDictionary<string, string> parameters)
    {
        try
        {
            var values = JsonSerializer.Deserialize<string[]>(Required(parameters, "containerIds")) ?? [];
            if (values.Length is < 1 or > 50 || values.Any(item => string.IsNullOrWhiteSpace(item) || item.Length > 128 ||
                item.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '_' or '.' or '-'))))
                throw new InvalidOperationException("Batch container ids are invalid.");
            return values.Distinct(StringComparer.Ordinal).ToArray();
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("containerIds must be a JSON string array.", exception);
        }
    }

    private static ContainerListResponse? FindContainer(IEnumerable<ContainerListResponse> containers, string value) =>
        containers.SingleOrDefault(item => item.ID.StartsWith(value, StringComparison.OrdinalIgnoreCase) ||
            item.Names.Any(name => string.Equals(name.TrimStart('/'), value.TrimStart('/'), StringComparison.Ordinal)));

    private static string BatchActionLabel(string action) => action switch
    {
        "start" => "启动",
        "stop" => "停止",
        "restart" => "重启",
        "delete" => "删除",
        _ => throw new InvalidOperationException("Unsupported batch container action.")
    };

    private async Task ValidateContainerCreationAsync(ContainerConfiguration configuration, CancellationToken cancellationToken)
    {
        var containers = await ListContainersAsync(true, cancellationToken);
        if (containers.Any(item => item.Names.Any(name => string.Equals(name.TrimStart('/'), configuration.Name, StringComparison.OrdinalIgnoreCase))))
            throw new InvalidOperationException("A container with the requested name already exists.");
        var requestedHostPorts = configuration.Ports.Values.SelectMany(item => item)
            .Select(item => ushort.Parse(item.HostPort, System.Globalization.CultureInfo.InvariantCulture)).ToHashSet();
        if (containers.SelectMany(item => item.Ports ?? []).Any(item =>
                item.PublicPort is > 0 and <= ushort.MaxValue && requestedHostPorts.Contains((ushort)item.PublicPort)))
            throw new InvalidOperationException("A requested host port is already published by another container.");

        if (configuration.Networks.Length == 0) return;
        var networks = await ListNetworksAsync(cancellationToken);
        var available = networks.Select(item => item.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var network in configuration.Networks)
        {
            if (!available.Contains(network)) throw new InvalidOperationException($"Docker network was not found: {network}");
            if (network.StartsWith("database-platform", StringComparison.OrdinalIgnoreCase) ||
                network.StartsWith("whaledeck", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Protected platform networks cannot be joined from the generic container workflow.");
        }
    }

    private async Task<ContainerListResponse> ResolveContainerAsync(string value, CancellationToken cancellationToken)
    {
        var containers = await ListContainersAsync(true, cancellationToken);
        var matches = containers.Where(item => item.ID.StartsWith(value, StringComparison.OrdinalIgnoreCase) ||
            item.Names.Any(name => string.Equals(name.TrimStart('/'), value.TrimStart('/'), StringComparison.Ordinal))).ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException("Container was not found."),
            _ => throw new InvalidOperationException("Container id is ambiguous.")
        };
    }

    private static ContainerConfiguration ParseContainerConfiguration(IReadOnlyDictionary<string, string> parameters)
    {
        var image = Required(parameters, "image");
        var name = Required(parameters, "name");
        ValidateImage(image);
        if (!ContainerNamePattern.IsMatch(name)) throw new InvalidOperationException("Container name is invalid.");
        if (name.StartsWith("database-platform-", StringComparison.OrdinalIgnoreCase) || name.StartsWith("whaledeck-", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Reserved container name prefix.");
        var restartPolicy = parameters.GetValueOrDefault("restartPolicy", "unless-stopped") switch
        {
            "no" => RestartPolicyKind.No,
            "unless-stopped" => RestartPolicyKind.UnlessStopped,
            "on-failure" => RestartPolicyKind.OnFailure,
            _ => throw new InvalidOperationException("Container restart policy is unsupported.")
        };
        var healthCommand = parameters.GetValueOrDefault("healthCommand")?.Trim();
        var versionPolicy = ValidateVersionPolicy(parameters.GetValueOrDefault("versionPolicy", "*"));
        var maintenanceWindow = parameters.GetValueOrDefault("maintenanceWindow", "Sun@20:00-23:59");
        if (!TryParseMaintenanceWindow(maintenanceWindow, out _, out _, out _))
            throw new InvalidOperationException("maintenanceWindow must use Day@HH:mm-HH:mm, for example Sun@20:00-23:59.");
        HealthConfig? healthcheck = null;
        if (!string.IsNullOrWhiteSpace(healthCommand))
        {
            if (healthCommand.Length > 1024 || healthCommand.Contains('\0') || healthCommand.Contains('\n'))
                throw new InvalidOperationException("Container health command is invalid.");
            healthcheck = new HealthConfig
            {
                Test = ["CMD-SHELL", healthCommand],
                Interval = TimeSpan.FromSeconds(ParseLong(parameters, "healthIntervalSeconds", 30, 5, 3600)),
                Timeout = TimeSpan.FromSeconds(ParseLong(parameters, "healthTimeoutSeconds", 5, 1, 300)),
                Retries = ParseLong(parameters, "healthRetries", 3, 1, 20)
            };
        }
        return new ContainerConfiguration(
            name,
            image,
            ParseLong(parameters, "memoryMb", 512, 32, 32768),
            ParseDouble(parameters, "cpus", 1, 0.1, 32),
            parameters.TryGetValue("autoUpdate", out var configured) && bool.TryParse(configured, out var enabled) && enabled,
            versionPolicy,
            maintenanceWindow,
            restartPolicy,
            ParseEnvironment(parameters.GetValueOrDefault("environment")),
            ParseStringArray(parameters.GetValueOrDefault("command"), "command"),
            ParseStringArray(parameters.GetValueOrDefault("entrypoint"), "entrypoint"),
            ParsePortBindings(parameters.GetValueOrDefault("ports")),
            ParseMounts(parameters.GetValueOrDefault("volumes")),
            ParseNetworks(parameters.GetValueOrDefault("networks")),
            ParseLabels(parameters.GetValueOrDefault("labels")),
            healthcheck);
    }

    private static List<string> ParseEnvironment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return [];
        if (value.Length > 64 * 1024 || value.Contains('\0')) throw new InvalidOperationException("Container environment is too large or invalid.");
        var result = new List<string>();
        foreach (var line in value.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = line.IndexOf('=');
            if (separator < 1 || !EnvironmentNamePattern.IsMatch(line[..separator]))
                throw new InvalidOperationException("A container environment variable name is invalid.");
            result.Add(line);
        }
        return result;
    }

    private static string[]? ParseStringArray(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            var values = JsonSerializer.Deserialize<string[]>(value);
            if (values is null || values.Length > 64 || values.Any(item => string.IsNullOrEmpty(item) || item.Length > 2048 || item.Contains('\0')))
                throw new InvalidOperationException($"Container {name} is invalid.");
            return values;
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"Container {name} must be a JSON string array.", exception);
        }
    }

    private static Dictionary<string, IList<PortBinding>> ParsePortBindings(string? value)
    {
        var result = new Dictionary<string, IList<PortBinding>>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(value)) return result;
        foreach (var mapping in value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!PortPattern.IsMatch(mapping)) throw new InvalidOperationException("A container port mapping is invalid.");
            var protocol = mapping.EndsWith("/udp", StringComparison.OrdinalIgnoreCase) ? "udp" : "tcp";
            var withoutProtocol = mapping.EndsWith("/tcp", StringComparison.OrdinalIgnoreCase) || mapping.EndsWith("/udp", StringComparison.OrdinalIgnoreCase)
                ? mapping[..^4]
                : mapping;
            var parts = withoutProtocol.Split(':');
            if (parts.Length != 3 || !ushort.TryParse(parts[1], out var hostPort) || !ushort.TryParse(parts[2], out var containerPort) ||
                hostPort == 0 || containerPort == 0)
                throw new InvalidOperationException("A container port mapping is outside the valid range.");
            var key = $"{containerPort}/{protocol}";
            if (!result.TryAdd(key, [new PortBinding { HostIP = parts[0], HostPort = hostPort.ToString(System.Globalization.CultureInfo.InvariantCulture) }]))
                throw new InvalidOperationException("A container port is mapped more than once.");
        }
        return result;
    }

    private static List<Mount> ParseMounts(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return [];
        var result = new List<Mount>();
        foreach (var mapping in value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = mapping.Split(':', StringSplitOptions.TrimEntries);
            if (parts.Length is < 2 or > 3 || !VolumeNamePattern.IsMatch(parts[0]) || !parts[1].StartsWith('/') ||
                parts[1].Contains("..", StringComparison.Ordinal) || parts[1].Contains('\0') ||
                parts[0].StartsWith("database-platform", StringComparison.OrdinalIgnoreCase) || parts[0].StartsWith("whaledeck", StringComparison.OrdinalIgnoreCase) ||
                parts.Length == 3 && parts[2] is not ("ro" or "rw"))
                throw new InvalidOperationException("A container volume mapping is invalid.");
            result.Add(new Mount { Type = "volume", Source = parts[0], Target = parts[1], ReadOnly = parts.Length == 3 && parts[2] == "ro" });
        }
        return result;
    }

    private static string[] ParseNetworks(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return [];
        return value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(item => NetworkNamePattern.IsMatch(item) ? item : throw new InvalidOperationException("A Docker network name is invalid."))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static Dictionary<string, string> ParseLabels(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            var labels = JsonSerializer.Deserialize<Dictionary<string, string>>(value) ?? new Dictionary<string, string>();
            if (labels.Count > 32 || labels.Any(item => !LabelNamePattern.IsMatch(item.Key) || item.Value.Length > 1024 ||
                item.Value.Any(char.IsControl) || item.Key.StartsWith("io.whaledeck.", StringComparison.OrdinalIgnoreCase) ||
                item.Key.StartsWith("com.docker.compose.", StringComparison.OrdinalIgnoreCase) ||
                IsSensitiveLabelName(item.Key)))
                throw new InvalidOperationException("Container labels contain an invalid or reserved entry.");
            return new Dictionary<string, string>(labels, StringComparer.Ordinal);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Container labels must be a JSON object of string values.", exception);
        }
    }

    private static bool IsSensitiveLabelName(string key) =>
        key.Contains("password", StringComparison.OrdinalIgnoreCase) || key.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("token", StringComparison.OrdinalIgnoreCase) || key.Contains("credential", StringComparison.OrdinalIgnoreCase);

    private static bool IsAutoUpdateEnabled(IDictionary<string, string> labels) =>
        (labels.TryGetValue("io.whaledeck.autoupdate", out var managed) && string.Equals(managed, "true", StringComparison.OrdinalIgnoreCase)) ||
        (labels.TryGetValue("autoupdate", out var standard) && string.Equals(standard, "true", StringComparison.OrdinalIgnoreCase));

    private static string ValidateVersionPolicy(string policy)
    {
        policy = policy.Trim();
        if (policy == "*") return policy;
        var candidate = policy.StartsWith(">=", StringComparison.Ordinal) ? policy[2..].TrimStart('v') : policy.TrimStart('v');
        if (candidate.EndsWith(".*", StringComparison.Ordinal))
        {
            var components = candidate[..^2].Split('.');
            if (components.Length is 1 or 2 && components.All(item => int.TryParse(item, out _))) return policy;
        }
        else if (System.Version.TryParse(candidate, out _))
        {
            return policy;
        }
        throw new InvalidOperationException("versionPolicy must be *, 1.*, 1.2.*, >=1.2.3 or an exact version.");
    }

    private static bool VersionMatchesPolicy(string version, string policy)
    {
        ValidateVersionPolicy(policy);
        if (policy == "*") return true;
        if (!System.Version.TryParse(version.TrimStart('v'), out var candidate)) return false;
        if (policy.EndsWith(".*", StringComparison.Ordinal))
        {
            var prefix = policy[..^2].Split('.');
            return candidate.Major.ToString(System.Globalization.CultureInfo.InvariantCulture) == prefix[0] &&
                   (prefix.Length == 1 || candidate.Minor.ToString(System.Globalization.CultureInfo.InvariantCulture) == prefix[1]);
        }
        if (policy.StartsWith(">=", StringComparison.Ordinal) && System.Version.TryParse(policy[2..].TrimStart('v'), out var minimum))
            return candidate >= minimum;
        return System.Version.TryParse(policy.TrimStart('v'), out var exact) && candidate == exact;
    }

    private static bool IsInMaintenanceWindow(string window, DateTimeOffset utcNow)
    {
        if (!TryParseMaintenanceWindow(window, out var day, out var start, out var end))
            throw new InvalidOperationException("maintenanceWindow must use Day@HH:mm-HH:mm, for example Sun@20:00-23:59.");
        var local = TimeZoneInfo.ConvertTime(utcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai"));
        var time = TimeOnly.FromDateTime(local.DateTime);
        return local.DayOfWeek == day && time >= start && time <= end;
    }

    private static bool TryParseMaintenanceWindow(string window, out DayOfWeek day, out TimeOnly start, out TimeOnly end)
    {
        day = default;
        start = default;
        end = default;
        var parts = window.Split('@', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2) return false;
        day = parts[0].ToLowerInvariant() switch
        {
            "sun" or "sunday" => DayOfWeek.Sunday,
            "mon" or "monday" => DayOfWeek.Monday,
            "tue" or "tuesday" => DayOfWeek.Tuesday,
            "wed" or "wednesday" => DayOfWeek.Wednesday,
            "thu" or "thursday" => DayOfWeek.Thursday,
            "fri" or "friday" => DayOfWeek.Friday,
            "sat" or "saturday" => DayOfWeek.Saturday,
            _ => (DayOfWeek)(-1)
        };
        if (!Enum.IsDefined(day)) return false;
        var times = parts[1].Split('-', 2, StringSplitOptions.TrimEntries);
        return times.Length == 2 && TimeOnly.TryParseExact(times[0], "HH:mm", out start) &&
               TimeOnly.TryParseExact(times[1], "HH:mm", out end) && start <= end;
    }

    private static string BuildRollbackName(string name, string jobId)
    {
        var suffix = Regex.Replace(jobId, "[^a-zA-Z0-9]", string.Empty, RegexOptions.CultureInvariant);
        suffix = suffix.Length > 8 ? suffix[..8] : suffix;
        var maximumBaseLength = Math.Max(1, 63 - suffix.Length - 4);
        return $"{name[..Math.Min(name.Length, maximumBaseLength)]}-old-{suffix}";
    }

    private static Dictionary<string, string> NormalizePlanParameters(IReadOnlyDictionary<string, string> parameters)
    {
        var normalized = new Dictionary<string, string>(parameters, StringComparer.Ordinal);
        normalized.Remove("password");
        normalized.Remove("environment");
        normalized.Remove("inputTicket");
        return normalized;
    }

    private static string Required(IReadOnlyDictionary<string, string> parameters, string key) =>
        parameters.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : throw new InvalidOperationException($"Required Docker parameter is missing: {key}");

    private static bool ParseBoolean(IReadOnlyDictionary<string, string> parameters, string key, bool fallback) =>
        !parameters.TryGetValue(key, out var value) ? fallback :
        bool.TryParse(value, out var parsed) ? parsed : throw new InvalidOperationException($"Docker parameter must be true or false: {key}");

    private static string ValidateDockerResourceName(string name, string kind)
    {
        if (!NetworkNamePattern.IsMatch(name)) throw new InvalidOperationException($"Docker {kind} name is invalid.");
        if (IsProtectedDockerResourceName(name)) throw new InvalidOperationException($"Docker {kind} name uses a reserved platform prefix.");
        return name;
    }

    private static bool IsProtectedDockerResourceName(string name) =>
        name.StartsWith("database-platform", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("whaledeck", StringComparison.OrdinalIgnoreCase);

    private static void ValidateImage(string image)
    {
        if (!ImagePattern.IsMatch(image) || image.Contains("..", StringComparison.Ordinal))
            throw new InvalidOperationException("Image reference is invalid.");
        var first = image.Split('/')[0];
        var hasExplicitRegistry = image.Contains('/', StringComparison.Ordinal) &&
            (first.Contains('.', StringComparison.Ordinal) || first.Contains(':', StringComparison.Ordinal) || first == "localhost");
        if (hasExplicitRegistry && first is not ("docker.io" or "ghcr.io" or "quay.io" or "xuanyuan.cloud" or "docker.xuanyuan.run" or "registry.cn-hangzhou.aliyuncs.com"))
            throw new InvalidOperationException("The image registry is not approved.");
    }

    private static (string Name, string Tag) SplitImage(string image)
    {
        if (image.Contains('@', StringComparison.Ordinal)) return (image, string.Empty);
        var slash = image.LastIndexOf('/');
        var colon = image.LastIndexOf(':');
        return colon > slash ? (image[..colon], image[(colon + 1)..]) : (image, "latest");
    }

    private static long ParseLong(IReadOnlyDictionary<string, string> parameters, string key, long fallback, long minimum, long maximum)
    {
        if (!parameters.TryGetValue(key, out var value)) return fallback;
        return long.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var parsed) && parsed >= minimum && parsed <= maximum
            ? parsed : throw new InvalidOperationException($"Docker parameter is out of range: {key}");
    }

    private static double ParseDouble(IReadOnlyDictionary<string, string> parameters, string key, double fallback, double minimum, double maximum)
    {
        if (!parameters.TryGetValue(key, out var value)) return fallback;
        return double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var parsed) && parsed >= minimum && parsed <= maximum
            ? parsed : throw new InvalidOperationException($"Docker parameter is out of range: {key}");
    }

    private static async Task<bool> TryAsync(Func<Task> action)
    {
        try
        {
            await action();
            return true;
        }
        catch (DockerApiException)
        {
            return false;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    private sealed record ContainerConfiguration(
        string Name,
        string Image,
        long MemoryMb,
        double Cpus,
        bool AutoUpdate,
        string VersionPolicy,
        string MaintenanceWindow,
        RestartPolicyKind RestartPolicy,
        List<string> Environment,
        string[]? Command,
        string[]? Entrypoint,
        Dictionary<string, IList<PortBinding>> Ports,
        List<Mount> Mounts,
        string[] Networks,
        Dictionary<string, string> Labels,
        HealthConfig? Healthcheck);

    private sealed record ContainerUpdatePlan(
        string Id,
        string Name,
        string Image,
        string OldImageId,
        string VersionPolicy);

    private sealed record BatchContainerPlan(
        string Action,
        IReadOnlyCollection<string> Affected,
        IReadOnlyCollection<string> Skipped);

    private sealed class ContainerUpdateException(string message, string resultJson, bool rolledBack, Exception innerException)
        : InvalidOperationException(message, innerException)
    {
        public string ResultJson { get; } = resultJson;
        public bool RolledBack { get; } = rolledBack;
    }
}
