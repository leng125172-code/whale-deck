using System.Diagnostics;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Google.Protobuf;
using Grpc.Core;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Agent.Services;

/// <summary>Bounded host PTY over capability/peer-checked UDS; never stores terminal text.</summary>
public sealed class HostTerminalGrpcService(HostTerminalRunner terminals, ResourceRegistry registry) : HostTerminalService.HostTerminalServiceBase
{
    public override async Task Connect(IAsyncStreamReader<HostTerminalFrame> requestStream,
        IServerStreamWriter<HostTerminalEvent> responseStream, ServerCallContext context)
    {
        _ = registry.Require("whaledeck.host", "terminal-open");
        using var firstFrame = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        firstFrame.CancelAfter(TimeSpan.FromSeconds(5));
        if (!await requestStream.MoveNext(firstFrame.Token)) throw new ArgumentException("TERMINAL_OPEN_REQUIRED");
        var open = requestStream.Current;
        var input = HostTerminalProtocol.Open(open);
        await terminals.RunAsync(open.SessionId, input, requestStream, responseStream, context.CancellationToken);
    }
}

internal static class HostTerminalProtocol
{
    public static string Open(HostTerminalFrame frame)
    {
        if (!Guid.TryParseExact(frame.SessionId, "N", out var id) || id == Guid.Empty || frame.SessionId != id.ToString("N") ||
            frame.Kind != "open" || frame.Data.Length != 0 || frame.Context is null ||
            string.IsNullOrWhiteSpace(frame.Context.ActorSubject) || frame.Context.ActorSubject.Length > 256 || frame.Context.ActorSubject.Any(char.IsControl))
            throw new ArgumentException("TERMINAL_OPEN_INVALID");
        Dimensions(frame);
        return JsonSerializer.Serialize(new { type = "open", columns = frame.Columns, rows = frame.Rows });
    }

    public static string Input(HostTerminalFrame frame, string sessionId)
    {
        if (frame.SessionId.Length != 0 && frame.SessionId != sessionId || frame.Context is not null)
            throw new ArgumentException("TERMINAL_TARGET_CHANGED");
        switch (frame.Kind)
        {
            case "input" when frame.Data.Length is > 0 and <= 4096 && frame.Columns == 0 && frame.Rows == 0:
                return JsonSerializer.Serialize(new { type = "input", data = frame.Data.ToBase64() });
            case "resize" when frame.Data.Length == 0:
                Dimensions(frame);
                return JsonSerializer.Serialize(new { type = "resize", columns = frame.Columns, rows = frame.Rows });
            case "close" when frame.Data.Length == 0 && frame.Columns == 0 && frame.Rows == 0:
                return "{\"type\":\"close\"}";
            default: throw new ArgumentException("TERMINAL_FRAME_INVALID");
        }
    }

    public static HostTerminalEvent Output(string line)
    {
        if (line.Length > 8192) throw new InvalidOperationException("TERMINAL_OUTPUT_INVALID");
        using var document = JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = 4 });
        var root = document.RootElement;
        var kind = root.GetProperty("type").GetString();
        if (kind is not ("ready" or "output" or "heartbeat" or "error" or "exit")) throw new InvalidOperationException("TERMINAL_OUTPUT_INVALID");
        var data = kind == "output" ? Convert.FromBase64String(root.GetProperty("data").GetString()!) : [];
        if (data.Length > 4096) throw new InvalidOperationException("TERMINAL_OUTPUT_INVALID");
        var code = root.TryGetProperty("code", out var codeValue) ? codeValue.GetString() ?? string.Empty : string.Empty;
        if (code.Length > 64 || code.Any(character => !(char.IsAsciiLetterUpper(character) || character == '_'))) throw new InvalidOperationException("TERMINAL_OUTPUT_INVALID");
        return new() { Kind = kind, Data = ByteString.CopyFrom(data), Code = code, ExitCode = root.TryGetProperty("exitCode", out var status) ? status.GetInt32() : 0 };
    }

    private static void Dimensions(HostTerminalFrame frame)
    {
        if (frame.Columns is < 2 or > 400 || frame.Rows is < 2 or > 400) throw new ArgumentException("TERMINAL_SIZE_INVALID");
    }
}

public sealed class HostTerminalRunner : IDisposable
{
    private readonly SemaphoreSlim slots = new(4);
    private readonly ConcurrentDictionary<string, byte> active = new(StringComparer.Ordinal);
    private const string Helper = "/usr/local/libexec/whaledeck-privileged";

    public async Task RunAsync(string sessionId, string open, IAsyncStreamReader<HostTerminalFrame> requests,
        IServerStreamWriter<HostTerminalEvent> responses, CancellationToken cancellationToken)
        => await RunCoreAsync("host-terminal", sessionId, open, requests, responses, cancellationToken);

    public async Task RunContainerAsync(string sessionId, string open, IAsyncStreamReader<HostTerminalFrame> requests,
        IServerStreamWriter<HostTerminalEvent> responses, CancellationToken cancellationToken)
        => await RunCoreAsync("container-terminal", sessionId, open, requests, responses, cancellationToken);

