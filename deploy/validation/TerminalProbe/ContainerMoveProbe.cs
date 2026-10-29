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

/// <summary>Installed signed move UDS/helper only; no business/BFF/UI claim.</summary>
internal static class ContainerMoveProbe
{
    public static string Phase { get; private set; } = "startup";
    public static async Task RunAsync()
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(300));
        var text = await Console.In.ReadToEndAsync(lifetime.Token); Require(text.Length is > 0 and <= 8192);
        using var document = JsonDocument.Parse(text); var input = document.RootElement;
        Require(input.EnumerateObject().Count() == 6);
        var revision = input.GetProperty("revision").GetString()!; var id = input.GetProperty("containerId").GetString()!;
        var instance = input.GetProperty("instanceId").GetString()!; var core = input.GetProperty("coreId").GetString()!;
        var sourceVersion = input.GetProperty("sourceVersion").GetString()!; var treeVersion = input.GetProperty("treeSourceVersion").GetString()!;
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
            var client = new ContainerMoveService.ContainerMoveServiceClient(channel);
            DateTime Until() => DateTime.UtcNow.AddSeconds(100);
            Metadata Headers(string action, IMessage request, bool wrongDomain = false)
            {
                var hash = ContainerMoveBinding.Hash(request); var expires = DateTimeOffset.UtcNow.AddSeconds(45).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
                var nonce = Guid.NewGuid().ToString("N"); var method = ContainerMoveBinding.Service + action;
                var payload = wrongDomain ? ContainerDirectoryBinding.Payload(expires, nonce, method, hash) : ContainerMoveBinding.Payload(expires, nonce, method, hash);
                return new() { { "x-whaledeck-capability", expires + "." + nonce + "." + Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload))) },
                    { ContainerMoveBinding.Header, hash } };
            }
            ContainerMovePlanRequest Planning(string target, bool tree = false, string container = "") => new()
            {
                Command = new() { Source = new() { ContainerId = container.Length == 0 ? id : container, InstanceId = instance, Path = tree ? "work/source-tree" : "work/move-source.bin" },
                    TargetPath = target, ExpectedVersion = tree ? treeVersion : sourceVersion },
                Context = new() { ActorSubject = "installed-move-protocol-probe", TraceId = "owned-move-validation" }
            };
            ContainerMoveRequest Operation(ContainerMovePlan plan)
            {
                var operation = Guid.NewGuid().ToString("N");
                return new() { OperationId = operation, Owner = new('B', 64), Plan = plan,
                    Context = new() { ActorSubject = "installed-move-protocol-probe", TraceId = "owned-move-validation", JobId = operation, IdempotencyKey = Guid.NewGuid().ToString("N") } };
            }
            Phase = "capabilities";
            var caps = await new AgentService.AgentServiceClient(channel).GetCapabilitiesAsync(new(), deadline: Until(), cancellationToken: lifetime.Token);
            Require(caps.ProtocolMajor == 1 && caps.ProtocolMinor >= 46 && caps.Capabilities.Contains("docker.files.move-intents"));
            Phase = "bound-plan-and-permission-rejections";
            var planned = Planning("work/移动文件.bin");
            await Denied(async () => await client.PlanAsync(planned, deadline: Until(), cancellationToken: lifetime.Token), StatusCode.PermissionDenied);
            await Denied(async () => await client.PlanAsync(planned, Headers("Plan", planned, true), Until(), lifetime.Token), StatusCode.PermissionDenied);
            var modified = planned.Clone(); modified.Context.TraceId = "changed-body";
            await Denied(async () => await client.PlanAsync(modified, Headers("Plan", planned), Until(), lifetime.Token), StatusCode.PermissionDenied);
            var corePlan = Planning("work/no-core-write", container: core);
            await Denied(async () => await client.PlanAsync(corePlan, Headers("Plan", corePlan), Until(), lifetime.Token), StatusCode.PermissionDenied);
            var readOnly = Planning("tmp/no-readonly-write");
            await Denied(async () => await client.PlanAsync(readOnly, Headers("Plan", readOnly), Until(), lifetime.Token), StatusCode.PermissionDenied);
            var protectedPath = Planning("work/.env");
            await Denied(async () => await client.PlanAsync(protectedPath, Headers("Plan", protectedPath), Until(), lifetime.Token), StatusCode.PermissionDenied);
            Phase = "permanent-discard-before-apply";
            var cancelPlan = Planning("work/取消移动.bin"); var cancel = Operation(await client.PlanAsync(cancelPlan, Headers("Plan", cancelPlan), Until(), lifetime.Token));
            var canceled = await client.DiscardAsync(cancel, Headers("Discard", cancel), Until(), lifetime.Token);
            var late = await client.ApplyAsync(cancel, Headers("Apply", cancel), Until(), lifetime.Token);
            Require(canceled.HasDestinationPublished && !canceled.DestinationPublished && canceled.HasSourceRemoved && !canceled.SourceRemoved && canceled.HasSourceRetained && !canceled.SourceRetained);
            Require(canceled.State == "Aborted" && !canceled.BindingCurrent && canceled.Version.Length == 0);
            Require(late.State == "Aborted" && !late.BindingCurrent && late.Version.Length == 0);
            var completed = new List<object>();
            foreach (var tree in new[] { false, true })
            {
                Phase = tree ? "directory-plan-apply-inspect" : "file-plan-apply-inspect";
                var planning = tree ? Planning("work/移动目录", true) : planned; var authority = Headers("Plan", planning);
                var plan = await client.PlanAsync(planning, authority, Until(), lifetime.Token);
                await Denied(async () => await client.PlanAsync(planning, authority, Until(), lifetime.Token), StatusCode.PermissionDenied);
                Require(plan.SchemaVersion == 1 && plan.OperationKind == "Move" && plan.Command.Equals(planning.Command) && plan.OwnerUid == 1000 && plan.OwnerGid == 1000 && plan.TargetParent.Inode > 0 && plan.Kind == (tree ? "Directory" : "File"));
                var operation = Operation(plan); var changed = operation.Clone(); changed.Context.TraceId = "tampered";
                await Denied(async () => await client.ApplyAsync(changed, Headers("Apply", operation), Until(), lifetime.Token), StatusCode.PermissionDenied);
                var wrongOwner = operation.Clone(); wrongOwner.Plan.OwnerUid++;
                await Denied(async () => await client.ApplyAsync(wrongOwner, Headers("Apply", wrongOwner), Until(), lifetime.Token), StatusCode.InvalidArgument);
                var result = await client.ApplyAsync(operation, Headers("Apply", operation), Until(), lifetime.Token);
                Require(result.HasDestinationPublished && result.DestinationPublished && result.HasSourceRemoved && result.SourceRemoved && result.HasSourceRetained && result.SourceRetained);
                Require(result.OperationKind == "Move" && result.State == "Completed" && result.BindingCurrent && !result.PrivateMayExist && !result.Replayed && result.OperationId == operation.OperationId && result.Fingerprint == plan.Fingerprint && result.TargetProof == plan.TargetProof);
                var inspected = await client.InspectAsync(operation, Headers("Inspect", operation), Until(), lifetime.Token);
                var replayed = await client.ApplyAsync(operation, Headers("Apply", operation), Until(), lifetime.Token);
                Require(inspected.State == "Completed" && inspected.BindingCurrent && inspected.Replayed && inspected.Version == result.Version);
                Require(replayed.State == "Completed" && replayed.Replayed && replayed.Version == result.Version);
                completed.Add(new { kind = plan.Kind, operationId = operation.OperationId, version = result.Version, fingerprint = plan.Fingerprint });
            }
            Phase = "done";
            Console.WriteLine(JsonSerializer.Serialize(new { containerMoveProbe = "passed", revision, containerId = id, instanceId = instance,
                completed, canceledOperation = cancel.OperationId, coreReadonlyProtectedRejected = true, bodyBindingAndReplayRejected = true, forgedOwnerRejectedByRoot = true }));
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
    private static void Require(bool condition) { if (!condition) throw new InvalidOperationException("Move probe invariant failed"); }
    private static async Task Denied(Func<Task> action, StatusCode expected)
    {
        try { await action(); }
        catch (RpcException error) when (error.StatusCode == expected) { return; }
        throw new InvalidOperationException("Move probe expected rejection missing");
    }
}
