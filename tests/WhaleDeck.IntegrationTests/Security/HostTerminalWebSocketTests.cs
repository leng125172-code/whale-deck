using System.Net.WebSockets;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging.Abstractions;
using WhaleDeck.Api.Terminals;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.IntegrationTests.Security;

public sealed class HostTerminalWebSocketTests
{
    [Fact]
    public async Task LoggedOutCookieCannotSignEitherAgentCapabilityAfterGrantClaim()
    {
        var (identity, store, context, key) = await TerminalCookieIdentityTests.Setup(); var login = await identity.ReadAsync(context);
        await store.RemoveAsync(key);
        var repository = new Metadata(true); var gateway = new Gateway();
        var sessions = new TerminalSessionService(repository, new Signer(), new Directory(), TimeProvider.System, gateway);
        using var socket = new TestSocket(true); context.Features.Set<IHttpWebSocketFeature>(new Feature(socket));
        await new HostTerminalWebSocket(sessions, identity, gateway, NullLogger<HostTerminalWebSocket>.Instance, gateway).RunAsync(context, Guid.NewGuid(), login);
        Assert.False(gateway.HostOpened); Assert.Null(gateway.ContainerOpened);
        Assert.Equal("TERMINAL_FORBIDDEN", repository.ClosedCode);
    }
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ExitSendsAProtocolCloseAndWaitsBoundedlyForPeerAcknowledgment(bool acknowledge, bool container)
    {
        var (identity, _, context, _) = await TerminalCookieIdentityTests.Setup();
        var login = await identity.ReadAsync(context); var repository = new Metadata(container); var gateway = new Gateway();
        var sessions = new TerminalSessionService(repository, new Signer(), new Directory(), TimeProvider.System, gateway);
        using var socket = new TestSocket(acknowledge); context.Features.Set<IHttpWebSocketFeature>(new Feature(socket));
        await new HostTerminalWebSocket(sessions, identity, gateway, NullLogger<HostTerminalWebSocket>.Instance, gateway).RunAsync(context, Guid.NewGuid(), login);
        Assert.True(socket.ProtocolCloseSent); Assert.Equal(!acknowledge, socket.PendingReceiveCanceled);
        if (!acknowledge) Assert.True(socket.ClosePrecededCancellation);
        else Assert.True(socket.PeerAcknowledged);
        Assert.Equal("TERMINAL_CLOSED", repository.ClosedCode); Assert.Equal(7, repository.ExitCode);
        Assert.Equal(!container, gateway.HostOpened);
        Assert.Equal(container ? new string('a', 64) + ":10:instance:sh" : null, gateway.ContainerOpened);
    }
    [Fact]
    public async Task RealManagedWebSocketsCompleteCloseHandshakeWithoutAbortingTheServerStream()
    {
        var (identity, _, context, _) = await TerminalCookieIdentityTests.Setup(); var login = await identity.ReadAsync(context);
        var repository = new Metadata(); var sessions = new TerminalSessionService(repository, new Signer(), new Directory(), TimeProvider.System, new Containers());
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        using var client = new TcpClient(); var opening = client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port, deadline.Token);
        using var server = await listener.AcceptTcpClientAsync(deadline.Token); await opening; listener.Stop();
        using var serverSocket = WebSocket.CreateFromStream(server.GetStream(), true, "whaledeck.terminal.v1", TimeSpan.FromSeconds(20));
        using var clientSocket = WebSocket.CreateFromStream(client.GetStream(), false, "whaledeck.terminal.v1", TimeSpan.FromSeconds(20));
        context.Features.Set<IHttpWebSocketFeature>(new Feature(serverSocket));
        var running = new HostTerminalWebSocket(sessions, identity, new Gateway(), NullLogger<HostTerminalWebSocket>.Instance, new Containers()).RunAsync(context, Guid.NewGuid(), login);
        try
        {
            await clientSocket.SendAsync(Encoding.UTF8.GetBytes("{\"token\":\"" + new string('A', 64) + "\"}").AsMemory(), WebSocketMessageType.Text, true, deadline.Token);
            var buffer = new byte[8192]; var rows = 0;
            while (true)
            {
                var result = await clientSocket.ReceiveAsync(buffer.AsMemory(), deadline.Token);
                if (result.MessageType == WebSocketMessageType.Close) break;
                Assert.Equal(WebSocketMessageType.Text, result.MessageType); rows++;
            }
            Assert.Equal(2, rows);
            await clientSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", deadline.Token);
            await running.WaitAsync(deadline.Token);
            Assert.Equal(WebSocketState.Closed, serverSocket.State);
            Assert.Equal("TERMINAL_CLOSED", repository.ClosedCode); Assert.Equal(7, repository.ExitCode);
        }
        finally
        {
            clientSocket.Dispose(); serverSocket.Dispose();
            try { await running.WaitAsync(TimeSpan.FromSeconds(5)); } catch (TimeoutException) { context.Abort(); }
        }
    }
    private sealed class Feature(WebSocket socket) : IHttpWebSocketFeature
    { public bool IsWebSocketRequest => true; public Task<WebSocket> AcceptAsync(WebSocketAcceptContext context) => Task.FromResult<WebSocket>(socket); }
    private sealed class TestSocket(bool acknowledge) : WebSocket
    {
        private readonly TaskCompletionSource<bool> closeAcknowledgment = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private WebSocketState state = WebSocketState.Open;
        private bool sentTicket;
        public bool ProtocolCloseSent { get; private set; }
        public bool PendingReceiveCanceled { get; private set; }
        public bool ClosePrecededCancellation { get; private set; }
        public bool PeerAcknowledged { get; private set; }
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => state;
        public override string? SubProtocol => "whaledeck.terminal.v1";
        public override void Abort() => state = WebSocketState.Aborted;
        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) => CloseOutputAsync(closeStatus, statusDescription, cancellationToken);
        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
        { ProtocolCloseSent = true; state = WebSocketState.CloseSent; if (acknowledge) closeAcknowledgment.TrySetResult(true); return Task.CompletedTask; }
        public override void Dispose() => state = WebSocketState.Closed;
        public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            if (!sentTicket)
            {
                sentTicket = true; var bytes = Encoding.UTF8.GetBytes("{\"token\":\"" + new string('A', 64) + "\"}"); bytes.CopyTo(buffer.AsSpan());
                return new(bytes.Length, WebSocketMessageType.Text, true);
            }
            try
            {
                await closeAcknowledgment.Task.WaitAsync(cancellationToken);
                PeerAcknowledged = true; state = WebSocketState.Closed;
                return new(0, WebSocketMessageType.Close, true);
            }
            catch (OperationCanceledException) { PendingReceiveCanceled = true; ClosePrecededCancellation = ProtocolCloseSent; Abort(); throw; }
        }
        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken) => Task.CompletedTask;
    }
    private sealed class Gateway : IHostTerminalGateway, IContainerTerminalGateway
    {
        public bool HostOpened { get; private set; }
        public string? ContainerOpened { get; private set; }
        public Task<IHostTerminalConnection> OpenHostAsync(Guid id, TerminalActor actor, CancellationToken cancellationToken)
        { HostOpened = true; return Task.FromResult<IHostTerminalConnection>(new Connection()); }
        public Task<ContainerTerminalTargetDto> InspectContainerTerminalAsync(string containerId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IHostTerminalConnection> OpenContainerAsync(Guid id, TerminalActor actor, string containerId, string instanceId, string shell, CancellationToken cancellationToken)
        { ContainerOpened = containerId + ":" + instanceId + ":" + shell; return Task.FromResult<IHostTerminalConnection>(new Connection()); }
    }
    private sealed class Connection : IHostTerminalConnection
    {
        private int index;
        public Task<TerminalOutput?> ReadAsync(CancellationToken cancellationToken) => Task.FromResult<TerminalOutput?>(index++ == 0 ? new("ready", [], "", 0) : new("exit", [], "TERMINAL_CLOSED", 7));
        public Task WriteAsync(TerminalInput frame, CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class Signer : ITerminalGrantSigner { public string Sign(Guid sessionId, string actorSubject, string loginBinding, string targetId = "whaledeck.host", string? instanceId = null, string? shell = null) => new('A', 64); }
    private sealed class Containers : IContainerTerminalGateway
    {
        public Task<ContainerTerminalTargetDto> InspectContainerTerminalAsync(string containerId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IHostTerminalConnection> OpenContainerAsync(Guid id, TerminalActor actor, string containerId, string instanceId, string shell, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    private sealed class Directory : IIdentityDirectory
    {
        public Task<CurrentUserDto> ResolveCurrentAsync(string subject, string? name, IReadOnlyCollection<string> claimGroups, CancellationToken cancellationToken) => Task.FromResult(new CurrentUserDto(subject, name, claimGroups, true, DateTimeOffset.UtcNow.AddHours(2)));
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListUsersAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListGroupsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListSsoApplicationsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    private sealed class Metadata(bool container = false) : ITerminalSessionRepository
    {
        public string? ClosedCode { get; private set; } public int? ExitCode { get; private set; }
        public Task<TerminalSession> ClaimAsync(TerminalActor actor, Guid id, string grantHash, DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult(new TerminalSession
        { Id = id, ActorSubject = actor.Subject, LoginBinding = actor.LoginBinding, GrantHash = grantHash, IdempotencyKey = Guid.NewGuid().ToString("D"), State = "Connecting",
            TargetKind = container ? "Container" : "Host", TargetId = container ? new('a', 64) : "whaledeck.host", InstanceId = container ? "10:instance" : null, Shell = container ? "sh" : null });
        public Task<bool> RenewAsync(TerminalActor actor, Guid id, DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task CloseAsync(TerminalActor actor, Guid id, string code, int? exitCode, DateTimeOffset now, CancellationToken cancellationToken) { ClosedCode = code; ExitCode = exitCode; return Task.CompletedTask; }
        public Task<TerminalSession> IssueAsync(TerminalActor actor, TerminalSession proposed, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<TerminalSession?> FindOwnedAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken) => Task.FromResult<TerminalSession?>(new TerminalSession
        { Id = id, ActorSubject = actor.Subject, LoginBinding = actor.LoginBinding, GrantHash = new('A', 64), IdempotencyKey = Guid.NewGuid().ToString("D"), State = "Connecting", ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10), LeaseExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(20) });
    }
}
