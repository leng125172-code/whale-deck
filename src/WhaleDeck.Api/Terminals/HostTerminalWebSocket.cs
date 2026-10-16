using System.Net.WebSockets;
using System.Text.Json;
using WhaleDeck.Api.Security;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Api.Terminals;

/// <summary>HTTP/WS transport only. Business state, tokens, authorization and durable audit live in Application.</summary>
public sealed class HostTerminalWebSocket(TerminalSessionService sessions, TerminalCookieIdentity identity, IHostTerminalGateway gateway, ILogger<HostTerminalWebSocket> logger, IContainerTerminalGateway containers)
{
    private static readonly Action<ILogger, Guid, string, Exception?> LogFailure = LoggerMessage.Define<Guid, string>(
        LogLevel.Warning, new EventId(9041, "TerminalTransportFailure"), "Terminal session {SessionId} failed with type {FailureType}");
    public async Task RunAsync(HttpContext context, Guid id, TerminalLogin login)
    {
        using var socket = await context.WebSockets.AcceptWebSocketAsync("whaledeck.terminal.v1");
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        lifetime.CancelAfter(TimeSpan.FromMinutes(10));
        var code = "TERMINAL_DISCONNECTED"; int? exitCode = null; var claimed = false;
        IHostTerminalConnection? connection = null;
        using var writes = new SemaphoreSlim(1);
        var requestedClose = false;
        var receivedReady = false;
        Task input = Task.CompletedTask, output = Task.CompletedTask, monitor = Task.CompletedTask;
        try
        {
            using var first = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); first.CancelAfter(TimeSpan.FromSeconds(5));
            var packet = await Message(socket, 256, first.Token) ?? throw new ArgumentException("TERMINAL_TICKET_REQUIRED");
            var token = TerminalFramePolicy.Grant(packet);
            var session = await sessions.ClaimAsync(login.Actor, id, token, first.Token); claimed = true;
            // Re-read the live server ticket immediately before capability
            // signing; a cached AuthenticateAsync result cannot prove logout.
            var current = await identity.ReadAsync(context, login.Key);
            if (current.Actor.Subject != login.Actor.Subject || current.Actor.LoginBinding != login.Actor.LoginBinding ||
                !await sessions.RequireConnectingAsync(current.Actor, id, first.Token)) throw new UnauthorizedAccessException("TERMINAL_SESSION_REVOKED");
            // Dispatch only persisted, claimed metadata. No WS frame/URL may select a target.
            connection = session.TargetKind switch
            {
                "Host" when session.TargetId == "whaledeck.host" && session.InstanceId is null && session.Shell is null =>
                    await gateway.OpenHostAsync(id, current.Actor, lifetime.Token),
                "Container" when session.InstanceId is not null && session.Shell is not null =>
                    await containers.OpenContainerAsync(id, current.Actor, session.TargetId, session.InstanceId, session.Shell, lifetime.Token),
                _ => throw new InvalidOperationException("TERMINAL_TARGET_INVALID")
            };
            input = PumpInput(); output = PumpOutput(); monitor = Monitor();
            var ended = await Task.WhenAny(input, output, monitor);
            await ended;
            if (ended == output && exitCode is not null || ended == input && requestedClose) code = "TERMINAL_CLOSED";
        }
        catch (UnauthorizedAccessException) { code = "TERMINAL_FORBIDDEN"; }
        catch (OperationCanceledException) { code = context.RequestAborted.IsCancellationRequested ? "TERMINAL_DISCONNECTED" : "TERMINAL_EXPIRED"; }
        catch (Exception exception)
        {
            code = "TERMINAL_FAILED";
            // Never pass the exception object: driver/SQL/cache exception
            // messages may contain private input. Only stable metadata.
            LogFailure(logger, id, exception.GetType().Name, null);
        }
        finally
        {
            if (connection is not null) await connection.DisposeAsync();
            var closeSent = false;
            // Canceling ManagedWebSocket.ReceiveAsync aborts the socket. Send
            // the close frame first, serialized with output, so the browser
            // receives the actual outcome instead of an unexplained TCP EOF.
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                using var closing = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                var entered = false;
                try
                {
                    await writes.WaitAsync(closing.Token); entered = true;
                    if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                    {
                        await socket.CloseOutputAsync(code == "TERMINAL_CLOSED" ? WebSocketCloseStatus.NormalClosure : WebSocketCloseStatus.PolicyViolation, code, closing.Token);
                        closeSent = true;
                    }
                }
                catch (Exception exception) when (exception is OperationCanceledException or WebSocketException) { }
                finally { if (entered) writes.Release(); }
            }
            // A successful write is not a completed close handshake. Immediate
            // receive cancellation aborts the upgraded HTTP stream and proxies
            // can discard the just-written frame. Give a compliant peer its
            // bounded opportunity to acknowledge before forcing teardown.
            if (closeSent && !input.IsCompleted) await Task.WhenAny(input, Task.Delay(TimeSpan.FromSeconds(2)));
            lifetime.Cancel();
            try { await Task.WhenAll(input, output, monitor); } catch (Exception) { /* No transcript/exception text in logs or responses. */ }
            if (claimed)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try { await sessions.CloseAsync(login.Actor, id, code, exitCode, cleanup.Token); }
                catch (Exception) { /* Lease expiry is authoritative after a database outage. */ }
            }
        }

        async Task PumpInput()
        {
            while (await Message(socket, 8192, lifetime.Token) is { } bytes)
            {
                var frame = TerminalFramePolicy.Input(bytes);
                await connection.WriteAsync(frame, lifetime.Token);
                if (frame.Kind == "close") { requestedClose = true; return; }
            }
        }
        async Task PumpOutput()
        {
            using var ready = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); ready.CancelAfter(TimeSpan.FromSeconds(10));
            while (await connection.ReadAsync(receivedReady ? lifetime.Token : ready.Token) is { } row)
            {
                if (!receivedReady && row.Kind != "ready") throw new InvalidOperationException("TERMINAL_READY_REQUIRED");
                if (!receivedReady && !await sessions.RenewAsync(login.Actor, id, ready.Token)) throw new UnauthorizedAccessException("TERMINAL_SESSION_REVOKED");
                receivedReady = true;
                using var slow = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); slow.CancelAfter(TimeSpan.FromSeconds(5));
                await writes.WaitAsync(slow.Token);
                try
                {
                    if (socket.State != WebSocketState.Open) return;
                    await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(new { kind = row.Kind, data = row.Data, code = row.Code, exitCode = row.ExitCode }), WebSocketMessageType.Text, true, slow.Token);
                }
                finally { writes.Release(); }
                if (row.Kind == "exit") { exitCode = row.ExitCode; return; }
                if (row.Kind == "error") throw new InvalidOperationException("TERMINAL_AGENT_FAILED");
            }
        }
        async Task Monitor()
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(10), lifetime.Token);
                if (!receivedReady) continue;
                var current = await identity.ReadAsync(context, login.Key);
                if (current.Actor.Subject != login.Actor.Subject || current.Actor.LoginBinding != login.Actor.LoginBinding ||
                    !await sessions.RenewAsync(current.Actor, id, lifetime.Token)) throw new UnauthorizedAccessException("TERMINAL_SESSION_REVOKED");
            }
        }
    }

    private static async Task<byte[]?> Message(WebSocket socket, int maximum, CancellationToken cancellationToken)
    {
        var buffer = new byte[maximum + 1]; var count = 0;
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer.AsMemory(count), cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            count += result.Count;
            if (result.MessageType != WebSocketMessageType.Text || count > maximum) throw new ArgumentException("TERMINAL_FRAME_INVALID");
            if (result.EndOfMessage) return buffer[..count];
        }
    }
}
