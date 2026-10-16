using System.Globalization;
using System.Text.Json;
using Grpc.Core;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Agent.Services;

/// <summary>Dedicated capability-bound Exec stream; not a host executable/UID/path endpoint.</summary>
public sealed class ContainerTerminalGrpcService(DockerEngine docker, ResourceRegistry registry, HostTerminalRunner terminals)
    : ContainerTerminalService.ContainerTerminalServiceBase
{
    public override async Task<ContainerTerminalTarget> Inspect(ResourceReference request, ServerCallContext context)
    {
        ContainerTerminalPolicy.RequireId(request.ResourceId);
        if (request.ResourceType != "Container") throw new ArgumentException("CONTAINER_TYPE_REQUIRED");
        return ContainerTerminalPolicy.Inspect(await docker.InspectContainerAsync(request.ResourceId, context.CancellationToken), registry);
    }

    public override async Task Connect(IAsyncStreamReader<ContainerTerminalFrame> requestStream,
        IServerStreamWriter<HostTerminalEvent> responseStream, ServerCallContext context)
    {
        using var first = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        first.CancelAfter(TimeSpan.FromSeconds(5));
        if (!await requestStream.MoveNext(first.Token)) throw new ArgumentException("TERMINAL_OPEN_REQUIRED");
        var open = requestStream.Current;
        var basic = ContainerTerminalPolicy.Open(open);
        var inspected = ContainerTerminalPolicy.Inspect(await docker.InspectContainerAsync(open.ContainerId, first.Token), registry);
        if (inspected.InstanceId != open.InstanceId) throw new InvalidOperationException("CONTAINER_INSTANCE_CHANGED");
        var input = JsonSerializer.Serialize(new { type = "open", columns = basic.Columns, rows = basic.Rows,
            containerId = open.ContainerId, instanceId = open.InstanceId, shell = open.Shell });
        await terminals.RunContainerAsync(basic.SessionId, input, new Frames(requestStream), responseStream, context.CancellationToken);
    }

    private sealed class Frames(IAsyncStreamReader<ContainerTerminalFrame> source) : IAsyncStreamReader<HostTerminalFrame>
    {
        public HostTerminalFrame Current { get; private set; } = null!;
        public async Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            if (!await source.MoveNext(cancellationToken)) return false;
            Current = ContainerTerminalPolicy.Follow(source.Current);
            return true;
        }
    }
}

internal static class ContainerTerminalPolicy
{
    public static void RequireId(string id)
    {
        if (id.Length != 64 || id.Any(character => !(char.IsAsciiDigit(character) || character is >= 'a' and <= 'f')))
            throw new ArgumentException("CONTAINER_ID_REQUIRED");
    }

    public static HostTerminalFrame Open(ContainerTerminalFrame frame)
    {
        RequireId(frame.ContainerId);
        if (frame.Payload is null || frame.Shell is not ("sh" or "bash" or "ash") ||
            string.IsNullOrWhiteSpace(frame.InstanceId) || frame.InstanceId.Length > 80 || frame.InstanceId.Any(char.IsControl))
            throw new ArgumentException("CONTAINER_TERMINAL_OPEN_INVALID");
        _ = HostTerminalProtocol.Open(frame.Payload);
        return frame.Payload;
    }

    public static HostTerminalFrame Follow(ContainerTerminalFrame frame)
    {
        if (frame.ContainerId.Length != 0 || frame.InstanceId.Length != 0 || frame.Shell.Length != 0 || frame.Payload is null)
            throw new ArgumentException("TERMINAL_TARGET_CHANGED");
        return frame.Payload;
    }

    public static ContainerTerminalTarget Inspect(Docker.DotNet.Models.ContainerInspectResponse container, ResourceRegistry registry)
    {
        RequireId(container.ID);
        var name = container.Name.TrimStart('/');
        if (registry.IsProtectedContainer(name, container.Config?.Labels) || registry.IsProtectedContainer(container.ID, container.Config?.Labels))
            throw new UnauthorizedAccessException("CONTAINER_PROTECTED");
        var state = container.State;
        if (state is null || !state.Running || state.Paused || state.Restarting || state.Dead || state.Pid <= 1 ||
            string.IsNullOrWhiteSpace(state.StartedAt) || state.StartedAt.Length > 64)
            throw new InvalidOperationException("CONTAINER_NOT_RUNNING");
        var config = container.HostConfig;
        if (config is null || config.Privileged || config.CapAdd is { Count: > 0 } || config.Devices is { Count: > 0 } ||
            config.DeviceRequests is { Count: > 0 } || config.DeviceCgroupRules is { Count: > 0 } ||
            new[] { config.PidMode, config.IpcMode, config.NetworkMode, config.UTSMode, config.UsernsMode, config.CgroupnsMode }
                .Any(value => value == "host" || value?.StartsWith("container:", StringComparison.Ordinal) == true) ||
            config.SecurityOpt?.Any(value => value is "seccomp=unconfined" or "apparmor=unconfined" or "label=disable") == true)
            throw new UnauthorizedAccessException("CONTAINER_UNSAFE_ISOLATION");
        // The root-owned driver independently checks actual mount identity and
        // the registry immediately before launch. Inspection never reveals Env.
        return new() { ContainerId = container.ID, Name = name, InstanceId = state.Pid.ToString(CultureInfo.InvariantCulture) + ":" + state.StartedAt,
            ImageId = container.Image ?? string.Empty, ConfiguredUser = container.Config?.User ?? string.Empty };
    }
}
