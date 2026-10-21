using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Configuration;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Infrastructure.Agent;

public sealed partial class AgentGateway : IAgentGateway, IManagementQuery, IHostProcessGateway, INotificationPrivateGateway, IDisposable
{
    private readonly GrpcChannel _channel;
    private readonly AgentService.AgentServiceClient _agent;
    private readonly HostService.HostServiceClient _host;
    private readonly DockerService.DockerServiceClient _docker;
    private readonly ManagedResourceService.ManagedResourceServiceClient _resources;
    private readonly OperationService.OperationServiceClient _operations;
    private readonly byte[]? _capabilityKey;
    private readonly FileRecoveryMaterial _recoveryMaterial;

    public AgentGateway(IConfiguration configuration)
    {
        var recoveryProtector = new FilePlanProtector(configuration);
        _recoveryMaterial = new(recoveryProtector, recoveryProtector, recoveryProtector, recoveryProtector, recoveryProtector, recoveryProtector, recoveryProtector, recoveryProtector, recoveryProtector, recoveryProtector, recoveryProtector);
        var socketPath = configuration["Agent:SocketPath"] ?? "/run/whaledeck/agent.sock";
        var keyFile = configuration["Agent:CapabilityKeyFile"] ?? "/run/secrets/agent-capability.key";
        _capabilityKey = File.Exists(keyFile) ? File.ReadAllBytes(keyFile) : null;
        var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (_, cancellationToken) =>
            {
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
        };
        _channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions { HttpHandler = handler });
        _agent = new AgentService.AgentServiceClient(_channel);
        _host = new HostService.HostServiceClient(_channel);
        _docker = new DockerService.DockerServiceClient(_channel);
        _resources = new ManagedResourceService.ManagedResourceServiceClient(_channel);
        _operations = new OperationService.OperationServiceClient(_channel);
    }

    public async Task<AgentHealthDto> GetHealthAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await _agent.GetHealthAsync(new Empty(), cancellationToken: cancellationToken);
            return new AgentHealthDto(response.Healthy, response.DockerAvailable, response.SystemdAvailable, response.Status, response.ObservedAtUtc.ToDateTimeOffset());
        }
        catch (RpcException exception) when (exception.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded)
        {
            return new AgentHealthDto(false, false, false, "AgentUnavailable", DateTimeOffset.UtcNow);
        }
    }

    public async Task<AgentCapabilitiesDto> GetCapabilitiesAsync(CancellationToken cancellationToken)
    {
        var response = await _agent.GetCapabilitiesAsync(new Empty(), cancellationToken: cancellationToken);
        return new AgentCapabilitiesDto(response.AgentVersion, checked((int)response.ProtocolMajor),
            checked((int)response.ProtocolMinor), response.Capabilities.ToArray());
    }

    public async Task<HostInfoDto> GetHostInfoAsync(CancellationToken cancellationToken)
    {
        var response = await _host.GetHostInfoAsync(new Empty(), Headers("/whaledeck.agent.v1.HostService/GetHostInfo"), cancellationToken: cancellationToken);
        return new HostInfoDto(response.HostName, response.Distribution, response.KernelVersion, response.Architecture,
            DateTimeOffset.FromUnixTimeSeconds(response.BootTimeUnixSeconds), TimeSpan.FromSeconds(response.UptimeSeconds),
            checked((int)response.LogicalProcessorCount), response.TotalMemoryBytes,
            response.Addresses.Select(item => new HostAddressDto(item.InterfaceName, item.Address)).ToArray());
    }

    public async Task<HostProcessPageDto> ListProcessesAsync(HostProcessQuery query, CancellationToken cancellationToken)
    {
        await EnsureProcessCapabilityAsync(cancellationToken);
        var response = await _host.ListProcessesAsync(new HostProcessesRequest
        {
            Page = checked((uint)query.Page), PageSize = checked((uint)query.PageSize), Sort = query.Sort,
            Descending = query.Descending, Filter = query.Filter ?? string.Empty
        }, Headers("/whaledeck.agent.v1.HostService/ListProcesses"), cancellationToken: cancellationToken);
        return new(response.Items.Select(MapProcess).ToArray(), response.Total, checked((int)response.Page), checked((int)response.PageSize),
            response.SampledAtUtc.ToDateTimeOffset(), response.CpuWindowSeconds, response.IsStale, response.IsTruncated, response.SkippedCount);
    }

    public async Task<HostProcessDto> GetProcessAsync(int pid, string instanceId, CancellationToken cancellationToken)
    {
        await EnsureProcessCapabilityAsync(cancellationToken);
        var response = await _host.GetProcessAsync(new HostProcessRequest { Pid = pid, InstanceId = instanceId },
            Headers("/whaledeck.agent.v1.HostService/GetProcess"), cancellationToken: cancellationToken);
        return MapProcess(response);
    }

    private async Task EnsureProcessCapabilityAsync(CancellationToken cancellationToken)
    {
        var capabilities = await GetCapabilitiesAsync(cancellationToken);
        if (capabilities.ProtocolMajor != 1 || !capabilities.Capabilities.Contains("host.processes"))
            throw new RpcException(new Status(StatusCode.Unimplemented, "Agent process capability is unavailable."));
    }

    private static HostProcessDto MapProcess(HostProcessSnapshot item) =>
        new(item.Pid, item.ParentPid, item.UserId, item.InstanceId, item.Name, item.State, item.ThreadCount,
            item.StartedAtUtc.ToDateTimeOffset(), item.CpuTimeSeconds, item.HasCpuPercent ? item.CpuPercent : null,
            item.MemoryBytes, item.VirtualMemoryBytes);

    public async Task<IReadOnlyCollection<JournalEntryDto>> QueryJournalAsync(
        string? unit,
        int take,
        int sinceMinutes,
        string? priority,
        string? keyword,
        CancellationToken cancellationToken)
    {
        var response = await _host.QueryJournalAsync(new JournalRequest
        {
            Unit = unit ?? string.Empty,
            Take = checked((uint)Math.Clamp(take, 1, 500)),
            SinceMinutes = checked((uint)Math.Clamp(sinceMinutes, 1, 10_080)),
            Priority = priority ?? string.Empty,
            Keyword = keyword ?? string.Empty
        }, Headers("/whaledeck.agent.v1.HostService/QueryJournal"), cancellationToken: cancellationToken);
        return response.Entries.Select(item => new JournalEntryDto(
            item.OccurredAtUtc.ToDateTimeOffset(), item.Unit, item.Priority, item.Process,
            item.ProcessId, item.Message)).ToArray();
    }

    public async Task<IReadOnlyCollection<ContainerDto>> ListContainersAsync(bool includeStopped, CancellationToken cancellationToken)
    {
        var response = await _docker.ListContainersAsync(new ListContainersRequest { IncludeStopped = includeStopped }, Headers("/whaledeck.agent.v1.DockerService/ListContainers"), cancellationToken: cancellationToken);
        return response.Containers.Select(item => new ContainerDto(item.Id, item.Name, item.Image, item.State, item.Status,
            item.ProtectedResource, item.ProtectionLevel, item.Labels, item.Ports, item.Networks, item.Addresses,
            item.CreatedAtUtc?.ToDateTimeOffset(), item.ImageId, item.VolumeNames, item.NetworkIds)).ToArray();
    }

    public async Task<ContainerLogsDto> GetContainerLogsAsync(string containerId, int tail, int sinceMinutes, CancellationToken cancellationToken)
    {
        var response = await _docker.GetContainerLogsAsync(new ContainerLogsRequest
        {
            ContainerId = containerId,
            Tail = checked((uint)Math.Clamp(tail, 1, 2_000)),
            SinceMinutes = checked((uint)Math.Clamp(sinceMinutes, 1, 10_080))
        }, Headers("/whaledeck.agent.v1.DockerService/GetContainerLogs"), cancellationToken: cancellationToken);
        return new ContainerLogsDto(response.Lines.ToArray(), response.Truncated);
    }

    public async Task<ContainerStatsDto> GetContainerStatsAsync(string containerId, CancellationToken cancellationToken)
    {
        var response = await _docker.GetContainerStatsAsync(
            new ResourceReference { ResourceId = containerId, ResourceType = "Container" },
            Headers("/whaledeck.agent.v1.DockerService/GetContainerStats"),
            cancellationToken: cancellationToken);
        return new ContainerStatsDto(response.Values);
    }

    public async Task<ContainerUsageBatchDto> GetContainerUsageAsync(CancellationToken cancellationToken)
    {
        var capabilities = await GetCapabilitiesAsync(cancellationToken);
        if (capabilities.ProtocolMajor != 1 || !capabilities.Capabilities.Contains("docker.usage"))
            throw new RpcException(new Status(StatusCode.Unimplemented, "Agent container usage capability is unavailable."));
        var response = await _docker.GetContainerUsageAsync(new Empty(), Headers("/whaledeck.agent.v1.DockerService/GetContainerUsage"), cancellationToken: cancellationToken);
        return new(response.Items.Select(item => new ContainerUsageDto(item.ContainerId, item.State, item.SampledAtUtc?.ToDateTimeOffset(),
            item.HasCpuPercent ? item.CpuPercent : null, item.CpuWindowSeconds,
            item.HasMemoryUsedBytes ? item.MemoryUsedBytes : null, item.HasMemoryLimitBytes ? item.MemoryLimitBytes : null,
            item.HasMemoryPercent ? item.MemoryPercent : null, item.HasNetworkReceiveBytes ? item.NetworkReceiveBytes : null,
            item.HasNetworkSendBytes ? item.NetworkSendBytes : null, item.HasBlockReadBytes ? item.BlockReadBytes : null,
            item.HasBlockWriteBytes ? item.BlockWriteBytes : null, item.HasPids ? item.Pids : null, item.Quality)).ToArray(),
            response.SampledAtUtc?.ToDateTimeOffset(), response.IsStale, response.IsTruncated, response.SkippedCount, response.TotalCount, response.IntervalSeconds);
    }

    public async Task<IReadOnlyCollection<DockerEventDto>> ListDockerEventsAsync(
        DateTimeOffset sinceUtc,
        int take,
        CancellationToken cancellationToken)
    {
        DockerEventsResponse response;
        try
        {
            response = await _docker.ListEventsAsync(new DockerEventsRequest
            {
                SinceUnixSeconds = sinceUtc.ToUnixTimeSeconds(),
                Take = checked((uint)Math.Clamp(take, 1, 1_000))
            }, Headers("/whaledeck.agent.v1.DockerService/ListEvents"), cancellationToken: cancellationToken);
        }
        catch (RpcException exception) when (exception.StatusCode == StatusCode.Unimplemented)
        {
            return [];
        }
        return response.Events.Select(item => new DockerEventDto(
            item.Fingerprint,
            item.OccurredAtUtc.ToDateTimeOffset(),
            item.EventType,
            item.Action,
            item.ResourceId,
            item.ResourceName,
            string.IsNullOrWhiteSpace(item.Image) ? null : item.Image,
            item.Attributes)).ToArray();
    }

    public async Task<ContainerInspectDto> InspectContainerAsync(string containerId, CancellationToken cancellationToken)
    {
        var response = await _docker.InspectContainerAsync(
            new ResourceReference { ResourceId = containerId, ResourceType = "Container" },
            Headers("/whaledeck.agent.v1.DockerService/InspectContainer"),
            cancellationToken: cancellationToken);
        return new ContainerInspectDto(response.Id, response.Name, response.Image, response.EnvironmentNames.ToArray(),
            response.Command.ToArray(), response.Entrypoint.ToArray(), response.Labels, response.Ports.ToArray(),
            response.Volumes.ToArray(), response.Networks.ToArray(), response.RestartPolicy, response.Cpus,
            response.MemoryMb, response.HealthCommand.ToArray(), response.HealthIntervalSeconds,
            response.HealthTimeoutSeconds, response.HealthRetries, response.Relationships is { } relation
                ? new ContainerRelationshipsDto(relation.ImageId, relation.ComposeProject, relation.ComposeService,
                    relation.NetworkMode, relation.Endpoints.Select(item => new ContainerNetworkEndpointDto(item.Name,
                        item.NetworkId, item.Ipv4Address, item.Ipv4PrefixLength, item.Ipv4Gateway, item.Ipv6Address,
                        item.Ipv6PrefixLength, item.Ipv6Gateway, item.MacAddress)).ToArray(),
                    relation.Mounts.Select(item => new ContainerMountDto(item.Type, item.Name, item.Source,
                        item.Destination, item.ReadWrite)).ToArray(),
                    relation.PortBindings.Select(item => new ContainerPortBindingDto(item.ContainerPort,
                        item.HostAddress, item.HostPort, item.Published)).ToArray(), relation.IsTruncated)
                : null);
    }

    public async Task<IReadOnlyCollection<ManagedResourceDto>> ListResourcesAsync(string kind, CancellationToken cancellationToken)
    {
        return kind.ToLowerInvariant() switch
        {
            "databases" => Map(await _resources.ListDatabaseInstancesAsync(new Empty(), Headers("/whaledeck.agent.v1.ManagedResourceService/ListDatabaseInstances"), cancellationToken: cancellationToken)),
            "systemd" => Map(await _resources.ListSystemdUnitsAsync(new Empty(), Headers("/whaledeck.agent.v1.ManagedResourceService/ListSystemdUnits"), cancellationToken: cancellationToken)),
            "compose-projects" or "applications" => Map(await _resources.ListComposeProjectsAsync(new Empty(), Headers("/whaledeck.agent.v1.ManagedResourceService/ListComposeProjects"), cancellationToken: cancellationToken)),
            "images" => Map(await _docker.ListImagesAsync(new Empty(), Headers("/whaledeck.agent.v1.DockerService/ListImages"), cancellationToken: cancellationToken)),
            "networks" => Map(await _docker.ListNetworksAsync(new Empty(), Headers("/whaledeck.agent.v1.DockerService/ListNetworks"), cancellationToken: cancellationToken)),
            "volumes" => Map(await _docker.ListVolumesAsync(new Empty(), Headers("/whaledeck.agent.v1.DockerService/ListVolumes"), cancellationToken: cancellationToken)),
            "host-network-interfaces" => Map(await _host.ListNetworkInterfacesAsync(new Empty(), Headers("/whaledeck.agent.v1.HostService/ListNetworkInterfaces"), cancellationToken: cancellationToken)),
            "host-storage-devices" => Map(await _host.ListStorageDevicesAsync(new Empty(), Headers("/whaledeck.agent.v1.HostService/ListStorageDevices"), cancellationToken: cancellationToken)),
            "overview" => (await ListContainersAsync(true, cancellationToken))
                .Where(IsOverviewContainer)
                .Select(item => new ManagedResourceDto(item.Id, item.Name, "Container", item.State, item.Image, item.IsProtected,
                    new Dictionary<string, string>(item.Labels) { ["dockerStatus"] = item.Status }))
                .ToArray(),
            _ => []
        };
    }

    public async Task<ManagedResourceDto> GetConfigRepositoryStatusAsync(CancellationToken cancellationToken)
    {
        var response = await _resources.GetConfigRepositoryStatusAsync(
            new RegisteredResourceRequest
            {
                Resource = new ResourceReference
                {
                    ResourceId = "database-platform.repository",
                    ResourceType = "ConfigRepository"
                }
            },
            Headers("/whaledeck.agent.v1.ManagedResourceService/GetConfigRepositoryStatus"),
            cancellationToken: cancellationToken);
        return Map(response);
    }

    public async Task<DockerSettingsDto> GetDockerSettingsAsync(CancellationToken cancellationToken)
    {
        var response = await _resources.GetDockerSettingsAsync(
            new Empty(),
            Headers("/whaledeck.agent.v1.ManagedResourceService/GetDockerSettings"),
            cancellationToken: cancellationToken);
        return new DockerSettingsDto(response.SettingsJson, response.EditableKeys);
    }

    public async Task<ApplicationImageMetadataDto> InspectApplicationImageAsync(string image, bool pullIfMissing, CancellationToken cancellationToken)
    {
        var response = await _docker.InspectImageAsync(
            new ImageMetadataRequest { Image = image, PullIfMissing = pullIfMissing },
            Headers("/whaledeck.agent.v1.DockerService/InspectImage"),
            cancellationToken: cancellationToken);
        return new ApplicationImageMetadataDto(response.Image, response.ImageId, response.Environment, response.ExposedPorts,
            response.Volumes, response.Entrypoint, response.Command, response.Labels);
    }

    public async Task<PlanDto> PlanAsync(string area, string action, string? resourceId, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        var normalizedParameters = new Dictionary<string, string>(parameters, StringComparer.Ordinal);
        normalizedParameters.Remove("password");
        if (!(area == "applications" && action == "configure")) normalizedParameters.Remove("environment");
        normalizedParameters.Remove("inputTicket");
        if (area.Equals("containers", StringComparison.OrdinalIgnoreCase) && action.Equals("delete", StringComparison.OrdinalIgnoreCase))
        {
            normalizedParameters["timeoutSeconds"] = parameters.TryGetValue("timeoutSeconds", out var timeout) && uint.TryParse(timeout, out var seconds)
                ? seconds.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : "10";
            normalizedParameters["preserveVolumes"] = (!parameters.TryGetValue("preserveVolumes", out var preserve) || !bool.TryParse(preserve, out var preserveValue) || preserveValue)
                .ToString().ToLowerInvariant();
        }
        var request = new RegisteredActionRequest
        {
            Action = action,
            Resource = new ResourceReference { ResourceId = resourceId ?? string.Empty, ResourceType = area }
        };
        request.Parameters.Add(normalizedParameters);
        var useDockerPlanner = area.Equals("containers", StringComparison.OrdinalIgnoreCase) ||
            area.Equals("docker", StringComparison.OrdinalIgnoreCase) && action.ToLowerInvariant() is
                "network-create" or "network-delete" or "volume-delete";
        var response = useDockerPlanner
            ? await _docker.PlanActionAsync(request, Headers("/whaledeck.agent.v1.DockerService/PlanAction"), cancellationToken: cancellationToken)
            : await _resources.PlanActionAsync(request, Headers("/whaledeck.agent.v1.ManagedResourceService/PlanAction"), cancellationToken: cancellationToken);
        return new PlanDto(response.PlanHash, response.ExpiresAtUtc.ToDateTimeOffset(), response.Changes, response.Warnings);
    }

    public async Task<BackupTargetCapacityDto> GetBackupTargetCapacityAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await _resources.GetBackupTargetCapacityAsync(new Empty(),
                Headers("/whaledeck.agent.v1.ManagedResourceService/GetBackupTargetCapacity"),
                deadline: DateTime.UtcNow.AddSeconds(15), cancellationToken: cancellationToken);
            var capacity = new BackupTargetCapacityDto(response.TargetDirectoryId, response.Available, response.TotalBytes,
                response.UsedBytes, response.FreeBytes, response.ObservedAtUtc?.ToDateTimeOffset(), string.IsNullOrEmpty(response.ErrorCode) ? null : "BACKUP_CAPACITY_UNAVAILABLE");
            return capacity.IsCurrent(DateTimeOffset.UtcNow) ? capacity : BackupTargetCapacityDto.Unknown("BACKUP_CAPACITY_UNAVAILABLE");
        }
        catch (RpcException exception) when (exception.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded or StatusCode.Unimplemented)
        {
            return BackupTargetCapacityDto.Unknown("BACKUP_CAPACITY_UNAVAILABLE");
        }
    }

    public async Task<ApplicationConfigurationDto> GetApplicationConfigurationAsync(string resourceId, CancellationToken cancellationToken)
    {
        var response = await _resources.GetApplicationConfigurationAsync(new RegisteredResourceRequest
        {
            Resource = new ResourceReference { ResourceId = resourceId, ResourceType = "applications" }
        }, Headers("/whaledeck.agent.v1.ManagedResourceService/GetApplicationConfiguration"), cancellationToken: cancellationToken);
        return new ApplicationConfigurationDto(response.ResourceId, response.Fingerprint, response.Services.Select(item =>
            new ApplicationServiceConfigurationDto(item.Name, item.Image, item.Cpus, item.MemoryMb, item.RestartPolicy,
                item.Ports, item.CommandPresent, item.EntrypointPresent, item.EnvironmentNames,
                item.Mounts.Select(mount => new ApplicationConfigurationMountDto(mount.Type, mount.Source, mount.Target, mount.ReadOnly)).ToArray(),
                item.Networks, item.NetworkMode)).ToArray(), response.EnvironmentNames, response.AutoUpdate, response.VersionPolicy,
            response.MaintenanceWindow, response.Dependencies, response.ApprovedNetworks, response.OwnedDataDirectory);
    }

    public async Task<IReadOnlyCollection<MetricValueDto>> GetMetricsSnapshotAsync(CancellationToken cancellationToken)
    {
        // The Agent owns the canonical six-second sample; Worker persists it. Ask for exactly
        // one batch so a normal sample does not end by canceling a live gRPC
        // stream and produce a stack trace every collection cycle.
        using var call = _host.StreamMetrics(new MetricsRequest { IntervalSeconds = 0 },
            Headers("/whaledeck.agent.v1.HostService/StreamMetrics"), cancellationToken: cancellationToken);
        var values = new List<MetricValueDto>();
        while (await call.ResponseStream.MoveNext(cancellationToken))
        {
            var item = call.ResponseStream.Current;
            if (item.MetricKind == "batch.complete") break;
            values.Add(new MetricValueDto(item.MetricKind, item.DeviceId, item.Value, item.Unit, item.Quality, item.SampledAtUtc.ToDateTimeOffset()));
        }
        return values;
    }

    public async Task<IReadOnlyCollection<MetricValueDto>> GetRecentMetricsAsync(DateTimeOffset? afterUtc, CancellationToken cancellationToken)
    {
        var capabilities = await GetCapabilitiesAsync(cancellationToken);
        if (capabilities.ProtocolMajor != 1 || capabilities.ProtocolMinor < 37 || !capabilities.Capabilities.Contains("host.metrics.replay", StringComparer.Ordinal))
            throw new InvalidOperationException("HOST_METRIC_REPLAY_UNSUPPORTED");
        var request = new MetricsRequest { ReplayRecent = true };
        if (afterUtc is not null) request.AfterSampledAtUtc = Timestamp.FromDateTimeOffset(afterUtc.Value);
        using var call = _host.StreamMetrics(request, Headers("/whaledeck.agent.v1.HostService/StreamMetrics"),
            deadline: DateTime.UtcNow.AddSeconds(15), cancellationToken: cancellationToken);
        return await HostMetricReplayReader.ReadAsync(call.ResponseStream, afterUtc, cancellationToken);
    }

    public async Task<AgentOperationDto> ExecuteAsync(string area, string action, string resourceId, IReadOnlyDictionary<string, string> parameters, string? planHash, Guid jobId, string idempotencyKey, CancellationToken cancellationToken)
    {
        var requestContext = new RequestContext { JobId = jobId.ToString("D"), IdempotencyKey = idempotencyKey };
        OperationHandle response;
        if (area.Equals("containers", StringComparison.OrdinalIgnoreCase) && System.Enum.TryParse<ContainerAction>(action, true, out var containerAction))
        {
            response = await _docker.ChangeContainerStateAsync(new ChangeContainerStateRequest
            {
                Context = requestContext,
                ContainerId = resourceId,
                Action = containerAction,
                TimeoutSeconds = parameters.TryGetValue("timeoutSeconds", out var timeout) && uint.TryParse(timeout, out var seconds) ? seconds : 10,
                PreserveVolumes = !parameters.TryGetValue("preserveVolumes", out var preserve) || !bool.TryParse(preserve, out var preserveValue) || preserveValue,
                PlanHash = planHash ?? string.Empty
            }, Headers("/whaledeck.agent.v1.DockerService/ChangeContainerState"), cancellationToken: cancellationToken);
        }
        else
        {
            var request = new RegisteredActionRequest
            {
                Context = requestContext,
                Action = action,
                Resource = new ResourceReference { ResourceId = resourceId, ResourceType = area },
                PlanHash = planHash ?? string.Empty
            };
            request.Parameters.Add(parameters.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal));
            response = area.Equals("docker", StringComparison.OrdinalIgnoreCase) && action is "network-create" or "network-delete" or "volume-delete"
                ? await _docker.RunActionAsync(request, Headers("/whaledeck.agent.v1.DockerService/RunAction"), cancellationToken: cancellationToken)
                : area.ToLowerInvariant() switch
                {
                    "containers" => await _docker.RunActionAsync(request, Headers("/whaledeck.agent.v1.DockerService/RunAction"), cancellationToken: cancellationToken),
                    "databases" or "backups" => await _resources.RunDatabaseActionAsync(request, Headers("/whaledeck.agent.v1.ManagedResourceService/RunDatabaseAction"), cancellationToken: cancellationToken),
                    "host" => await _resources.RunSystemdActionAsync(request, Headers("/whaledeck.agent.v1.ManagedResourceService/RunSystemdAction"), cancellationToken: cancellationToken),
                    "applications" or "compose-projects" => await _resources.RunComposeActionAsync(request, Headers("/whaledeck.agent.v1.ManagedResourceService/RunComposeAction"), cancellationToken: cancellationToken),
                    "config-repository" => await _resources.RunConfigRepositoryActionAsync(request, Headers("/whaledeck.agent.v1.ManagedResourceService/RunConfigRepositoryAction"), cancellationToken: cancellationToken),
                    "platform" or "docker" => await _resources.RunPlatformMaintenanceAsync(request, Headers("/whaledeck.agent.v1.ManagedResourceService/RunPlatformMaintenance"), cancellationToken: cancellationToken),
                    _ => throw new InvalidOperationException("The Agent does not support this operation area.")
                };
        }

        return new AgentOperationDto(response.OperationId, response.State.ToString(), response.Phase, response.ProgressPercent,
            string.IsNullOrWhiteSpace(response.ErrorCode) ? null : response.ErrorCode,
            string.IsNullOrWhiteSpace(response.ResultJson) ? null : response.ResultJson);
    }

    public async Task<AgentOperationDto> GetOperationAsync(string operationId, CancellationToken cancellationToken)
    {
        var response = await _operations.GetOperationAsync(
            new OperationRequest { OperationId = operationId },
            Headers("/whaledeck.agent.v1.OperationService/GetOperation"),
            cancellationToken: cancellationToken);
        return Map(response);
    }

    public async Task<AgentOperationDto> CancelOperationAsync(string operationId, CancellationToken cancellationToken)
    {
        var request = new OperationRequest { OperationId = operationId };
        try
        {
            var response = await _operations.CancelOperationAsync(
                request,
                Headers("/whaledeck.agent.v1.OperationService/CancelOperation"),
                cancellationToken: cancellationToken);
            return Map(response);
        }
        catch (RpcException exception) when (exception.StatusCode == StatusCode.FailedPrecondition)
        {
            var response = await _operations.GetOperationAsync(
                request,
                Headers("/whaledeck.agent.v1.OperationService/GetOperation"),
                cancellationToken: cancellationToken);
            return Map(response);
        }
    }

    public async Task<byte[]> DownloadDiagnosticBundleAsync(string bundleId, CancellationToken cancellationToken)
    {
        using var call = _resources.DownloadDiagnosticBundle(
            new DiagnosticBundleRequest { BundleId = bundleId },
            Headers("/whaledeck.agent.v1.ManagedResourceService/DownloadDiagnosticBundle"),
            cancellationToken: cancellationToken);
        using var output = new MemoryStream();
        while (await call.ResponseStream.MoveNext(cancellationToken))
        {
            var content = call.ResponseStream.Current.Content;
            if (output.Length + content.Length > 10 * 1024 * 1024)
                throw new InvalidOperationException("The diagnostic bundle exceeds the approved size limit.");
            content.WriteTo(output);
        }
        return output.ToArray();
    }

    public Task<IReadOnlyCollection<ManagedResourceDto>> ListAsync(string area, CancellationToken cancellationToken) =>
        ListResourcesAsync(area, cancellationToken);

    public void Dispose() => _channel.Dispose();

    private Metadata Headers(string method)
    {
        if (_capabilityKey is null) return [];
        var expires = DateTimeOffset.UtcNow.AddSeconds(45).ToUnixTimeSeconds();
        var nonce = Guid.NewGuid().ToString("N");
        using var hmac = new HMACSHA256(_capabilityKey);
        var signature = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{expires}.{nonce}.{method}")));
        return new Metadata { { "x-whaledeck-capability", $"{expires}.{nonce}.{signature}" } };
    }

    private static ManagedResourceDto[] Map(ResourceCollectionResponse response) =>
        response.Resources.Select(item => new ManagedResourceDto(item.ResourceId, item.DisplayName, item.ResourceType,
            item.State, item.Version, item.ProtectedResource, item.Attributes)).ToArray();

    private static bool IsOverviewContainer(ContainerDto item) =>
        string.Equals(item.State, "running", StringComparison.OrdinalIgnoreCase) ||
        item.IsProtected ||
        (item.Labels.TryGetValue("com.docker.compose.project", out var project) &&
         project.StartsWith("whaledeck", StringComparison.Ordinal));

    private static ManagedResourceDto Map(ResourceSnapshot item) =>
        new(item.ResourceId, item.DisplayName, item.ResourceType, item.State, item.Version,
            item.ProtectedResource, item.Attributes);

    private static AgentOperationDto Map(OperationHandle response) =>
        new(response.OperationId, response.State.ToString(), response.Phase, response.ProgressPercent,
            string.IsNullOrWhiteSpace(response.ErrorCode) ? null : response.ErrorCode,
            string.IsNullOrWhiteSpace(response.ResultJson) ? null : response.ResultJson);
}
