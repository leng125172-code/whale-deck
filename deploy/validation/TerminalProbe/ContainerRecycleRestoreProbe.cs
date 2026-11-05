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

/// <summary>Parent-paced, fixed-resource probe. Each reply must be independently checked before the next mutation.</summary>
internal static class ContainerRecycleRestoreProbe
{
    public static string Phase { get; private set; } = "startup";
    public static string FailureCode { get; private set; } = "";
    public static object? CapabilityObservation { get; private set; }
    public static async Task RunAsync()
    {
        Require(OperatingSystem.IsLinux());
        using var lifetime = new CancellationTokenSource(TimeSpan.FromMinutes(12));
        var line = await Console.In.ReadLineAsync(lifetime.Token);
        Require(line is { Length: > 0 } && Encoding.UTF8.GetByteCount(line) <= 8192);
        using var header = JsonDocument.Parse(line!); var input = header.RootElement;
        Fields(input, "revision", "probeRevision", "containerId", "instanceId", "coreId");
        var revision = Text(input, "revision"); var probeRevision = Text(input, "probeRevision");
        var id = Text(input, "containerId"); var instance = Text(input, "instanceId"); var core = Text(input, "coreId");
        Require(Lower(revision, 40) && Lower(probeRevision, 40) && Lower(id, 64) && Lower(core, 64) && id != core && instance.Length is > 20 and < 64);
        Require(Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.EndsWith("+" + probeRevision, StringComparison.Ordinal));
        using var handler = new SocketsHttpHandler { UseProxy = false, ConnectCallback = async (_, token) =>
        {
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try { await socket.ConnectAsync(new UnixDomainSocketEndPoint("/run/whaledeck/agent.sock"), token); return new NetworkStream(socket, true); }
            catch { socket.Dispose(); throw; }
        }};
        using var channel = GrpcChannel.ForAddress("http://localhost", new() { HttpHandler = handler });
        Phase = "capabilities";
        var caps = await new AgentService.AgentServiceClient(channel).GetCapabilitiesAsync(new(), deadline: DateTime.UtcNow.AddSeconds(10), cancellationToken: lifetime.Token);
        CapabilityObservation = new { protocolMajor = caps.ProtocolMajor, protocolMinor = caps.ProtocolMinor,
            sourceCapability = caps.Capabilities.Contains("docker.files.recycle"), restoreCapability = caps.Capabilities.Contains("docker.files.recycle.restore") };
        // An older installation must refuse before reading the signing key or sending any operation.
        if (!(caps.ProtocolMajor == 1 && caps.ProtocolMinor >= 57 && caps.Capabilities.Contains("docker.files.recycle.restore") && caps.Capabilities.Contains("docker.files.recycle")))
        {
            FailureCode = "CONTAINER_RECYCLE_RESTORE_CAPABILITY_UNAVAILABLE";
            throw new InvalidOperationException();
        }
        var key = await File.ReadAllBytesAsync("/etc/whaledeck/agent-capability.key", lifetime.Token);
        try
        {
            Require(key.Length == 32);
            var sourceClient = new ContainerRecycleService.ContainerRecycleServiceClient(channel);
            var client = new ContainerRecycleRestoreService.ContainerRecycleRestoreServiceClient(channel);
            var formatter = new JsonFormatter(new JsonFormatter.Settings(formatDefaultValues: true));
            var sourcePlans = new Dictionary<string, ContainerRecyclePlan>(StringComparer.Ordinal);
            var sources = new Dictionary<string, ContainerRecycleRequest>(StringComparer.Ordinal);
            var plans = new Dictionary<string, ContainerRecycleRestorePlan>(StringComparer.Ordinal);
            var operations = new Dictionary<string, ContainerRecycleRestoreRequest>(StringComparer.Ordinal);
            DateTime Until() => DateTime.UtcNow.AddSeconds(120);
            RequestContext Context() => new() { ActorSubject = "installed-recycle-restore-probe", TraceId = "owned-recycle-restore-validation" };
            Metadata Headers(string action, IMessage request, bool source = false, string variant = "")
            {
                var hash = source ? ContainerRecycleBinding.Hash(request) : ContainerRecycleRestoreBinding.Hash(request);
                var method = (source ? ContainerRecycleBinding.Service : ContainerRecycleRestoreBinding.Service) + action;
                var expires = DateTimeOffset.UtcNow.AddSeconds(variant == "expired" ? -60 : variant == "future" ? 180 : 45).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
                var nonce = Guid.NewGuid().ToString("N");
                var payload = variant switch {
                    "recycle-domain" => ContainerRecycleBinding.Payload(expires, nonce, method, hash),
                    "permission-domain" => ContainerPermissionBinding.Payload(expires, nonce, method, hash),
                    "generic-domain" => $"{expires}.{nonce}.{method}",
                    _ => source ? ContainerRecycleBinding.Payload(expires, nonce, method, hash) : ContainerRecycleRestoreBinding.Payload(expires, nonce, method, hash) };
                return new() { { "x-whaledeck-capability", expires + "." + nonce + "." + Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload))) },
                    { source ? ContainerRecycleBinding.Header : ContainerRecycleRestoreBinding.Header, hash } };
            }
            async Task Emit(object value) { await Console.Out.WriteLineAsync(JsonSerializer.Serialize(value)); await Console.Out.FlushAsync(); }
            await Emit(new { kind = "ready", revision, probeRevision, protocolMinor = caps.ProtocolMinor });
            for (var count = 0; count < 64; count++)
            {
                line = await Console.In.ReadLineAsync(lifetime.Token);
                Require(line is { Length: > 0 } && Encoding.UTF8.GetByteCount(line) <= 65536);
                using var packet = JsonDocument.Parse(line!); input = packet.RootElement; var action = Text(input, "action"); Phase = action;
                if (action == "quit") { Fields(input, "action"); await Emit(new { kind = "done" }); return; }
                var refusals = 0;
                async Task Reject(Func<Task> call, StatusCode expected)
                { Phase = action + "-refusal-" + (refusals + 1).ToString(CultureInfo.InvariantCulture); await Denied(call, expected); refusals++; }
                if (action == "source-plan")
                {
                    Fields(input, "action", "version"); Require(Upper(Text(input, "version")));
                    var request = new ContainerRecyclePlanRequest { Command = new() { Location = new() { ContainerId = id, InstanceId = instance, Path = "work/recycle-rpc.bin" }, ExpectedVersion = Text(input, "version") }, Context = Context() };
                    var plan = await sourceClient.PlanAsync(request, Headers("Plan", request, source: true), Until(), lifetime.Token);
                    ContainerRecycleRestoreWire.RequireKnown(plan);
                    Require(plan.Command.Equals(request.Command) && plan.SchemaVersion == 1 && plan.OwnerUid == 1000 && plan.OwnerGid == 1000 && Upper(plan.Fingerprint) && Upper(plan.Sha256));
                    sourcePlans[plan.Fingerprint] = plan.Clone();
                    await Emit(new { kind = "source-plan", plan = formatter.Format(plan) }); continue;
                }
                if (action is "source-apply" or "source-inspect")
                {
                    Fields(input, "action", "operationId", "plan"); var sourceId = Text(input, "operationId"); Identity(sourceId);
                    var supplied = JsonParser.Default.Parse<ContainerRecyclePlan>(Text(input, "plan"));
                    Require(sourcePlans.TryGetValue(supplied.Fingerprint, out var approved) && supplied.Equals(approved));
                    if (!sources.TryGetValue(sourceId, out var request))
                    {
                        Require(action == "source-apply"); var context = Context(); context.JobId = sourceId; context.IdempotencyKey = Guid.NewGuid().ToString("N");
                        request = new() { OperationId = sourceId, Owner = new('B', 64), Plan = supplied.Clone(), Context = context }; sources.Add(sourceId, request);
                    }
                    Require(request.Plan.Equals(supplied));
                    var result = action == "source-apply" ? await sourceClient.ApplyAsync(request, Headers("Apply", request, source: true), Until(), lifetime.Token) :
                        await sourceClient.InspectAsync(request, Headers("Inspect", request, source: true), Until(), lifetime.Token);
                    ContainerRecycleRestoreWire.RequireKnown(result);
                    Require(result.OperationId == sourceId && result.ContainerId == id && result.InstanceId == instance && result.Fingerprint == supplied.Fingerprint && result.TargetProof == supplied.TargetProof);
                    Require(result.State is "Trashed" or "Restored" or "Aborted" or "Unknown");
                    await Emit(new { kind = "source-receipt", action, operationId = sourceId, jobId = request.Context.JobId, result = formatter.Format(result) }); continue;
                }
                if (action == "plan")
                {
                    Fields(input, "action", "sourceOperationId", "boundaries");
                    Require(sources.TryGetValue(Text(input, "sourceOperationId"), out var original));
                    var observed = await sourceClient.InspectAsync(original, Headers("Inspect", original!, source: true), Until(), lifetime.Token);
                    Require(observed.OperationId == original!.OperationId && observed.Fingerprint == original.Plan.Fingerprint && observed.TargetProof == original.Plan.TargetProof && observed.ContainerId == id && observed.InstanceId == instance);
                    Require(observed.State == "Trashed" && observed.BindingCurrent && observed.HistoryVerified && observed.HistoryAvailable && observed.HasSourceRetained && observed.SourceRetained && observed.HasSourceRestored && !observed.SourceRestored && Upper(observed.HistoryDigest));
                    var request = new ContainerRecycleRestorePlanRequest { Command = new() { Source = new() { OperationId = original.OperationId, Owner = original.Owner, Plan = original.Plan.Clone() }, HistoryDigest = observed.HistoryDigest }, Context = Context() };
                    if (input.GetProperty("boundaries").GetBoolean())
                    {
                        await Reject(async () => await client.PlanAsync(request, deadline: Until(), cancellationToken: lifetime.Token), StatusCode.PermissionDenied);
                        foreach (var variant in new[] { "recycle-domain", "permission-domain", "generic-domain", "expired", "future" })
                            await Reject(async () => await client.PlanAsync(request, Headers("Plan", request, variant: variant), Until(), lifetime.Token), StatusCode.PermissionDenied);
                        await Reject(async () => await client.PlanAsync(request, Headers("Apply", request), Until(), lifetime.Token), StatusCode.PermissionDenied);
                        foreach (var field in new[] { "source", "owner", "history", "path", "bytes", "actor", "trace" })
                        {
                            var changed = request.Clone();
                            switch (field) { case "source": changed.Command.Source.OperationId = Guid.NewGuid().ToString("N"); break; case "owner": changed.Command.Source.Owner = new('E', 64); break;
                                case "history": changed.Command.HistoryDigest = new('E', 64); break; case "path": changed.Command.Source.Plan.Command.Location.Path = "work/other.bin"; break;
                                case "bytes": changed.Command.Source.Plan.Sha256 = new('E', 64); break; case "actor": changed.Context.ActorSubject += "-changed"; break; default: changed.Context.TraceId += "-changed"; break; }
                            await Reject(async () => await client.PlanAsync(changed, Headers("Plan", request), Until(), lifetime.Token), StatusCode.PermissionDenied);
                        }
                        var bad = request.Clone(); bad.Command.HistoryDigest = "";
                        await Reject(async () => await client.PlanAsync(bad, Headers("Plan", bad), Until(), lifetime.Token), StatusCode.InvalidArgument);
                    }
                    Phase = "plan-value"; var authority = Headers("Plan", request);
                    var plan = await client.PlanAsync(request, authority, Until(), lifetime.Token);
                    ContainerRecycleRestoreWire.RequireKnown(plan);
                    Require(plan.Command.Equals(request.Command) && plan.SchemaVersion == 1 && plan.OwnerUid == 1000 && plan.OwnerGid == 1000 && plan.TargetProof == original.Plan.TargetProof && plan.ParentIdentity.Equals(original.Plan.ParentIdentity));
                    Require(plan.RetainedIdentity.Equals(original.Plan.SourceIdentity) && plan.RetainedChangedAtUnixNs >= original.Plan.SourceChangedAtUnixNs && plan.PrivateIdentity is { Inode: > 0 } && plan.Fingerprint == ContainerRecycleRestoreWire.Fingerprint(plan));
                    plans[plan.Fingerprint] = plan.Clone();
                    if (input.GetProperty("boundaries").GetBoolean()) await Reject(async () => await client.PlanAsync(request, authority, Until(), lifetime.Token), StatusCode.PermissionDenied);
                    await Emit(new { kind = "plan", plan = formatter.Format(plan), boundaryRefusals = refusals }); continue;
                }
                Require(action is "apply" or "inspect" or "discard" or "reject"); Fields(input, "action", "operationId", "plan");
                var operationId = Text(input, "operationId"); Identity(operationId);
                var restorePlan = JsonParser.Default.Parse<ContainerRecycleRestorePlan>(Text(input, "plan"));
                Require(plans.TryGetValue(restorePlan.Fingerprint, out var approvedRestore) && restorePlan.Equals(approvedRestore) && operationId != restorePlan.Command.Source.OperationId);
                if (!operations.TryGetValue(operationId, out var operation))
                {
                    Require(action is "apply" or "discard" or "reject"); var context = Context(); context.JobId = operationId; context.IdempotencyKey = Guid.NewGuid().ToString("N");
                    operation = new() { OperationId = operationId, Owner = new('C', 64), Plan = restorePlan.Clone(), Context = context }; operations.Add(operationId, operation);
                }
                Require(operation.Plan.Equals(restorePlan));
                if (action == "reject")
                {
                    foreach (var field in new[] { "source", "history", "private", "ctime", "fingerprint", "owner", "actor", "trace", "key" })
                    {
                        var changed = operation.Clone();
                        switch (field) { case "source": changed.Plan.Command.Source.Owner = new('E', 64); break; case "history": changed.Plan.Command.HistoryDigest = new('E', 64); break;
                            case "private": changed.Plan.PrivateIdentity.Inode++; break; case "ctime": changed.Plan.RetainedChangedAtUnixNs++; break; case "fingerprint": changed.Plan.ParentVersion = new('E', 64); break;
                            case "owner": changed.Owner = new('E', 64); break; case "actor": changed.Context.ActorSubject += "-changed"; break; case "trace": changed.Context.TraceId += "-changed"; break; default: changed.Context.IdempotencyKey = Guid.NewGuid().ToString("N"); break; }
                        // Keep the body structurally valid so this tests signed
                        // binding, rather than failing the earlier plan parser.
                        changed.Plan.Fingerprint = ContainerRecycleRestoreWire.Fingerprint(changed.Plan);
                        await Reject(async () => await client.ApplyAsync(changed, Headers("Apply", operation), Until(), lifetime.Token), StatusCode.PermissionDenied);
                    }
                    foreach (var variant in new[] { "recycle-domain", "permission-domain", "generic-domain" })
                        await Reject(async () => await client.ApplyAsync(operation, Headers("Apply", operation, variant: variant), Until(), lifetime.Token), StatusCode.PermissionDenied);
                    await Reject(async () => await client.ApplyAsync(operation, Headers("Discard", operation), Until(), lifetime.Token), StatusCode.PermissionDenied);
                    var bad = operation.Clone(); bad.OperationId = bad.Context.JobId = bad.Plan.Command.Source.OperationId;
                    await Reject(async () => await client.ApplyAsync(bad, Headers("Apply", bad), Until(), lifetime.Token), StatusCode.InvalidArgument);
                    bad = operation.Clone(); bad.Context.JobId = bad.Plan.Command.Source.OperationId;
                    await Reject(async () => await client.ApplyAsync(bad, Headers("Apply", bad), Until(), lifetime.Token), StatusCode.InvalidArgument);
                    await Reject(async () => await client.InspectAsync(operation, Headers("Inspect", operation), Until(), lifetime.Token), StatusCode.NotFound);
                    await Emit(new { kind = "rejected", operationId, refusals }); continue;
                }
                Phase = action + "-value";
                var receipt = action switch {
                    "apply" => await client.ApplyAsync(operation, Headers("Apply", operation), Until(), lifetime.Token),
                    "inspect" => await client.InspectAsync(operation, Headers("Inspect", operation), Until(), lifetime.Token),
                    _ => await client.DiscardAsync(operation, Headers("Discard", operation), Until(), lifetime.Token) };
                ContainerRecycleRestoreWire.Receipt(operation, receipt);
                await Emit(new { kind = "receipt", action, operationId, jobId = operation.Context.JobId, result = formatter.Format(receipt) });
            }
            throw new InvalidOperationException();
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
    private static void Fields(JsonElement value, params string[] names) { Require(value.ValueKind == JsonValueKind.Object); var fields = value.EnumerateObject().Select(p => p.Name).ToArray(); Require(fields.Length == names.Length && fields.ToHashSet(StringComparer.Ordinal).SetEquals(names)); }
    private static string Text(JsonElement value, string name) => value.GetProperty(name).GetString() ?? throw new InvalidOperationException();
    private static bool Lower(string value, int length) => value.Length == length && value.All(char.IsAsciiHexDigitLower);
    private static bool Upper(string value) => value.Length == 64 && value.All(c => char.IsAsciiDigit(c) || c is >= 'A' and <= 'F');
    private static void Identity(string value) => Require(Lower(value, 32) && Guid.ParseExact(value, "N") != Guid.Empty);
    private static void Require(bool condition) { if (!condition) throw new InvalidOperationException(); }
    private static async Task Denied(Func<Task> call, StatusCode expected) { try { await call(); } catch (RpcException e) when (e.StatusCode == expected) { return; } throw new InvalidOperationException(); }
}
