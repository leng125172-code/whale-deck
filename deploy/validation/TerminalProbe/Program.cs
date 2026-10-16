using System.Diagnostics;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using WhaleDeck.Contracts.Agent.V1;

try
{
    await TerminalProbe.RunAsync();
    return 0;
}
catch (Exception error)
{
    // No exception text, terminal transcript, credential or capability output.
    Console.Error.WriteLine(JsonSerializer.Serialize(new { terminalProbe = "failed", phase = TerminalProbe.Phase, failureType = error.GetType().Name }));
    return 1;
}

internal static partial class TerminalProbe
{
    private const string Method = "/whaledeck.agent.v1.HostTerminalService/Connect";
    public static string Phase { get; private set; } = "startup";
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(20);
    private static readonly string[] KillArguments = ["kill", "--kill-whom=main", "--signal=KILL"];

    public static async Task RunAsync()
    {
        if (!OperatingSystem.IsLinux()) throw new InvalidOperationException("Linux required");
        var key = File.ReadAllBytes("/etc/whaledeck/agent-capability.key");
        using var handler = new SocketsHttpHandler { UseProxy = false, ConnectCallback = async (_, token) =>
        {
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try { await socket.ConnectAsync(new UnixDomainSocketEndPoint("/run/whaledeck/agent.sock"), token); return new NetworkStream(socket, true); }
            catch { socket.Dispose(); throw; }
        } };
        using var channel = GrpcChannel.ForAddress("http://localhost", new() { HttpHandler = handler });
        var client = new HostTerminalService.HostTerminalServiceClient(channel);
        try
        {
            Phase = "capabilities";
            var capabilities = await new AgentService.AgentServiceClient(channel).GetCapabilitiesAsync(new());
            Check(capabilities.ProtocolMajor == 1 && capabilities.ProtocolMinor >= 18 && capabilities.Capabilities.Contains("host.terminal.pty"));
            string Token(string method = Method)
            {
                var prefix = DateTimeOffset.UtcNow.AddSeconds(45).ToUnixTimeSeconds() + "." + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
                return prefix + "." + Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(prefix + "." + method)));
            }
            Metadata Headers(string? token = null) => new() { { "x-whaledeck-capability", token ?? Token() } };
            AsyncDuplexStreamingCall<HostTerminalFrame, HostTerminalEvent> Connect(Metadata headers) => client.Connect(headers, deadline: DateTime.UtcNow.Add(Budget));
            async Task Denied(string id, Metadata headers, StatusCode expected)
            {
                using var call = Connect(headers);
                try
                {
                    await call.RequestStream.WriteAsync(Open(id));
                    await call.RequestStream.CompleteAsync();
                    while (await call.ResponseStream.MoveNext()) Check(call.ResponseStream.Current.Kind != "ready");
                    throw new InvalidOperationException("Rejected call succeeded");
                }
                catch (RpcException error) when (error.StatusCode == expected) { }
            }
            Phase = "capability-rejection";
            await Denied(Guid.NewGuid().ToString("N"), [], StatusCode.PermissionDenied);
            await Denied(Guid.NewGuid().ToString("N"), Headers(Token("/whaledeck.agent.v1.HostService/GetHostInfo")), StatusCode.PermissionDenied);
            await Denied("../invalid", Headers(), StatusCode.InvalidArgument);