    private async Task RunCoreAsync(string action, string sessionId, string open, IAsyncStreamReader<HostTerminalFrame> requests,
        IServerStreamWriter<HostTerminalEvent> responses, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux()) throw new InvalidOperationException("TERMINAL_LINUX_REQUIRED");
        if (!await slots.WaitAsync(0, cancellationToken)) throw new RpcException(new Status(StatusCode.ResourceExhausted, "TERMINAL_SESSION_LIMIT"));
        if (!active.TryAdd(sessionId, 0)) { slots.Release(); throw new RpcException(new Status(StatusCode.AlreadyExists, "TERMINAL_SESSION_EXISTS")); }
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lifetime.CancelAfter(TimeSpan.FromMinutes(10));
        using var process = new Process { StartInfo = Start(action, sessionId) };
        Task input = Task.CompletedTask, output = Task.CompletedTask, errors = Task.CompletedTask;
        string? invocationId = null;
        try
        {
            if (!process.Start()) throw new InvalidOperationException("TERMINAL_START_FAILED");
            errors = process.StandardError.BaseStream.CopyToAsync(Stream.Null, lifetime.Token);
            await process.StandardInput.WriteLineAsync(open.AsMemory(), lifetime.Token);
            await process.StandardInput.FlushAsync(lifetime.Token);
            input = PumpInput(); output = PumpOutput();
            var ended = await Task.WhenAny(input, output);
            await ended;
            if (ended == input)
            {
                process.StandardInput.Close();
                await output.WaitAsync(TimeSpan.FromSeconds(10), lifetime.Token);
            }
        }
        finally
        {
            lifetime.Cancel();
            try { if (process.Id > 0) process.StandardInput.Close(); } catch (InvalidOperationException) { }
            // A rejected duplicate/start failure must never stop a different
            // unit that already owns this UUID. Stop only the root receipt's
            // exact systemd invocation, not an unbound name/PID.
            try { if (invocationId is not null) await StopAsync(sessionId, invocationId); }
            finally
            {
                try { await Task.WhenAll(input, output, errors); }
                catch (Exception exception) when (exception is OperationCanceledException or IOException or InvalidOperationException or RpcException) { }
                active.TryRemove(sessionId, out _);
                slots.Release();
            }
        }

        async Task PumpInput()
        {
            while (await requests.MoveNext(lifetime.Token))
            {
                var frame = requests.Current;
                var text = HostTerminalProtocol.Input(frame, sessionId);
                await process.StandardInput.WriteLineAsync(text.AsMemory(), lifetime.Token).WaitAsync(TimeSpan.FromSeconds(3), lifetime.Token);
                await process.StandardInput.FlushAsync(lifetime.Token);
                if (frame.Kind == "close") break;
            }
        }
        async Task PumpOutput()
        {
            await foreach (var line in Lines(process.StandardOutput.BaseStream, lifetime.Token))
            {
                var frame = HostTerminalProtocol.Output(line);
                if (frame.Kind == "ready")
                {
                    using var receipt = JsonDocument.Parse(line);
                    var instance = receipt.RootElement.GetProperty("invocationId").GetString();
                    if (!Guid.TryParseExact(instance, "N", out var parsed) || parsed == Guid.Empty) throw new InvalidOperationException("TERMINAL_RECEIPT_INVALID");
                    invocationId = parsed.ToString("N");
                }
                await responses.WriteAsync(frame, lifetime.Token).WaitAsync(TimeSpan.FromSeconds(10), lifetime.Token);
            }
            await process.WaitForExitAsync(lifetime.Token);
            if (process.ExitCode != 0) throw new InvalidOperationException("TERMINAL_HELPER_REJECTED");
        }
    }

    private static ProcessStartInfo Start(string action, string id)
    {
        var start = new ProcessStartInfo("/usr/bin/sudo") { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        start.ArgumentList.Add("-n"); start.ArgumentList.Add(Helper); start.ArgumentList.Add(action); start.ArgumentList.Add(id);
        start.Environment.Clear(); start.Environment["PATH"] = "/usr/sbin:/usr/bin:/sbin:/bin"; start.Environment["LANG"] = "C.UTF-8";
        return start;
    }
    private static async Task StopAsync(string id, string invocationId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var start = Start("host-terminal-stop", id); start.ArgumentList.Add(invocationId);
        using var stop = Process.Start(start);
        if (stop is null) return;
        stop.StandardInput.Close();
        await Task.WhenAll(stop.StandardOutput.BaseStream.CopyToAsync(Stream.Null, timeout.Token), stop.StandardError.BaseStream.CopyToAsync(Stream.Null, timeout.Token), stop.WaitForExitAsync(timeout.Token));
    }
    private static async IAsyncEnumerable<string> Lines(Stream source, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var buffer = new byte[4096]; var line = new List<byte>(8192);
        int count;
        while ((count = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            for (var index = 0; index < count; index++)
            {
                if (buffer[index] == (byte)'\n') { yield return Encoding.UTF8.GetString(line.ToArray()); line.Clear(); }
                else if (line.Count >= 8192) throw new InvalidOperationException("TERMINAL_OUTPUT_INVALID");
                else line.Add(buffer[index]);
            }
        }
        if (line.Count != 0) throw new InvalidOperationException("TERMINAL_OUTPUT_TRUNCATED");
    }
    public void Dispose() => slots.Dispose();
}
