using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using WhaleDeck.Contracts.Agent.V1;

internal static class ContainerTerminalProbe
{
    private const string ConnectMethod = "/whaledeck.agent.v1.ContainerTerminalService/Connect";
    private const string InspectMethod = "/whaledeck.agent.v1.ContainerTerminalService/Inspect";
    public static async Task RunAsync()
    {
        if (!OperatingSystem.IsLinux()) throw new InvalidOperationException();
        var key = File.ReadAllBytes("/etc/whaledeck/agent-capability.key");
        var name = "whaledeck-container-exec-validation-" + Guid.NewGuid().ToString("N");
        string? containerId = null;
        var phase = "startup";
        try
        {
            using var handler = new SocketsHttpHandler { UseProxy = false, ConnectCallback = async (_, token) =>
            {
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                try { await socket.ConnectAsync(new UnixDomainSocketEndPoint("/run/whaledeck/agent.sock"), token); return new NetworkStream(socket, true); }
                catch { socket.Dispose(); throw; }
            } };
            using var channel = GrpcChannel.ForAddress("http://localhost", new() { HttpHandler = handler });
            var client = new ContainerTerminalService.ContainerTerminalServiceClient(channel);
            Metadata Headers(string method = ConnectMethod, string? fixedToken = null)
            {
                var prefix = DateTimeOffset.UtcNow.AddSeconds(45).ToUnixTimeSeconds() + "." + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
                return new() { { "x-whaledeck-capability", fixedToken ?? prefix + "." + Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(prefix + "." + method))) } };
            }
            phase = "capability";
            var capabilities = await new AgentService.AgentServiceClient(channel).GetCapabilitiesAsync(new());
            Check(capabilities.ProtocolMajor == 1 && capabilities.ProtocolMinor >= 19 && capabilities.Capabilities.Contains("docker.terminal.exec"));
            var image = await Command("/usr/bin/docker", ["inspect", "--format", "{{.Image}}", "whaledeck-worker"]);
            Check(image.StartsWith("sha256:", StringComparison.Ordinal) && image.Length == 71);
            phase = "isolated-container";
            containerId = await Command("/usr/bin/docker", ["run", "--detach", "--name", name, "--network", "none", "--read-only", "--cap-drop", "ALL",
                "--security-opt", "no-new-privileges", "--pids-limit", "32", "--memory", "128m", "--cpus", "0.25", "--user", "1000:1000",
                "--entrypoint", "/bin/sh", image, "-c", "exec sleep 600"]);
            Check(Id(containerId));
            var target = await client.InspectAsync(new() { ResourceId = containerId, ResourceType = "Container" }, Headers(InspectMethod));
            Check(target.ContainerId == containerId && target.ConfiguredUser == "1000:1000");
            var instance = target.InstanceId;
            async Task Denied(ContainerTerminalFrame frame, Metadata headers, StatusCode expected)
            {
                using var rejected = client.Connect(headers, deadline: DateTime.UtcNow.AddSeconds(20));
                try
                {
                    await rejected.RequestStream.WriteAsync(frame); await rejected.RequestStream.CompleteAsync();
                    while (await rejected.ResponseStream.MoveNext()) Check(rejected.ResponseStream.Current.Kind != "ready");
                    throw new InvalidOperationException();
                }
                catch (RpcException error) when (error.StatusCode == expected) { }
            }
            ContainerTerminalFrame Open(string session) => new()
            {
                ContainerId = containerId, InstanceId = instance, Shell = "sh",
                Payload = new() { SessionId = session, Kind = "open", Columns = 80, Rows = 24, Context = new() { ActorSubject = "isolated-native-validator" } }
            };
            phase = "core-and-method-protection";
            var core = await Command("/usr/bin/docker", ["inspect", "--format", "{{.Id}}", "whaledeck-api"]);
            await AssertRpc(async () => { await client.InspectAsync(new() { ResourceId = core, ResourceType = "Container" }, Headers(InspectMethod)); }, StatusCode.PermissionDenied);
            await Denied(Open(Guid.NewGuid().ToString("N")), [], StatusCode.PermissionDenied);
            await Denied(Open(Guid.NewGuid().ToString("N")), Headers(InspectMethod), StatusCode.PermissionDenied);
            var stale = Open(Guid.NewGuid().ToString("N")); stale.InstanceId = "1:2000-01-01T00:00:00Z";
            await Denied(stale, Headers(), StatusCode.FailedPrecondition);
            var injection = Open(Guid.NewGuid().ToString("N")); injection.Shell = "sh -c id";
            await Denied(injection, Headers(), StatusCode.InvalidArgument);
            phase = "real-exec-resize-exit";
            var session = Guid.NewGuid().ToString("N"); var marker = "wd-exec-transcript-" + Guid.NewGuid().ToString("N");
            var capability = Headers();
            using (var call = client.Connect(capability, deadline: DateTime.UtcNow.AddSeconds(30)))
            {
                await call.RequestStream.WriteAsync(Open(session)); await Ready(call);
                await Denied(Open(session), Headers(), StatusCode.AlreadyExists);
                await call.RequestStream.WriteAsync(new() { Payload = new() { Kind = "resize", Columns = 120, Rows = 40 } });
                await Input(call, "printf '%s\\n' '" + marker + "'; printf 'WD_CONTAINER_UID=%s\\n' \"$(id -u)\"; stty size; exit 7\n");
                var text = new StringBuilder(); var exited = false;
                while (await call.ResponseStream.MoveNext())
                {
                    var row = call.ResponseStream.Current; Capture(text, row);
                    if (row.Kind == "exit") { Check(row.ExitCode == 7); exited = true; await call.RequestStream.CompleteAsync(); }
                    Check(row.Kind != "error");
                }
                Check(exited && text.ToString().Contains(marker, StringComparison.Ordinal) && text.ToString().Contains("WD_CONTAINER_UID=1000", StringComparison.Ordinal) && text.ToString().Contains("40 120", StringComparison.Ordinal));
            }
            await Closed(session, marker);
            phase = "capability-and-uuid-replay";
            await Denied(Open(Guid.NewGuid().ToString("N")), capability, StatusCode.PermissionDenied);
            await Denied(Open(session), Headers(), StatusCode.FailedPrecondition);
            phase = "disconnect-exec-cleanup"; await Cleanup(false);
            phase = "driver-kill-exec-cleanup"; await Cleanup(true);
            phase = "container-still-running";
            Check((await client.InspectAsync(new() { ResourceId = containerId, ResourceType = "Container" }, Headers(InspectMethod))).InstanceId == instance);
            Console.WriteLine(JsonSerializer.Serialize(new { nativeContainerTerminal = "passed", capabilityCoreAndInstanceBinding = "passed", configuredUserResizeExitAndReplay = "passed",
                disconnectAndKilledDriverEndOnlyInteractiveExec = "passed", noTerminalTranscriptInJournalOrReceipt = true, productionContainersModified = false }));

