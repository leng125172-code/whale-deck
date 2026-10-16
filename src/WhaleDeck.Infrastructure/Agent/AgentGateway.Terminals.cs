using Google.Protobuf;
using Grpc.Core;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Infrastructure.Agent;

public sealed partial class AgentGateway : IHostTerminalGateway
{
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
}
