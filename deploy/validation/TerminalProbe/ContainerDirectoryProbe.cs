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

/// <summary>Installed signed UDS/Root protocol only, no human BFF or UI claim.</summary>
internal static class ContainerDirectoryProbe
{
    public static string Phase { get; private set; } = "startup";
    public static async Task RunAsync()
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(150));
        var text = await Console.In.ReadToEndAsync(lifetime.Token); Require(text.Length is > 0 and <= 4096);
        using var document = JsonDocument.Parse(text); var input = document.RootElement;
        Require(input.EnumerateObject().Count() == 4);
        var revision = input.GetProperty("revision").GetString()!; var id = input.GetProperty("containerId").GetString()!;
        var instance = input.GetProperty("instanceId").GetString()!; var core = input.GetProperty("coreId").GetString()!;
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
            var client = new ContainerDirectoryService.ContainerDirectoryServiceClient(channel);
            DateTime Until() => DateTime.UtcNow.AddSeconds(35);
            Metadata Headers(string action, IMessage request, bool wrongDomain = false)
            {
                var hash = ContainerDirectoryBinding.Hash(request); var expires = DateTimeOffset.UtcNow.AddSeconds(45).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
                var nonce = Guid.NewGuid().ToString("N"); var method = ContainerDirectoryBinding.Service + action;
                var payload = wrongDomain ? ContainerFileWriteBinding.Payload(expires, nonce, method, hash) : ContainerDirectoryBinding.Payload(expires, nonce, method, hash);
                return new() { { "x-whaledeck-capability", expires + "." + nonce + "." + Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload))) },
                    { ContainerDirectoryBinding.Header, hash } };
            }
            ContainerDirectoryPlanRequest Planning(string path, string container = "") => new()
            {
                Command = new() { Location = new() { ContainerId = container.Length == 0 ? id : container, InstanceId = instance, Path = path }, Mode = "0750" },
                Context = new() { ActorSubject = "installed-directory-protocol-probe", TraceId = "owned-directory-validation" }
            };
            ContainerDirectoryRequest Operation(ContainerDirectoryPlan plan)
            {
                var operation = Guid.NewGuid().ToString("N");
                return new() { OperationId = operation, Owner = new('B', 64), Plan = plan,
                    Context = new() { ActorSubject = "installed-directory-protocol-probe", TraceId = "owned-directory-validation", JobId = operation, IdempotencyKey = Guid.NewGuid().ToString("N") } };
            }
            Phase = "capabilities";
            var caps = await new AgentService.AgentServiceClient(channel).GetCapabilitiesAsync(new(), deadline: Until(), cancellationToken: lifetime.Token);
            Require(caps.ProtocolMajor == 1 && caps.ProtocolMinor >= 44 && caps.Capabilities.Contains("docker.files.directory-intents"));
            Phase = "bound-plan-and-permission-rejections";
            var planned = Planning("work/新建目录");
            await Denied(async () => await client.PlanAsync(planned, deadline: Until(), cancellationToken: lifetime.Token), StatusCode.PermissionDenied);
            await Denied(async () => await client.PlanAsync(planned, Headers("Plan", planned, true), Until(), lifetime.Token), StatusCode.PermissionDenied);
            var modified = planned.Clone(); modified.Command.Mode = "0777";
            await Denied(async () => await client.PlanAsync(modified, Headers("Plan", planned), Until(), lifetime.Token), StatusCode.PermissionDenied);
            var corePlan = Planning("work/no-core-write", core);
            await Denied(async () => await client.PlanAsync(corePlan, Headers("Plan", corePlan), Until(), lifetime.Token), StatusCode.PermissionDenied);
            var readonlyPlan = Planning("tmp/no-readonly-write");
            await Denied(async () => await client.PlanAsync(readonlyPlan, Headers("Plan", readonlyPlan), Until(), lifetime.Token), StatusCode.PermissionDenied);
            var badMode = planned.Clone(); badMode.Command.Mode = "4755";
            await Denied(async () => await client.PlanAsync(badMode, Headers("Plan", badMode), Until(), lifetime.Token), StatusCode.InvalidArgument);
            var planAuthority = Headers("Plan", planned);
            var plan = await client.PlanAsync(planned, planAuthority, Until(), lifetime.Token);
            await Denied(async () => await client.PlanAsync(planned, planAuthority, Until(), lifetime.Token), StatusCode.PermissionDenied);
            Require(plan.SchemaVersion == 1 && plan.Command.Equals(planned.Command) && plan.OwnerUid == 1000 && plan.OwnerGid == 1000 && plan.ParentIdentity.Inode > 0);
            Phase = "apply-original-plan-and-inspect";
            var operation = Operation(plan); var changed = operation.Clone(); changed.Plan.OwnerUid = 0;
            await Denied(async () => await client.ApplyAsync(changed, Headers("Apply", operation), Until(), lifetime.Token), StatusCode.PermissionDenied);
            var result = await client.ApplyAsync(operation, Headers("Apply", operation), Until(), lifetime.Token);
            Require(result.State == "Completed" && result.BindingCurrent && !result.Replayed && result.OperationId == operation.OperationId && result.Fingerprint == plan.Fingerprint && result.TargetProof == plan.TargetProof);
            var inspected = await client.InspectAsync(operation, Headers("Inspect", operation), Until(), lifetime.Token);
            Require(inspected.State == "Completed" && inspected.BindingCurrent && inspected.Replayed && inspected.Version == result.Version);
            var replayed = await client.ApplyAsync(operation, Headers("Apply", operation), Until(), lifetime.Token);
            Require(replayed.State == "Completed" && replayed.Replayed && replayed.Version == result.Version);
            Phase = "permanent-cancellation-before-apply";
            var cancelPlan = Planning("work/取消目录"); var cancel = Operation(await client.PlanAsync(cancelPlan, Headers("Plan", cancelPlan), Until(), lifetime.Token));
            var canceled = await client.DiscardAsync(cancel, Headers("Discard", cancel), Until(), lifetime.Token);
            Require(canceled.State == "Aborted" && !canceled.BindingCurrent && canceled.Version.Length == 0);
            var late = await client.ApplyAsync(cancel, Headers("Apply", cancel), Until(), lifetime.Token);
            Require(late.State == "Aborted" && !late.BindingCurrent && late.Version.Length == 0);
            Phase = "done";
            Console.WriteLine(JsonSerializer.Serialize(new { containerDirectoryProbe = "passed", revision, containerId = id, instanceId = instance,
                completedOperation = operation.OperationId, completedPlan = JsonFormatter.Default.Format(plan), version = result.Version,
                canceledOperation = cancel.OperationId, canceledPlan = JsonFormatter.Default.Format(cancel.Plan), coreAndReadonlyRejected = true, bodyBindingAndReplayRejected = true }));
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
    private static void Require(bool condition) { if (!condition) throw new InvalidOperationException("Directory probe invariant failed"); }
    private static async Task Denied(Func<Task> action, StatusCode expected)
    {
        try { await action(); }
        catch (RpcException error) when (error.StatusCode == expected) { return; }
        throw new InvalidOperationException("Directory probe expected rejection missing");
    }
}