            var id = Guid.NewGuid().ToString("N"); var capability = Token();
            var transcriptMarker = "wd-terminal-transcript-" + Guid.NewGuid().ToString("N");
            using (var call = Connect(Headers(capability)))
            {
                Phase = "normal-open";
                await call.RequestStream.WriteAsync(Open(id)); await Ready(call);
                // A second valid capability with the same active ID must not
                // launch a helper or stop the original session during cleanup.
                Phase = "active-duplicate";
                await Denied(id, Headers(), StatusCode.AlreadyExists);
                Phase = "resize-and-output";
                await call.RequestStream.WriteAsync(new() { Kind = "resize", Columns = 120, Rows = 40 });
                await Input(call, "printf '%s\\n' '" + transcriptMarker + "'\n");
                await Input(call, "printf 'WD_UID=%s\\n' \"$(id -u)\"; printf 'WD_GROUPS=%s\\n' \"$(id -Gn)\"; printf 'WD_HOME=%s\\n' \"$PWD\"; stty size; printf 'WD_KEY_%s\\n' \"$(test -r /etc/whaledeck/agent-capability.key && printf YES || printf NO)\"; exit 7\n");
                var text = new StringBuilder(); var exit = false;
                while (await call.ResponseStream.MoveNext())
                {
                    var row = call.ResponseStream.Current; Capture(text, row);
                    if (row.Kind == "exit") { Check(row.ExitCode == 7); exit = true; await call.RequestStream.CompleteAsync(); }
                    Check(row.Kind != "error");
                }
                var uid = File.ReadLines("/etc/passwd").Single(line => line.StartsWith("whaledeck-shell:", StringComparison.Ordinal)).Split(':')[2];
                Check(exit && text.ToString().Contains(transcriptMarker, StringComparison.Ordinal) && text.ToString().Contains("WD_UID=" + uid, StringComparison.Ordinal) && text.ToString().Contains("WD_GROUPS=whaledeck-shell", StringComparison.Ordinal) &&
                    text.ToString().Contains("WD_HOME=/var/lib/whaledeck-shell", StringComparison.Ordinal) && text.ToString().Contains("40 120", StringComparison.Ordinal) && text.ToString().Contains("WD_KEY_NO", StringComparison.Ordinal));
            }
            Phase = "transcript-not-in-journal-or-record";
            var journalStart = new ProcessStartInfo("/usr/bin/journalctl") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            journalStart.ArgumentList.Add("-u"); journalStart.ArgumentList.Add("whaledeck-terminal-" + id + ".service");
            journalStart.ArgumentList.Add("--no-pager"); journalStart.ArgumentList.Add("-o"); journalStart.ArgumentList.Add("json"); journalStart.ArgumentList.Add("-n"); journalStart.ArgumentList.Add("50");
            using (var journal = Process.Start(journalStart) ?? throw new InvalidOperationException())
            {
                var text = await journal.StandardOutput.ReadToEndAsync();
                await journal.StandardError.BaseStream.CopyToAsync(Stream.Null); await journal.WaitForExitAsync();
                Check(journal.ExitCode == 0 && text.Length <= 1048576 && !text.Contains(transcriptMarker, StringComparison.Ordinal));
                foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    using var entry = JsonDocument.Parse(line);
                    if (entry.RootElement.TryGetProperty("MESSAGE", out var message) && message.ValueKind == JsonValueKind.String)
                        Check(!message.GetString()!.StartsWith("{\"type\":", StringComparison.Ordinal));
                }
            }
            Check(!File.ReadAllText("/var/lib/whaledeck-terminal/" + id + ".json").Contains(transcriptMarker, StringComparison.Ordinal));
            Phase = "capability-and-uuid-replay";
            await Denied(Guid.NewGuid().ToString("N"), Headers(capability), StatusCode.PermissionDenied);
            await Denied(id, Headers(), StatusCode.FailedPrecondition);

            // Disconnect and a killed driver must both remove a descendant
            // that deliberately created a new POSIX session. No production
            // service or arbitrary PID is signaled by this probe.
            Phase = "disconnect-cgroup-cleanup"; await ChildCleanup(killDriver: false);
            Phase = "driver-kill-cgroup-cleanup"; await ChildCleanup(killDriver: true);
            Console.WriteLine(JsonSerializer.Serialize(new { nativeHostTerminal = "passed", udsMethodCapabilityReplay = "passed", fixedUnprivilegedIdentityResizeExit = "passed",
                activeDuplicateDoesNotStopOriginal = "passed", eofAndKilledDriverCgroupCleanup = "passed", journalAndRootRecordContainNoTranscript = true, productionContainersModified = false }));

            async Task ChildCleanup(bool killDriver)
            {
                var session = Guid.NewGuid().ToString("N");
                using var call = Connect(Headers());
                await call.RequestStream.WriteAsync(Open(session)); await Ready(call);
                await Input(call, "python3 -c 'import os,time;os.setsid();time.sleep(300)' & printf 'WD_CHILD=%s\\n' \"$!\"\n");
                var text = new StringBuilder(); Match match;
                do
                {
                    Check(await call.ResponseStream.MoveNext()); Capture(text, call.ResponseStream.Current);
                    match = Child().Match(text.ToString());
                } while (!match.Success);
                var pid = int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                var instance = ProcessStart(pid); Check(instance is not null);
                if (killDriver)
                {
                    var start = new ProcessStartInfo("/usr/bin/systemctl") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                    foreach (var arg in KillArguments) start.ArgumentList.Add(arg);
                    start.ArgumentList.Add("whaledeck-terminal-" + session + ".service");
                    using var kill = Process.Start(start) ?? throw new InvalidOperationException();
                    await Task.WhenAll(kill.StandardOutput.BaseStream.CopyToAsync(Stream.Null), kill.StandardError.BaseStream.CopyToAsync(Stream.Null), kill.WaitForExitAsync());
                    Check(kill.ExitCode == 0);
                }
                call.Dispose();
                for (var attempt = 0; attempt < 50 && ProcessStart(pid) == instance; attempt++) await Task.Delay(100);
                Check(ProcessStart(pid) != instance);
            }
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    private static HostTerminalFrame Open(string id) => new() { SessionId = id, Kind = "open", Columns = 80, Rows = 24, Context = new() { ActorSubject = "whaledeck-native-terminal-probe" } };
    private static Task Input(AsyncDuplexStreamingCall<HostTerminalFrame, HostTerminalEvent> call, string value) => call.RequestStream.WriteAsync(new() { Kind = "input", Data = ByteString.CopyFromUtf8(value) });
    private static async Task Ready(AsyncDuplexStreamingCall<HostTerminalFrame, HostTerminalEvent> call)
    {
        Check(await call.ResponseStream.MoveNext()); Check(call.ResponseStream.Current.Kind == "ready");
    }
    private static void Capture(StringBuilder target, HostTerminalEvent row)
    {
        if (row.Kind == "output") target.Append(row.Data.ToStringUtf8());
        Check(target.Length <= 65536);
    }
    private static string? ProcessStart(int pid)
    {
        try { var row = File.ReadAllText($"/proc/{pid}/stat"); return row[(row.LastIndexOf(')') + 2)..].Split(' ')[19]; }
        catch (IOException) { return null; }
    }
    private static void Check(bool valid) { if (!valid) throw new InvalidOperationException("Terminal acceptance failed"); }
    [GeneratedRegex("WD_CHILD=([0-9]{1,10})")]
    private static partial Regex Child();
}
