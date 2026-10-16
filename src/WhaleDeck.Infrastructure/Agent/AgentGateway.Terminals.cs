using Google.Protobuf;
using Grpc.Core;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Infrastructure.Agent;

public sealed partial class AgentGateway : IHostTerminalGateway, IContainerTerminalGateway
{
    public async Task<ContainerTerminalTargetDto> InspectContainerTerminalAsync(string containerId, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(5));
        await RequireContainerTerminalAsync(timeout.Token);
        var client = new ContainerTerminalService.ContainerTerminalServiceClient(_channel);
        var target = await client.InspectAsync(new ResourceReference { ResourceType = "Container", ResourceId = containerId },
            Headers("/whaledeck.agent.v1.ContainerTerminalService/Inspect"), cancellationToken: timeout.Token);
        return new(target.ContainerId, target.Name, target.InstanceId, target.ImageId, target.ConfiguredUser);
    }
    public async Task<IHostTerminalConnection> OpenContainerAsync(Guid id, TerminalActor actor, string containerId, string instanceId, string shell, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(5));
        await RequireContainerTerminalAsync(timeout.Token);
        var call = new ContainerTerminalService.ContainerTerminalServiceClient(_channel).Connect(
            Headers("/whaledeck.agent.v1.ContainerTerminalService/Connect"), deadline: DateTime.UtcNow.AddMinutes(10), cancellationToken: cancellationToken);
        try
        {
            await call.RequestStream.WriteAsync(new ContainerTerminalFrame { ContainerId = containerId, InstanceId = instanceId, Shell = shell,
                Payload = new() { SessionId = id.ToString("N"), Kind = "open", Columns = 80, Rows = 24,
                    Context = new() { ActorSubject = actor.Subject, TraceId = actor.TraceId } } }, timeout.Token);
            return new ContainerTerminalConnection(call);
        }
        catch { call.Dispose(); throw; }
    }
    private async Task RequireContainerTerminalAsync(CancellationToken cancellationToken)
    {
        var capabilities = await GetCapabilitiesAsync(cancellationToken);
        if (capabilities.ProtocolMajor != 1 || !capabilities.Capabilities.Contains("docker.terminal.exec"))
            throw new RpcException(new(StatusCode.Unimplemented, "TERMINAL_CAPABILITY_UNAVAILABLE"));
    }
    public async Task<IHostTerminalConnection> OpenHostAsync(Guid id, TerminalActor actor, CancellationToken cancellationToken)
    {
        using var opening = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        opening.CancelAfter(TimeSpan.FromSeconds(5));
        var capabilities = await GetCapabilitiesAsync(opening.Token);
        if (capabilities.ProtocolMajor != 1 || !capabilities.Capabilities.Contains("host.terminal.pty"))
            throw new RpcException(new(StatusCode.Unimplemented, "TERMINAL_CAPABILITY_UNAVAILABLE"));
        var client = new HostTerminalService.HostTerminalServiceClient(_channel);
        var call = client.Connect(Headers("/whaledeck.agent.v1.HostTerminalService/Connect"), deadline: DateTime.UtcNow.AddMinutes(10), cancellationToken: cancellationToken);
        try
        {
            await call.RequestStream.WriteAsync(new HostTerminalFrame { SessionId = id.ToString("N"), Kind = "open", Columns = 80, Rows = 24,
                Context = new() { ActorSubject = actor.Subject, TraceId = actor.TraceId } }, opening.Token);
            return new HostTerminalConnection(call);
        }
        catch { call.Dispose(); throw; }
    }
    private sealed class HostTerminalConnection(AsyncDuplexStreamingCall<HostTerminalFrame, HostTerminalEvent> call) : IHostTerminalConnection
    {
        public Task WriteAsync(TerminalInput frame, CancellationToken cancellationToken) => call.RequestStream.WriteAsync(new HostTerminalFrame
        { Kind = frame.Kind, Data = ByteString.CopyFrom(frame.Data), Columns = frame.Columns, Rows = frame.Rows }, cancellationToken);
        public async Task<TerminalOutput?> ReadAsync(CancellationToken cancellationToken) => await call.ResponseStream.MoveNext(cancellationToken)
            ? new(call.ResponseStream.Current.Kind, call.ResponseStream.Current.Data.ToByteArray(), call.ResponseStream.Current.Code, call.ResponseStream.Current.ExitCode) : null;
        public ValueTask DisposeAsync() { call.Dispose(); return ValueTask.CompletedTask; }
    }
    private sealed class ContainerTerminalConnection(AsyncDuplexStreamingCall<ContainerTerminalFrame, HostTerminalEvent> call) : IHostTerminalConnection
    {
        public Task WriteAsync(TerminalInput frame, CancellationToken cancellationToken) => call.RequestStream.WriteAsync(new ContainerTerminalFrame
        { Payload = new() { Kind = frame.Kind, Data = ByteString.CopyFrom(frame.Data), Columns = frame.Columns, Rows = frame.Rows } }, cancellationToken);
        public async Task<TerminalOutput?> ReadAsync(CancellationToken cancellationToken) => await call.ResponseStream.MoveNext(cancellationToken)
            ? new(call.ResponseStream.Current.Kind, call.ResponseStream.Current.Data.ToByteArray(), call.ResponseStream.Current.Code, call.ResponseStream.Current.ExitCode) : null;
        public ValueTask DisposeAsync() { call.Dispose(); return ValueTask.CompletedTask; }
    }
}
