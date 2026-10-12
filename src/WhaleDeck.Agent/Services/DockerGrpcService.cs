using System.Text.Json;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Agent.Services;

public sealed class DockerGrpcService(
    DockerEngine docker,
    DockerEventBuffer events,
    ResourceRegistry registry,
    BoundedProcessRunner processes,
    ContainerUsageStore usage) : DockerService.DockerServiceBase
{
    public override Task<ContainerUsageBatch> GetContainerUsage(Empty request, ServerCallContext context) =>
        Task.FromResult(usage.Snapshot());

    public override async Task<ImageMetadataResponse> InspectImage(ImageMetadataRequest request, ServerCallContext context)
    {
        var metadata = await docker.InspectImageAsync(request.Image, request.PullIfMissing, context.CancellationToken);
        var response = new ImageMetadataResponse { Image = request.Image, ImageId = metadata.ID ?? string.Empty };
        response.Environment.Add(metadata.Config?.Env ?? []);
        response.ExposedPorts.Add(metadata.Config?.ExposedPorts?.Keys ?? []);
        response.Volumes.Add(metadata.Config?.Volumes?.Keys ?? []);
        response.Entrypoint.Add(metadata.Config?.Entrypoint ?? []);
        response.Command.Add(metadata.Config?.Cmd ?? []);
        if (metadata.Config?.Labels is not null) response.Labels.Add(metadata.Config.Labels);
        return response;
    }

    public override async Task<DockerInfoResponse> GetEngineInfo(Empty request, ServerCallContext context)
    {
        var info = await docker.GetInfoAsync(context.CancellationToken);
        return new DockerInfoResponse
        {
            ServerVersion = info.ServerVersion ?? string.Empty,
            OperatingSystem = info.OperatingSystem ?? string.Empty,
            Architecture = info.Architecture ?? string.Empty,
            Containers = checked((int)info.Containers),
            Images = checked((int)info.Images),
            LoggingDriver = info.LoggingDriver ?? string.Empty
        };
    }

    public override async Task<ListContainersResponse> ListContainers(ListContainersRequest request, ServerCallContext context)
    {
        var containers = await docker.ListContainersAsync(request.IncludeStopped, context.CancellationToken);
        var response = new ListContainersResponse();
        foreach (var container in containers)
        {
            response.Containers.Add(BuildContainerSummary(container, registry));
        }
        return response;
    }

    public static ContainerSummary BuildContainerSummary(Docker.DotNet.Models.ContainerListResponse container, ResourceRegistry registry)
    {
        var name = container.Names.FirstOrDefault()?.TrimStart('/') ?? container.ID[..Math.Min(12, container.ID.Length)];
        var protectedResource = registry.IsProtectedContainer(name, container.Labels);
        var registered = registry.All.FirstOrDefault(item => item.Type == "Container" && item.ExternalId == name);
        var protectionLevel = registered?.ProtectionLevel ??
            (container.Labels is not null && container.Labels.TryGetValue("io.whaledeck.protection-level", out var configuredLevel)
                ? configuredLevel : protectedResource ? "CriticalData" : "Managed");
        var summary = new ContainerSummary
        {
            Id = container.ID,
            Name = name,
            Image = container.Image,
            State = container.State,
            Status = container.Status,
            ProtectedResource = protectedResource,
            ProtectionLevel = protectionLevel
        };
        summary.Ports.AddRange((container.Ports ?? []).Select(port => port.PublicPort > 0
            ? $"{port.IP}:{port.PublicPort} → {port.PrivatePort}/{port.Type}"
            : $"{port.PrivatePort}/{port.Type}").Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
        if (container.NetworkSettings?.Networks is not null)
        {
            summary.Networks.AddRange(container.NetworkSettings.Networks.Keys.Order(StringComparer.Ordinal));
            summary.Addresses.AddRange(container.NetworkSettings.Networks.Values.Select(item => item.IPAddress)
                .Where(address => !string.IsNullOrWhiteSpace(address)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
        }
        if (container.Created != default) summary.CreatedAtUtc = Timestamp.FromDateTime(container.Created.ToUniversalTime());
        if (container.Labels is not null)
        {
            foreach (var label in container.Labels.Where(item => !IsSensitiveLabel(item.Key)))
            {
                summary.Labels[label.Key] = label.Value;
            }
        }
        return summary;
    }

    public override async Task<ContainerLogsResponse> GetContainerLogs(ContainerLogsRequest request, ServerCallContext context)
    {
        var containerId = await ResolveContainerIdAsync(request.ContainerId, context.CancellationToken);
        var tail = (int)Math.Clamp(request.Tail, 1u, 2_000u);
        var sinceMinutes = (int)Math.Clamp(request.SinceMinutes, 1u, 10_080u);
        var result = await processes.RunAsync(
            "/usr/bin/docker",
            ["logs", "--timestamps", "--tail", tail.ToString(System.Globalization.CultureInfo.InvariantCulture), "--since", $"{sinceMinutes}m", containerId],
            null,
            TimeSpan.FromSeconds(20),
            "CONTAINER_LOG_QUERY_FAILED",
            context.CancellationToken);
        var combined = string.Join('\n', new[] { result.StandardOutput, result.StandardError }.Where(value => !string.IsNullOrWhiteSpace(value)));
        var truncated = combined.Length > 64 * 1024;
        if (truncated) combined = combined[..(64 * 1024)];
        var response = new ContainerLogsResponse { Truncated = truncated };
        response.Lines.AddRange(combined.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
        return response;
    }

    public override async Task<ContainerStatsResponse> GetContainerStats(ResourceReference request, ServerCallContext context)
    {
        var containerId = await ResolveContainerIdAsync(request.ResourceId, context.CancellationToken);
        var result = await processes.RunAsync(
            "/usr/bin/docker",
            ["stats", "--no-stream", "--format", "{\"cpu\":{{json .CPUPerc}},\"memory\":{{json .MemUsage}},\"memoryPercent\":{{json .MemPerc}},\"network\":{{json .NetIO}},\"block\":{{json .BlockIO}},\"pids\":{{json .PIDs}}}", containerId],
            null,
            TimeSpan.FromSeconds(20),
            "CONTAINER_STATS_QUERY_FAILED",
            context.CancellationToken);
        using var document = JsonDocument.Parse(result.StandardOutput);
        var response = new ContainerStatsResponse();
        foreach (var property in document.RootElement.EnumerateObject())
            response.Values[property.Name] = property.Value.GetString() ?? property.Value.ToString();
        return response;
    }

    public override Task<DockerEventsResponse> ListEvents(DockerEventsRequest request, ServerCallContext context)
    {
        var since = request.SinceUnixSeconds > 0
            ? DateTimeOffset.FromUnixTimeSeconds(request.SinceUnixSeconds)
            : DateTimeOffset.UtcNow.AddHours(-1);
        var response = new DockerEventsResponse();
        foreach (var item in events.Snapshot(since, checked((int)Math.Clamp(request.Take, 1u, 1_000u))))
        {
            var entry = new DockerEvent
            {
                Fingerprint = item.Fingerprint,
                OccurredAtUtc = Timestamp.FromDateTimeOffset(item.OccurredAtUtc),
                EventType = item.EventType,
                Action = item.Action,
                ResourceId = item.ResourceId,
                ResourceName = item.ResourceName,
                Image = item.Image
            };
            foreach (var attribute in item.Attributes)
                entry.Attributes[attribute.Key] = attribute.Value;
            response.Events.Add(entry);
        }
        return Task.FromResult(response);
    }

    public override async Task<ContainerInspectResponse> InspectContainer(ResourceReference request, ServerCallContext context)
    {
        var metadata = await docker.InspectContainerAsync(request.ResourceId, context.CancellationToken);
        var response = new ContainerInspectResponse
        {
            Id = metadata.ID ?? string.Empty,
            Name = metadata.Name?.TrimStart('/') ?? string.Empty,
            Image = metadata.Config?.Image ?? metadata.Image ?? string.Empty,
            RestartPolicy = metadata.HostConfig?.RestartPolicy?.Name switch
            {
                Docker.DotNet.Models.RestartPolicyKind.UnlessStopped => "unless-stopped",
                Docker.DotNet.Models.RestartPolicyKind.OnFailure => "on-failure",
                Docker.DotNet.Models.RestartPolicyKind.Always => "always",
                _ => "no"
            },
            Cpus = (metadata.HostConfig?.NanoCPUs ?? 0) / 1_000_000_000d,
            MemoryMb = (metadata.HostConfig?.Memory ?? 0) / (1024 * 1024),
            HealthIntervalSeconds = checked((long)(metadata.Config?.Healthcheck?.Interval.TotalSeconds ?? 0)),
            HealthTimeoutSeconds = checked((long)(metadata.Config?.Healthcheck?.Timeout.TotalSeconds ?? 0)),
            HealthRetries = metadata.Config?.Healthcheck?.Retries ?? 0
        };
        response.EnvironmentNames.AddRange((metadata.Config?.Env ?? [])
            .Select(item => item.Split('=', 2)[0]).Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.Ordinal));
        response.Command.AddRange((metadata.Config?.Cmd ?? []).Select(RedactArgument));
        response.Entrypoint.AddRange((metadata.Config?.Entrypoint ?? []).Select(RedactArgument));
        response.HealthCommand.AddRange((metadata.Config?.Healthcheck?.Test ?? []).Select(RedactArgument));
        if (metadata.Config?.Labels is not null)
        {
            foreach (var label in metadata.Config.Labels.Where(item => !IsSensitiveLabel(item.Key)))
                response.Labels[label.Key] = label.Value;
        }
        if (metadata.HostConfig?.PortBindings is not null)
        {
            foreach (var (containerPort, bindings) in metadata.HostConfig.PortBindings)
            {
                foreach (var binding in bindings ?? [])
                    response.Ports.Add($"{binding.HostIP}:{binding.HostPort}:{containerPort}");
            }
        }
        response.Volumes.AddRange((metadata.Mounts ?? []).Select(item =>
            $"{(string.IsNullOrWhiteSpace(item.Name) ? item.Source : item.Name)}:{item.Destination}:{(item.RW ? "rw" : "ro")}"));
        response.Networks.AddRange(metadata.NetworkSettings?.Networks?.Keys ?? []);
        return response;
    }

    public override Task<OperationHandle> ChangeContainerState(ChangeContainerStateRequest request, ServerCallContext context) =>
        docker.ChangeContainerStateAsync(
            request.ContainerId,
            request.Action,
            request.TimeoutSeconds,
            request.PreserveVolumes,
            request.PlanHash,
            request.Context?.JobId ?? string.Empty,
            request.Context?.IdempotencyKey ?? string.Empty,
            context.CancellationToken);

    public override async Task<ResourceCollectionResponse> ListImages(Empty request, ServerCallContext context)
    {
        var response = new ResourceCollectionResponse();
        foreach (var image in await docker.ListImagesAsync(context.CancellationToken))
        {
            var snapshot = new ResourceSnapshot
            {
                ResourceId = $"image:{image.ID}",
                DisplayName = image.RepoTags?.FirstOrDefault() ?? image.ID,
                ResourceType = "Image",
                State = image.Containers > 0 ? "InUse" : "Unused",
                Version = image.ID
            };
            snapshot.Attributes["sizeBytes"] = image.Size.ToString(System.Globalization.CultureInfo.InvariantCulture);
            snapshot.Attributes["containerCount"] = image.Containers.ToString(System.Globalization.CultureInfo.InvariantCulture);
            snapshot.Attributes["tags"] = string.Join(',', (image.RepoTags ?? []).Where(item => item != "<none>:<none>").Take(20));
            snapshot.Attributes["digests"] = string.Join(',', (image.RepoDigests ?? []).Take(20));
            snapshot.Attributes["createdAtUtc"] = image.Created != default
                ? image.Created.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture)
                : string.Empty;
            response.Resources.Add(snapshot);
        }
        return response;
    }

    public override async Task<ResourceCollectionResponse> ListNetworks(Empty request, ServerCallContext context)
    {
        var response = new ResourceCollectionResponse();
        foreach (var network in await docker.ListNetworksAsync(context.CancellationToken))
        {
            var snapshot = new ResourceSnapshot
            {
                ResourceId = $"network:{network.ID}",
                DisplayName = network.Name,
                ResourceType = "DockerNetwork",
                State = network.Internal ? "Internal" : "External",
                Version = network.Driver,
                ProtectedResource = network.Name is "bridge" or "host" or "none" || network.Ingress || network.ConfigOnly ||
                    network.Name.StartsWith("database-platform", StringComparison.OrdinalIgnoreCase) ||
                    network.Name.StartsWith("whaledeck", StringComparison.OrdinalIgnoreCase) ||
                    network.Labels.TryGetValue("io.whaledeck.protected", out var protectedValue) &&
                    string.Equals(protectedValue, "true", StringComparison.OrdinalIgnoreCase)
            };
            snapshot.Attributes["scope"] = network.Scope;
            snapshot.Attributes["driver"] = network.Driver;
            snapshot.Attributes["internal"] = network.Internal.ToString().ToLowerInvariant();
            snapshot.Attributes["attachable"] = network.Attachable.ToString().ToLowerInvariant();
            snapshot.Attributes["containerCount"] = (network.Containers?.Count ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture);
            snapshot.Attributes["createdAtUtc"] = network.Created.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture);
            response.Resources.Add(snapshot);
        }
        return response;
    }

    public override async Task<ResourceCollectionResponse> ListVolumes(Empty request, ServerCallContext context)
    {
        var response = new ResourceCollectionResponse();
        var references = (await docker.ListContainersAsync(true, context.CancellationToken))
            .SelectMany(container => container.Mounts ?? [])
            .Where(mount => !string.IsNullOrWhiteSpace(mount.Name))
            .GroupBy(mount => mount.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        foreach (var volume in await docker.ListVolumesAsync(context.CancellationToken))
        {
            var referenceCount = references.GetValueOrDefault(volume.Name);
            var snapshot = new ResourceSnapshot
            {
                ResourceId = $"volume:{volume.Name}",
                DisplayName = volume.Name,
                ResourceType = "DockerVolume",
                State = referenceCount == 0 ? "Orphaned" : "InUse",
                Version = volume.Driver,
                ProtectedResource = volume.Name.StartsWith("database-platform", StringComparison.OrdinalIgnoreCase) ||
                    volume.Name.StartsWith("whaledeck", StringComparison.OrdinalIgnoreCase) ||
                    volume.Labels.TryGetValue("io.whaledeck.protected", out var protectedValue) &&
                    string.Equals(protectedValue, "true", StringComparison.OrdinalIgnoreCase)
            };
            snapshot.Attributes["mountpoint"] = volume.Mountpoint ?? string.Empty;
            snapshot.Attributes["scope"] = volume.Scope ?? string.Empty;
            snapshot.Attributes["referenceCount"] = referenceCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
            snapshot.Attributes["sizeBytes"] = (volume.UsageData?.Size ?? -1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            snapshot.Attributes["backupStatus"] = "Unconfigured";
            snapshot.Attributes["createdAtUtc"] = volume.CreatedAt ?? string.Empty;
            response.Resources.Add(snapshot);
        }
        return response;
    }

    public override Task<PlanResponse> PlanAction(RegisteredActionRequest request, ServerCallContext context) =>
        docker.PlanAsync(request.Resource?.ResourceId ?? string.Empty, request.Action, request.Parameters, context.CancellationToken);

    public override Task<OperationHandle> RunAction(RegisteredActionRequest request, ServerCallContext context) =>
        docker.RunActionAsync(request.Resource?.ResourceId ?? string.Empty, request.Action, request.Parameters,
            request.PlanHash, request.Context?.JobId ?? string.Empty, request.Context?.IdempotencyKey ?? string.Empty,
            context.CancellationToken);

    private static bool IsSensitiveLabel(string key) =>
        key.Contains("password", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("token", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("secret", StringComparison.OrdinalIgnoreCase);

    private static string RedactArgument(string value) =>
        value.Contains("password", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("token", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("credential", StringComparison.OrdinalIgnoreCase)
            ? "[REDACTED]"
            : value;

    private async Task<string> ResolveContainerIdAsync(string value, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128 ||
            value.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '_' or '.' or '-')))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "The container id is invalid."));
        var containers = await docker.ListContainersAsync(true, cancellationToken);
        var matches = containers.Where(item =>
            item.ID.StartsWith(value, StringComparison.OrdinalIgnoreCase) ||
            item.Names.Any(name => string.Equals(name.TrimStart('/'), value, StringComparison.Ordinal))).ToArray();
        return matches.Length switch
        {
            0 => throw new RpcException(new Status(StatusCode.NotFound, "The container was not found.")),
            > 1 => throw new RpcException(new Status(StatusCode.InvalidArgument, "The container id is ambiguous.")),
            _ => matches[0].ID,
        };
    }
}