            async Task Cleanup(bool killDriver)
            {
                var id = Guid.NewGuid().ToString("N");
                using var call = client.Connect(Headers(), deadline: DateTime.UtcNow.AddSeconds(30));
                await call.RequestStream.WriteAsync(Open(id)); await Ready(call);
                using var receipt = JsonDocument.Parse(await File.ReadAllTextAsync("/var/lib/whaledeck-terminal/" + id + ".json"));
                var pid = receipt.RootElement.GetProperty("execPid").GetInt32(); var start = receipt.RootElement.GetProperty("execStart").GetString();
                if (killDriver)
                    _ = await Command("/usr/bin/systemctl", ["kill", "--kill-whom=main", "--signal=KILL", "whaledeck-terminal-" + id + ".service"]);
                call.Dispose();
                await Closed(id, null);
                Check(ProcessStart(pid) != start);
                Check((await client.InspectAsync(new() { ResourceId = containerId, ResourceType = "Container" }, Headers(InspectMethod))).InstanceId == instance);
            }
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(JsonSerializer.Serialize(new { nativeContainerTerminal = "failed", phase, failureType = error.GetType().Name }));
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            if (containerId is not null && Id(containerId))
            {
                Check(await Command("/usr/bin/docker", ["inspect", "--format", "{{.Name}}", containerId]) == "/" + name);
                _ = await Command("/usr/bin/docker", ["rm", "--force", containerId]);
                Console.WriteLine("{\"isolatedExecContainerRemoved\":true}");
            }
        }
    }

    private static bool Id(string value) => value.Length == 64 && value.All(character => char.IsAsciiDigit(character) || character is >= 'a' and <= 'f');
    private static void Check(bool condition) { if (!condition) throw new InvalidOperationException(); }
    private static async Task Ready(AsyncDuplexStreamingCall<ContainerTerminalFrame, HostTerminalEvent> call)
    { Check(await call.ResponseStream.MoveNext() && call.ResponseStream.Current.Kind == "ready"); }
    private static Task Input(AsyncDuplexStreamingCall<ContainerTerminalFrame, HostTerminalEvent> call, string value) =>
        call.RequestStream.WriteAsync(new() { Payload = new() { Kind = "input", Data = ByteString.CopyFromUtf8(value) } });
    private static void Capture(StringBuilder text, HostTerminalEvent frame)
    {
        if (frame.Kind == "output") text.Append(frame.Data.ToStringUtf8());
        Check(text.Length <= 65536);
    }
    private static string? ProcessStart(int pid)
    {
        try { var value = File.ReadAllText("/proc/" + pid.ToString(CultureInfo.InvariantCulture) + "/stat"); return value[(value.LastIndexOf(')') + 2)..].Split(' ')[19]; }
        catch (IOException) { return null; }
    }
    private static async Task Closed(string id, string? marker)
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            var text = await File.ReadAllTextAsync("/var/lib/whaledeck-terminal/" + id + ".json");
            using var document = JsonDocument.Parse(text);
            if (!document.RootElement.GetProperty("active").GetBoolean())
            {
                if (marker is not null)
                {
                    Check(!text.Contains(marker, StringComparison.Ordinal));
                    var journal = await Command("/usr/bin/journalctl", ["-u", "whaledeck-terminal-" + id + ".service", "--no-pager", "-o", "cat", "-n", "50"]);
                    Check(!journal.Contains(marker, StringComparison.Ordinal) && !journal.Contains("\"type\":\"output\"", StringComparison.Ordinal));
                }
                return;
            }
            await Task.Delay(200);
        }
        throw new InvalidOperationException();
    }
    private static async Task AssertRpc(Func<Task> action, StatusCode status)
    {
        try { await action(); throw new InvalidOperationException(); }
        catch (RpcException error) when (error.StatusCode == status) { }
    }
    private static async Task<string> Command(string executable, string[] arguments)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException();
        var output = process.StandardOutput.ReadToEndAsync(deadline.Token); var error = process.StandardError.BaseStream.CopyToAsync(Stream.Null, deadline.Token);
        await Task.WhenAll(output, error, process.WaitForExitAsync(deadline.Token));
        Check(process.ExitCode == 0 && output.Result.Length <= 1048576);
        return output.Result.Trim();
    }
}
