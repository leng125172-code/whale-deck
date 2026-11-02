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

/// <summary>Private line-framed installed RPC probe. Python independently checks physical inodes after each response.</summary>
internal static class ContainerRecursiveOwnershipProbe
{
    public static string Phase { get; private set; } = "startup";
    public static async Task RunAsync()
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(360));
        var headerText = await Console.In.ReadLineAsync(lifetime.Token); Require(headerText is { Length: > 0 and <= 8192 });
        using var header = JsonDocument.Parse(headerText!); var value = header.RootElement;
        Require(value.EnumerateObject().Count() == 4);
        var revision = value.GetProperty("revision").GetString()!; var id = value.GetProperty("containerId").GetString()!;
        var instance = value.GetProperty("instanceId").GetString()!; var core = value.GetProperty("coreId").GetString()!;
        Require(revision.Length == 40 && revision.All(char.IsAsciiHexDigitLower) && id.Length == 64 && id.All(char.IsAsciiHexDigitLower) && core.Length == 64 && core.All(char.IsAsciiHexDigitLower));
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
            var client = new ContainerRecursiveOwnershipService.ContainerRecursiveOwnershipServiceClient(channel);
            var formatter = new JsonFormatter(new JsonFormatter.Settings(formatDefaultValues: true));
            var operations = new Dictionary<string, ContainerRecursiveOwnershipRequest>(StringComparer.Ordinal);
            DateTime Until() => DateTime.UtcNow.AddSeconds(40);
            Metadata Headers(string action, IMessage request, string variant = "")
            {
                var hash = ContainerRecursiveOwnershipBinding.Hash(request);
                var expires = DateTimeOffset.UtcNow.AddSeconds(variant == "expired" ? -60 : 45).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
                var nonce = Guid.NewGuid().ToString("N"); var method = ContainerRecursiveOwnershipBinding.Service + action;
                var payload = variant == "domain" ? ContainerRecursivePermissionBinding.Payload(expires, nonce, method, hash) : ContainerRecursiveOwnershipBinding.Payload(expires, nonce, method, hash);
                return new() { { "x-whaledeck-capability", expires + "." + nonce + "." + Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload))) }, { ContainerRecursiveOwnershipBinding.Header, hash } };
            }
            async Task Emit(object response) { await Console.Out.WriteLineAsync(JsonSerializer.Serialize(response)); await Console.Out.FlushAsync(); }
            Phase = "capabilities";
            var caps = await new AgentService.AgentServiceClient(channel).GetCapabilitiesAsync(new(), deadline: Until(), cancellationToken: lifetime.Token);
            Require(caps.ProtocolMajor == 1 && caps.ProtocolMinor >= 50 && caps.Capabilities.Contains("docker.files.recursive-ownership-intents"));
            await Emit(new { kind = "ready", revision, protocolMinor = caps.ProtocolMinor });
            for (var count = 0; count < 64; count++)
            {
                var text = await Console.In.ReadLineAsync(lifetime.Token); Require(text is { Length: > 0 and <= 65536 });
                using var document = JsonDocument.Parse(text!); var input = document.RootElement; var action = input.GetProperty("action").GetString()!;
                Phase = action;
                if (action == "quit") { await Emit(new { kind = "done" }); return; }
                if (action == "plan")
                {
                    Require(input.EnumerateObject().Count() == 5);
                    var request = new ContainerRecursiveOwnershipPlanRequest
                    {
                        Command = new() { Location = new() { ContainerId = id, InstanceId = instance, Path = "work/recursive-tree" },
                            Uid = input.GetProperty("uid").GetUInt32(), Gid = input.GetProperty("gid").GetUInt32(), ExpectedVersion = input.GetProperty("version").GetString()! },
                        Context = new() { ActorSubject = "installed-recursive-protocol-probe", TraceId = "owned-recursive-validation" }
                    };
                    var boundaries = input.GetProperty("boundaries").GetBoolean();
                    if (boundaries)
                    {
                        await Denied(async () => await client.PlanAsync(request, deadline: Until(), cancellationToken: lifetime.Token), StatusCode.PermissionDenied);
                        await Denied(async () => await client.PlanAsync(request, Headers("Plan", request, "domain"), Until(), lifetime.Token), StatusCode.PermissionDenied);
                        await Denied(async () => await client.PlanAsync(request, Headers("Plan", request, "expired"), Until(), lifetime.Token), StatusCode.PermissionDenied);
                        var changed = request.Clone(); changed.Command.Gid++;
                        await Denied(async () => await client.PlanAsync(changed, Headers("Plan", request), Until(), lifetime.Token), StatusCode.PermissionDenied);
                        var badMode = request.Clone(); badMode.Command.Uid = uint.MaxValue;
                        await Denied(async () => await client.PlanAsync(badMode, Headers("Plan", badMode), Until(), lifetime.Token), StatusCode.InvalidArgument);
                        var badGid = request.Clone(); badGid.Command.Gid = uint.MaxValue;
                        await Denied(async () => await client.PlanAsync(badGid, Headers("Plan", badGid), Until(), lifetime.Token), StatusCode.InvalidArgument);
                        var missingUid = request.Clone(); missingUid.Command.ClearUid();
                        await Denied(async () => await client.PlanAsync(missingUid, Headers("Plan", missingUid), Until(), lifetime.Token), StatusCode.InvalidArgument);
                        var missingGid = request.Clone(); missingGid.Command.ClearGid();
                        await Denied(async () => await client.PlanAsync(missingGid, Headers("Plan", missingGid), Until(), lifetime.Token), StatusCode.InvalidArgument);
                        var protectedTarget = request.Clone(); protectedTarget.Command.Location.ContainerId = core;
                        await Denied(async () => await client.PlanAsync(protectedTarget, Headers("Plan", protectedTarget), Until(), lifetime.Token), StatusCode.PermissionDenied);
                        var readOnly = request.Clone(); readOnly.Command.Location.Path = "tmp/recursive-readonly";
                        await Denied(async () => await client.PlanAsync(readOnly, Headers("Plan", readOnly), Until(), lifetime.Token), StatusCode.PermissionDenied);
                    }
                    var authority = Headers("Plan", request); var plan = await client.PlanAsync(request, authority, Until(), lifetime.Token);
                    if (boundaries) await Denied(async () => await client.PlanAsync(request, authority, Until(), lifetime.Token), StatusCode.PermissionDenied);
                    Require(plan.OwnerUid == 1000 && plan.OwnerGid == 1000 && plan.EntryCount == 4 && plan.FileCount == 2 && plan.DirectoryCount == 2 && plan.Command.Equals(request.Command));
                    await Emit(new { kind = "plan", plan = formatter.Format(plan), boundaryRefusals = boundaries ? 11 : 0 }); continue;
                }
                Require(action is "apply" or "inspect" or "discard" or "reject"); Require(input.EnumerateObject().Count() == 3);
                var operationId = input.GetProperty("operationId").GetString()!;
                Require(Guid.TryParseExact(operationId, "N", out var parsed) && parsed != Guid.Empty && parsed.ToString("N") == operationId);
                var supplied = JsonParser.Default.Parse<ContainerRecursiveOwnershipPlan>(input.GetProperty("plan").GetRawText());
                Require(supplied.Command.Location.ContainerId == id && supplied.Command.Location.InstanceId == instance && supplied.Command.Location.Path == "work/recursive-tree");
                if (!operations.TryGetValue(operationId, out var operation))
                {
                    operation = new() { OperationId = operationId, Owner = new('B', 64), Plan = supplied,
                        Context = new() { ActorSubject = "installed-recursive-protocol-probe", TraceId = "owned-recursive-validation", JobId = operationId, IdempotencyKey = Guid.NewGuid().ToString("N") } };
                    operations.Add(operationId, operation);
                }
                Require(operation.Plan.Equals(supplied));
                if (action == "reject")
                {
                    var changed = operation.Clone(); changed.Plan.ManifestDigest = new('F', 64);
                    await Denied(async () => await client.ApplyAsync(changed, Headers("Apply", operation), Until(), lifetime.Token), StatusCode.PermissionDenied);
                    var changedUid = operation.Clone(); changedUid.Plan.Command.Uid++;
                    await Denied(async () => await client.ApplyAsync(changedUid, Headers("Apply", operation), Until(), lifetime.Token), StatusCode.PermissionDenied);
                    var changedGid = operation.Clone(); changedGid.Plan.Command.Gid++;
                    await Denied(async () => await client.ApplyAsync(changedGid, Headers("Apply", operation), Until(), lifetime.Token), StatusCode.PermissionDenied);
                    await Denied(async () => await client.ApplyAsync(operation, Headers("Inspect", operation), Until(), lifetime.Token), StatusCode.PermissionDenied);
                    await Denied(async () => await client.InspectAsync(operation, Headers("Inspect", operation), Until(), lifetime.Token), StatusCode.NotFound);
                    await Emit(new { kind = "rejected", operationId, refusals = 5 }); continue;
                }
                var result = action switch
                {
                    "apply" => await client.ApplyAsync(operation, Headers("Apply", operation), Until(), lifetime.Token),
                    "inspect" => await client.InspectAsync(operation, Headers("Inspect", operation), Until(), lifetime.Token),
                    _ => await client.DiscardAsync(operation, Headers("Discard", operation), Until(), lifetime.Token)
                };
                Require(result.OperationId == operationId && result.Fingerprint == supplied.Fingerprint && result.TargetProof == supplied.TargetProof);
                await Emit(new { kind = "receipt", operationId, result = formatter.Format(result) });
            }
            throw new InvalidOperationException("Recursive probe command budget exceeded");
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
    private static void Require(bool condition) { if (!condition) throw new InvalidOperationException("Recursive probe invariant failed"); }
    private static async Task Denied(Func<Task> action, StatusCode expected)
    {
        try { await action(); } catch (RpcException error) when (error.StatusCode == expected) { return; }
        throw new InvalidOperationException("Recursive probe expected refusal");
    }
}
