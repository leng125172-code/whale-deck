using System.Net.WebSockets;
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
    public async Task ExitSendsAProtocolCloseBeforeCancelingThePendingReceiveAndPersistsExitCode()
    {
        var (identity, _, context, _) = await TerminalCookieIdentityTests.Setup();
        var login = await identity.ReadAsync(context); var repository = new Metadata();
        var sessions = new TerminalSessionService(repository, new Signer(), new Directory(), TimeProvider.System);
        using var socket = new TestSocket(); context.Features.Set<IHttpWebSocketFeature>(new Feature(socket));
        await new HostTerminalWebSocket(sessions, identity, new Gateway(), NullLogger<HostTerminalWebSocket>.Instance).RunAsync(context, Guid.NewGuid(), login);
        Assert.True(socket.ProtocolCloseSent); Assert.True(socket.PendingReceiveCanceled); Assert.True(socket.ClosePrecededCancellation);
        Assert.Equal("TERMINAL_CLOSED", repository.ClosedCode); Assert.Equal(7, repository.ExitCode);
    }
    private sealed class Feature(TestSocket socket) : IHttpWebSocketFeature
    { public bool IsWebSocketRequest => true; public Task<WebSocket> AcceptAsync(WebSocketAcceptContext context) => Task.FromResult<WebSocket>(socket); }
    private sealed class TestSocket : WebSocket
    {
        private WebSocketState state = WebSocketState.Open;
        private bool sentTicket;
        public bool ProtocolCloseSent { get; private set; }
        public bool PendingReceiveCanceled { get; private set; }
        public bool ClosePrecededCancellation { get; private set; }
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => state;
        public override string? SubProtocol => "whaledeck.terminal.v1";
        public override void Abort() => state = WebSocketState.Aborted;
        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) => CloseOutputAsync(closeStatus, statusDescription, cancellationToken);
        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
        { ProtocolCloseSent = true; state = WebSocketState.CloseSent; return Task.CompletedTask; }
        public override void Dispose() => state = WebSocketState.Closed;
        public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            if (!sentTicket)
            {
                sentTicket = true; var bytes = Encoding.UTF8.GetBytes("{\"token\":\"" + new string('A', 64) + "\"}"); bytes.CopyTo(buffer.AsSpan());
                return new(bytes.Length, WebSocketMessageType.Text, true);
            }
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            catch (OperationCanceledException) { PendingReceiveCanceled = true; ClosePrecededCancellation = ProtocolCloseSent; Abort(); throw; }
            throw new InvalidOperationException();
        }
        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken) => Task.CompletedTask;
    }
    private sealed class Gateway : IHostTerminalGateway
    { public Task<IHostTerminalConnection> OpenHostAsync(Guid id, TerminalActor actor, CancellationToken cancellationToken) => Task.FromResult<IHostTerminalConnection>(new Connection()); }
    private sealed class Connection : IHostTerminalConnection
    {
        private int index;
        public Task<TerminalOutput?> ReadAsync(CancellationToken cancellationToken) => Task.FromResult<TerminalOutput?>(index++ == 0 ? new("ready", [], "", 0) : new("exit", [], "TERMINAL_CLOSED", 7));
        public Task WriteAsync(TerminalInput frame, CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class Signer : ITerminalGrantSigner { public string Sign(Guid sessionId, string actorSubject, string loginBinding) => new('A', 64); }
    private sealed class Directory : IIdentityDirectory
    {
        public Task<CurrentUserDto> ResolveCurrentAsync(string subject, string? name, IReadOnlyCollection<string> claimGroups, CancellationToken cancellationToken) => Task.FromResult(new CurrentUserDto(subject, name, claimGroups, true, DateTimeOffset.UtcNow.AddHours(2)));
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListUsersAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListGroupsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListSsoApplicationsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    private sealed class Metadata : ITerminalSessionRepository
    {
        public string? ClosedCode { get; private set; } public int? ExitCode { get; private set; }
        public Task<TerminalSession> ClaimAsync(TerminalActor actor, Guid id, string grantHash, DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult(new TerminalSession
        { Id = id, ActorSubject = actor.Subject, LoginBinding = actor.LoginBinding, GrantHash = grantHash, IdempotencyKey = Guid.NewGuid().ToString("D"), State = "Connecting" });
        public Task<bool> RenewAsync(TerminalActor actor, Guid id, DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task CloseAsync(TerminalActor actor, Guid id, string code, int? exitCode, DateTimeOffset now, CancellationToken cancellationToken) { ClosedCode = code; ExitCode = exitCode; return Task.CompletedTask; }
        public Task<TerminalSession> IssueAsync(TerminalActor actor, TerminalSession proposed, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<TerminalSession?> FindOwnedAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken) => Task.FromResult<TerminalSession?>(new TerminalSession
        { Id = id, ActorSubject = actor.Subject, LoginBinding = actor.LoginBinding, GrantHash = new('A', 64), IdempotencyKey = Guid.NewGuid().ToString("D"), State = "Connecting", ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10), LeaseExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(20) });
    }
}
