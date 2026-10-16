using System.Net.WebSockets;
using System.Text.Json;
using WhaleDeck.Api.Security;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Api.Terminals;

/// <summary>HTTP/WS transport only. Business state, tokens, authorization and durable audit live in Application.</summary>
public sealed class HostTerminalWebSocket(TerminalSessionService sessions, TerminalCookieIdentity identity, IHostTerminalGateway gateway, ILogger<HostTerminalWebSocket> logger)
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
        Task input = Task.CompletedTask, output = Task.CompletedTask, monitor = Task.CompletedTask;
        try
        {
            using var first = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); first.CancelAfter(TimeSpan.FromSeconds(5));
            var packet = await Message(socket, 256, first.Token) ?? throw new ArgumentException("TERMINAL_TICKET_REQUIRED");
            var token = TerminalFramePolicy.Grant(packet);
            await sessions.ClaimAsync(login.Actor, id, token, first.Token); claimed = true;
            // Re-read the live server ticket immediately before capability
            // signing; a cached AuthenticateAsync result cannot prove logout.
            var current = await identity.ReadAsync(context, login.Key);
            if (current.Actor.Subject != login.Actor.Subject || current.Actor.LoginBinding != login.Actor.LoginBinding ||
                !await sessions.RenewAsync(current.Actor, id, first.Token)) throw new UnauthorizedAccessException("TERMINAL_SESSION_REVOKED");
            connection = await gateway.OpenHostAsync(id, login.Actor, lifetime.Token);
            input = PumpInput(); output = PumpOutput(); monitor = Monitor();
            await await Task.WhenAny(input, output, monitor);
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
            lifetime.Cancel();
            if (connection is not null) await connection.DisposeAsync();
            try { await Task.WhenAll(input, output, monitor); } catch (Exception) { /* No transcript/exception text in logs or responses. */ }
            if (claimed)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try { await sessions.CloseAsync(login.Actor, id, code, exitCode, cleanup.Token); }
                catch (Exception) { /* Lease expiry is authoritative after a database outage. */ }
            }
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                using var closing = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try { await socket.CloseOutputAsync(code == "TERMINAL_CLOSED" ? WebSocketCloseStatus.NormalClosure : WebSocketCloseStatus.PolicyViolation, code, closing.Token); }
                catch (Exception exception) when (exception is OperationCanceledException or WebSocketException) { }
            }
        }

        async Task PumpInput()
        {
            while (await Message(socket, 8192, lifetime.Token) is { } bytes)
            {
                var frame = TerminalFramePolicy.Input(bytes);
                await connection.WriteAsync(frame, lifetime.Token);
                if (frame.Kind == "close") { code = "TERMINAL_CLOSED"; return; }
            }
        }
        async Task PumpOutput()
        {
            using var ready = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); ready.CancelAfter(TimeSpan.FromSeconds(10));
            var receivedReady = false;
            while (await connection.ReadAsync(receivedReady ? lifetime.Token : ready.Token) is { } row)
            {
                if (!receivedReady && row.Kind != "ready") throw new InvalidOperationException("TERMINAL_READY_REQUIRED");
                receivedReady = true;
                using var slow = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); slow.CancelAfter(TimeSpan.FromSeconds(5));
                await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(new { kind = row.Kind, data = row.Data, code = row.Code, exitCode = row.ExitCode }), WebSocketMessageType.Text, true, slow.Token);
                if (row.Kind == "exit") { exitCode = row.ExitCode; code = "TERMINAL_CLOSED"; return; }
                if (row.Kind == "error") throw new InvalidOperationException("TERMINAL_AGENT_FAILED");
            }
        }
        async Task Monitor()
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(10), lifetime.Token);
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
