using System.Globalization;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;

/// <summary>Installed signed UDS and fixed helper; no HTTP, permanent business jobs or UI claim.</summary>
internal static class ContainerPermissionProbe
{
    public static string Phase { get; private set; } = "startup";
    public static async Task RunAsync()
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(180));
        var text = await Console.In.ReadToEndAsync(lifetime.Token); Require(text.Length is > 0 and <= 8192);
        using var document = JsonDocument.Parse(text); var input = document.RootElement;
        Require(input.EnumerateObject().Count() == 6);
        var revision = input.GetProperty("revision").GetString()!; var id = input.GetProperty("containerId").GetString()!;
        var instance = input.GetProperty("instanceId").GetString()!; var core = input.GetProperty("coreId").GetString()!;
        var fileVersion = input.GetProperty("fileVersion").GetString()!; var directoryVersion = input.GetProperty("directoryVersion").GetString()!;
        Require(revision.Length == 40 && revision.All(char.IsAsciiHexDigitLower) && id.Length == 64 && id.All(char.IsAsciiHexDigitLower) && core.Length == 64 && core.All(char.IsAsciiHexDigitLower));
        Require(fileVersion.Length == 64 && directoryVersion.Length == 64);
        Require(Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.EndsWith("+" + revision, StringComparison.Ordinal));
        var key = await File.ReadAllBytesAsync("/etc/whaledeck/agent-capability.key", lifetime.Token);
        try
        {
            Require(key.Length == 32);
            using var handler = new SocketsHttpHandler { UseProxy = false, ConnectCallback = async (_, token) =>
            {
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                try { await socket.ConnectAsync(new UnixDomainSocketEndPoint("/run/whaledeck/agent.sock"), token); return new NetworkStream(socket, ownsSocket: true); }
                catch { socket.Dispose(); throw; }
            } };
            using var channel = GrpcChannel.ForAddress("http://localhost", new() { HttpHandler = handler });
            var client = new ContainerPermissionService.ContainerPermissionServiceClient(channel);
            DateTime Until() => DateTime.UtcNow.AddSeconds(60);
            Metadata Headers(string action, IMessage request, bool wrongDomain = false)
            {
                var hash = ContainerPermissionBinding.Hash(request); var expires = DateTimeOffset.UtcNow.AddSeconds(45).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
                var nonce = Guid.NewGuid().ToString("N"); var method = ContainerPermissionBinding.Service + action;
                var payload = wrongDomain ? ContainerDirectoryBinding.Payload(expires, nonce, method, hash) : ContainerPermissionBinding.Payload(expires, nonce, method, hash);
                return new() { { "x-whaledeck-capability", expires + "." + nonce + "." + Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload))) },
                    { ContainerPermissionBinding.Header, hash } };
            }
            ContainerPermissionPlanRequest Planning(string path, string version, string mode) => new()
            {
                Command = new() { Location = new() { ContainerId = id, InstanceId = instance, Path = path }, Mode = mode, ExpectedVersion = version },
                Context = new() { ActorSubject = "installed-permission-protocol-probe", TraceId = "owned-permission-validation" }
            };
            ContainerPermissionRequest Operation(ContainerPermissionPlan plan)
            {
                var operation = Guid.NewGuid().ToString("N");
                return new() { OperationId = operation, Owner = new('B', 64), Plan = plan,
                    Context = new() { ActorSubject = "installed-permission-protocol-probe", TraceId = "owned-permission-validation", JobId = operation, IdempotencyKey = Guid.NewGuid().ToString("N") } };
            }
            Phase = "capabilities";
            var caps = await new AgentService.AgentServiceClient(channel).GetCapabilitiesAsync(new(), deadline: Until(), cancellationToken: lifetime.Token);
            Require(caps.ProtocolMajor == 1 && caps.ProtocolMinor >= 47 && caps.Capabilities.Contains("docker.files.permission-intents"));
            var planning = Planning("work/original.bin", fileVersion, "0600");
            Phase = "signature-and-protection-rejections";
            await Denied(async () => await client.PlanAsync(planning, deadline: Until(), cancellationToken: lifetime.Token), StatusCode.PermissionDenied);
            await Denied(async () => await client.PlanAsync(planning, Headers("Plan", planning, true), Until(), lifetime.Token), StatusCode.PermissionDenied);
            var changed = planning.Clone(); changed.Command.Mode = "0777";
            await Denied(async () => await client.PlanAsync(changed, Headers("Plan", planning), Until(), lifetime.Token), StatusCode.PermissionDenied);
            var badMode = planning.Clone(); badMode.Command.Mode = "4755";
            await Denied(async () => await client.PlanAsync(badMode, Headers("Plan", badMode), Until(), lifetime.Token), StatusCode.InvalidArgument);
            var corePlan = planning.Clone(); corePlan.Command.Location.ContainerId = core;
            await Denied(async () => await client.PlanAsync(corePlan, Headers("Plan", corePlan), Until(), lifetime.Token), StatusCode.PermissionDenied);
            var readOnly = Planning("tmp/no-readonly-write", fileVersion, "0600");
            await Denied(async () => await client.PlanAsync(readOnly, Headers("Plan", readOnly), Until(), lifetime.Token), StatusCode.PermissionDenied);
            var authority = Headers("Plan", planning);
            var firstPlan = await client.PlanAsync(planning, authority, Until(), lifetime.Token);
            await Denied(async () => await client.PlanAsync(planning, authority, Until(), lifetime.Token), StatusCode.PermissionDenied);
            var receipts = new List<object>();
            async Task<ContainerPermissionReceipt> Change(string name, ContainerPermissionPlan plan, bool canceled = false)
            {
                Phase = name; Require(plan.SchemaVersion == 1 && plan.OwnerUid == 1000 && plan.OwnerGid == 1000 && plan.SourceIdentity.Inode > 0);
                var operation = Operation(plan); var modified = operation.Clone(); modified.Plan.SourceChangedAtUnixNs++;
                await Denied(async () => await client.ApplyAsync(modified, Headers("Apply", operation), Until(), lifetime.Token), StatusCode.PermissionDenied);
                var result = canceled ? await client.DiscardAsync(operation, Headers("Discard", operation), Until(), lifetime.Token)
                    : await client.ApplyAsync(operation, Headers("Apply", operation), Until(), lifetime.Token);
                Require(result.State == (canceled ? "Aborted" : "Completed") && result.HasModeConfirmed && result.ModeConfirmed == !canceled && result.BindingCurrent == !canceled);
                Require(result.Kind == plan.Kind && result.NoChange == plan.NoChange && result.OperationId == operation.OperationId && result.Fingerprint == plan.Fingerprint && result.TargetProof == plan.TargetProof);
                Require(canceled ? !result.HistoryMayExist && !result.HistoryVerified && result.Version.Length == 0 && result.HistoryDigest.Length == 0
                    : result.HistoryMayExist && result.HistoryVerified && result.Version.Length == 64 && result.HistoryDigest.Length == 64);
                foreach (var action in new[] { "Inspect", "Apply", "Discard" })
                {
                    var replay = action switch
                    {
                        "Inspect" => await client.InspectAsync(operation, Headers(action, operation), Until(), lifetime.Token),
                        "Apply" => await client.ApplyAsync(operation, Headers(action, operation), Until(), lifetime.Token),
                        _ => await client.DiscardAsync(operation, Headers(action, operation), Until(), lifetime.Token)
                    };
                    Require(replay.Replayed && replay.State == result.State && replay.Version == result.Version && replay.ModeConfirmed == result.ModeConfirmed && replay.HistoryDigest == result.HistoryDigest);
                }
                receipts.Add(new { name, operationId = operation.OperationId, plan = JsonFormatter.Default.Format(plan), result = JsonFormatter.Default.Format(result) });
                return result;
            }
            async Task<ContainerPermissionPlan> Plan(string path, string version, string mode)
            {
                var request = Planning(path, version, mode);
                return await client.PlanAsync(request, Headers("Plan", request), Until(), lifetime.Token);
            }
            var file = await Change("file-mode", firstPlan);
            var noop = await Change("file-noop", await Plan("work/original.bin", file.Version, "0600")); Require(noop.NoChange && noop.Version == file.Version);
            var restored = await Change("file-restore", await Plan("work/original.bin", noop.Version, "0640"));
            var directory = await Change("directory-mode", await Plan("work/permission-folder", directoryVersion, "0700"));
            await Change("directory-restore", await Plan("work/permission-folder", directory.Version, "0750"));
            await Change("file-cancel", await Plan("work/original.bin", restored.Version, "0600"), canceled: true);
            Phase = "done";
            Console.WriteLine(JsonSerializer.Serialize(new { containerPermissionProbe = "passed", revision, containerId = id, instanceId = instance,
                receipts, coreAndReadonlyRejected = true, bodyBindingAndReplayRejected = true, installedSignedUdsAndFixedHelper = true, humanBffOrUi = false }));
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
    private static void Require(bool condition) { if (!condition) throw new InvalidOperationException("Permission probe invariant failed"); }
    private static async Task Denied(Func<Task> action, StatusCode expected)
    {
        try { await action(); }
        catch (RpcException error) when (error.StatusCode == expected) { return; }
        throw new InvalidOperationException("Permission probe expected refusal");
    }
}
