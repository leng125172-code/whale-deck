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

/// <summary>Opt-in installed restore probe; exact owned fixture only, no local mutation fallback.</summary>
internal static class ContainerRecursiveAclRestoreProbe
{
    public static string Phase { get; private set; } = "startup";
    public static async Task RunAsync()
    {
        Require(OperatingSystem.IsLinux());
        using var lifetime = new CancellationTokenSource(TimeSpan.FromMinutes(12));
        var text = await Console.In.ReadLineAsync(lifetime.Token); Require(text is { Length: > 0 and <= 8192 });
        using var header = JsonDocument.Parse(text!); var input = header.RootElement;
        Fields(input, "revision", "probeRevision", "containerId", "instanceId", "coreId");
        var revision = Text(input, "revision"); var probeRevision = Text(input, "probeRevision");
        var id = Text(input, "containerId"); var instance = Text(input, "instanceId"); var core = Text(input, "coreId");
        Require(Lower(revision, 40) && Lower(probeRevision, 40) && Lower(id, 64) && Lower(core, 64) && id != core && instance.Length is > 20 and < 64);
        Require(Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion.EndsWith("+" + probeRevision, StringComparison.Ordinal));
        using var handler = new SocketsHttpHandler { UseProxy = false, ConnectCallback = async (_, token) =>
        {
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try { await socket.ConnectAsync(new UnixDomainSocketEndPoint("/run/whaledeck/agent.sock"), token); return new NetworkStream(socket, true); }
            catch { socket.Dispose(); throw; }
        }};
        using var channel = GrpcChannel.ForAddress("http://localhost", new() { HttpHandler = handler });
        Phase = "capabilities";
        var caps = await new AgentService.AgentServiceClient(channel).GetCapabilitiesAsync(new(), deadline: DateTime.UtcNow.AddSeconds(10), cancellationToken: lifetime.Token);
        Require(caps.ProtocolMajor == 1 && caps.ProtocolMinor >= 55 && caps.Capabilities.Contains("docker.files.recursive-acl-restore-intents"));
        var key = await File.ReadAllBytesAsync("/etc/whaledeck/agent-capability.key", lifetime.Token);
        try
        {
            Require(key.Length == 32);
            var client = new ContainerRecursiveAclRestoreService.ContainerRecursiveAclRestoreServiceClient(channel);
            var formatter = new JsonFormatter(new JsonFormatter.Settings(formatDefaultValues: true));
            var operations = new Dictionary<string, ContainerRecursiveAclRestoreRequest>(StringComparer.Ordinal);
            DateTime Until() => DateTime.UtcNow.AddSeconds(330);
            RequestContext Context() => new() { ActorSubject = "installed-recursive-acl-restore-probe", TraceId = "owned-recursive-acl-restore-validation" };
            Metadata Headers(string action, IMessage request, string variant = "")
            {
                var hash = ContainerRecursiveAclRestoreBinding.Hash(request); var method = ContainerRecursiveAclRestoreBinding.Service + action;
                var expires = DateTimeOffset.UtcNow.AddSeconds(variant == "expired" ? -60 : variant == "future" ? 180 : 45).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
                var nonce = Guid.NewGuid().ToString("N");
                var payload = variant switch {
                    "acl-domain" => ContainerAclBinding.Payload(expires, nonce, method, hash),
                    "recursive-domain" => ContainerRecursiveAclBinding.Payload(expires, nonce, method, hash),
                    "history-domain" => ContainerRecursiveAclHistoryBinding.Payload(expires, nonce, method, hash),
                    "generic-domain" => $"{expires}.{nonce}.{method}",
                    _ => ContainerRecursiveAclRestoreBinding.Payload(expires, nonce, method, hash) };
                return new() { { "x-whaledeck-capability", expires + "." + nonce + "." + Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload))) }, { ContainerRecursiveAclRestoreBinding.Header, hash } };
            }
            async Task Emit(object value) { await Console.Out.WriteLineAsync(JsonSerializer.Serialize(value)); await Console.Out.FlushAsync(); }
            await Emit(new { kind = "ready", revision, probeRevision, protocolMinor = caps.ProtocolMinor });
            for (var count = 0; count < 64; count++)
            {
                text = await Console.In.ReadLineAsync(lifetime.Token); Require(text is { Length: > 0 and <= 65536 });
                using var line = JsonDocument.Parse(text!); input = line.RootElement; var action = Text(input, "action"); Phase = action;
                if (action == "quit") { Fields(input, "action"); await Emit(new { kind = "done" }); return; }
                var refusals = 0;
                async Task Reject(Func<Task> call, StatusCode status)
                { Phase = action + "-refusal-" + (refusals + 1).ToString(CultureInfo.InvariantCulture); await Denied(call, status); refusals++; }
                if (action == "plan")
                {
                    Fields(input, "action", "captureId", "historyDigest", "version", "boundaries");
                    Require(Lower(Text(input, "captureId"), 32) && Upper(Text(input, "historyDigest")) && Upper(Text(input, "version")));
                    var request = new ContainerRecursiveAclRestorePlanRequest { Command = new() {
                        Location = new() { ContainerId = id, InstanceId = instance, Path = "work/recursive-tree" },
                        CaptureId = Text(input, "captureId"), ExpectedHistoryDigest = Text(input, "historyDigest"), ExpectedVersion = Text(input, "version") }, Context = Context() };
                    if (input.GetProperty("boundaries").GetBoolean())
                    {
                        await Reject(async () => await client.PlanAsync(request, deadline: Until(), cancellationToken: lifetime.Token), StatusCode.PermissionDenied);
                        foreach (var variant in new[] { "acl-domain", "recursive-domain", "history-domain", "generic-domain", "expired", "future" })
                            await Reject(async () => await client.PlanAsync(request, Headers("Plan", request, variant), Until(), lifetime.Token), StatusCode.PermissionDenied);
                        await Reject(async () => await client.PlanAsync(request, Headers("Apply", request), Until(), lifetime.Token), StatusCode.PermissionDenied);
                        var changes = new List<ContainerRecursiveAclRestorePlanRequest>();
                        var changed = request.Clone(); changed.Command.CaptureId = Guid.NewGuid().ToString("N"); changes.Add(changed);
                        changed = request.Clone(); changed.Command.ExpectedHistoryDigest = new('F', 64); changes.Add(changed);
                        changed = request.Clone(); changed.Command.ExpectedVersion = new('F', 64); changes.Add(changed);
                        changed = request.Clone(); changed.Command.Location.Path = "work/original.bin"; changes.Add(changed);
                        changed = request.Clone(); changed.Context.ActorSubject += "-changed"; changes.Add(changed);
                        changed = request.Clone(); changed.Context.TraceId += "-changed"; changes.Add(changed);
                        foreach (var altered in changes)
                            await Reject(async () => await client.PlanAsync(altered, Headers("Plan", request), Until(), lifetime.Token), StatusCode.PermissionDenied);
                        changed = request.Clone(); changed.Command.ExpectedHistoryDigest = "";
                        await Reject(async () => await client.PlanAsync(changed, Headers("Plan", changed), Until(), lifetime.Token), StatusCode.InvalidArgument);
                        changed = request.Clone(); changed.Context.JobId = Guid.NewGuid().ToString("N");
                        await Reject(async () => await client.PlanAsync(changed, Headers("Plan", changed), Until(), lifetime.Token), StatusCode.InvalidArgument);
                        changed = request.Clone(); changed.Command.Location.ContainerId = core;
                        await Reject(async () => await client.PlanAsync(changed, Headers("Plan", changed), Until(), lifetime.Token), StatusCode.PermissionDenied);
                        // Source path matching precedes writable-parent checks. This is
                        // a source-scope rejection, not proof of the read-only write guard.
                        changed = request.Clone(); changed.Command.Location.Path = "tmp/recursive-readonly";
                        await Reject(async () => await client.PlanAsync(changed, Headers("Plan", changed), Until(), lifetime.Token), StatusCode.FailedPrecondition);
                        changed = request.Clone(); changed.Command.ExpectedVersion = new('0', 64);
                        await Reject(async () => await client.PlanAsync(changed, Headers("Plan", changed), Until(), lifetime.Token), StatusCode.Aborted);
                        changed = request.Clone(); changed.Command.ExpectedHistoryDigest = new('0', 64);
                        await Reject(async () => await client.PlanAsync(changed, Headers("Plan", changed), Until(), lifetime.Token), StatusCode.Aborted);
                        changed = request.Clone(); changed.Command.CaptureId = Guid.NewGuid().ToString("N");
                        await Reject(async () => await client.PlanAsync(changed, Headers("Plan", changed), Until(), lifetime.Token), StatusCode.FailedPrecondition);
                    }
                    var authority = Headers("Plan", request); Phase = "plan-value";
                    var plan = await client.PlanAsync(request, authority, Until(), lifetime.Token);
                    Require(plan.Command.Equals(request.Command) && plan.OwnerUid == 1000 && plan.OwnerGid == 1000 && plan.EntryCount == 4 && plan.FileCount == 2 && plan.DirectoryCount == 2);
                    Require(plan.RestoreSource is not null && plan.RestoreSource.CaptureId == request.Command.CaptureId && plan.RestoreSource.HistoryDigest == request.Command.ExpectedHistoryDigest);
                    if (input.GetProperty("boundaries").GetBoolean())
                        await Reject(async () => await client.PlanAsync(request, authority, Until(), lifetime.Token), StatusCode.PermissionDenied);
                    await Emit(new { kind = "plan", plan = formatter.Format(plan), boundaryRefusals = refusals }); continue;
                }
                Require(action is "apply" or "inspect" or "discard" or "reject"); Fields(input, "action", "operationId", "plan");
                var operationId = Text(input, "operationId"); Require(Lower(operationId, 32) && Guid.ParseExact(operationId, "N") != Guid.Empty);
                var supplied = JsonParser.Default.Parse<ContainerRecursiveAclRestorePlan>(input.GetProperty("plan").GetRawText());
                Require(supplied.Command.Location.ContainerId == id && supplied.Command.Location.InstanceId == instance && supplied.Command.Location.Path == "work/recursive-tree");
                if (!operations.TryGetValue(operationId, out var operation))
                {
                    var context = Context(); context.JobId = operationId; context.IdempotencyKey = Guid.NewGuid().ToString("N");
                    operation = new() { OperationId = operationId, Owner = new('B', 64), Plan = supplied, Context = context }; operations.Add(operationId, operation);
                }
                Require(operation.Plan.Equals(supplied));
                if (action == "reject")
                {
                    var changes = new List<ContainerRecursiveAclRestoreRequest>();
                    var changed = operation.Clone(); changed.Plan.ManifestDigest = new('F', 64); changes.Add(changed);
                    changed = operation.Clone(); changed.Plan.RestoreTargetsDigest = new('F', 64); changes.Add(changed);
                    changed = operation.Clone(); changed.Plan.RestoreSource.CreatedAtUnixMs++; changes.Add(changed);
                    changed = operation.Clone(); changed.Plan.Command.CaptureId = Guid.NewGuid().ToString("N"); changed.Plan.RestoreSource.CaptureId = changed.Plan.Command.CaptureId; changes.Add(changed);
                    changed = operation.Clone(); changed.Plan.Command.ExpectedHistoryDigest = new('F', 64); changed.Plan.RestoreSource.HistoryDigest = new('F', 64); changes.Add(changed);
                    changed = operation.Clone(); changed.Context.ActorSubject += "-changed"; changes.Add(changed);
                    changed = operation.Clone(); changed.Context.TraceId += "-changed"; changes.Add(changed);
                    // Mutate only fields whose altered value still satisfies policy,
                    // so these checks reach binding rather than preflight rejection.
                    foreach (var frame in new[] { "Device", "Inode", "SizeBytes", "ModifiedAtUnixNs", "ChangedAtUnixNs" })
                    {
                        changed = operation.Clone(); var field = changed.Plan.RestoreSourceFrame.GetType().GetProperty(frame)!;
                        var before = field.GetValue(changed.Plan.RestoreSourceFrame); object after = before is ulong u ? (object)(u + 1) : (long)before! + 1;
                        field.SetValue(changed.Plan.RestoreSourceFrame, after); changes.Add(changed);
                    }
                    foreach (var altered in changes)
                        await Reject(async () => await client.ApplyAsync(altered, Headers("Apply", operation), Until(), lifetime.Token), StatusCode.PermissionDenied);
                    foreach (var variant in new[] { "acl-domain", "recursive-domain", "history-domain", "generic-domain" })
                        await Reject(async () => await client.ApplyAsync(operation, Headers("Apply", operation, variant), Until(), lifetime.Token), StatusCode.PermissionDenied);
                    await Reject(async () => await client.ApplyAsync(operation, Headers("Inspect", operation), Until(), lifetime.Token), StatusCode.PermissionDenied);
                    changed = operation.Clone(); changed.Context.JobId = "";
                    await Reject(async () => await client.ApplyAsync(changed, Headers("Apply", changed), Until(), lifetime.Token), StatusCode.InvalidArgument);
                    foreach (var frame in new[] { "OwnerUid", "OwnerGid", "Mode", "LinkCount" })
                    {
                        changed = operation.Clone(); changed.Plan.RestoreSourceFrame.GetType().GetMethod("Clear" + frame)!.Invoke(changed.Plan.RestoreSourceFrame, null);
                        await Reject(async () => await client.ApplyAsync(changed, Headers("Apply", changed), Until(), lifetime.Token), StatusCode.InvalidArgument);
                    }
                    await Reject(async () => await client.InspectAsync(operation, Headers("Inspect", operation), Until(), lifetime.Token), StatusCode.NotFound);
                    await Emit(new { kind = "rejected", operationId, refusals }); continue;
                }
                Phase = action + "-value";
                var result = action switch {
                    "apply" => await client.ApplyAsync(operation, Headers("Apply", operation), Until(), lifetime.Token),
                    "inspect" => await client.InspectAsync(operation, Headers("Inspect", operation), Until(), lifetime.Token),
                    _ => await client.DiscardAsync(operation, Headers("Discard", operation), Until(), lifetime.Token) };
                Require(result.OperationId == operationId && result.ContainerId == id && result.InstanceId == instance && result.Fingerprint == supplied.Fingerprint &&
                    result.TargetProof == supplied.TargetProof && result.AclType == supplied.RestoreSource.ChangedAclType);
                await Emit(new { kind = "receipt", operationId, result = formatter.Format(result) });
            }
            throw new InvalidOperationException();
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
    private static void Fields(JsonElement value, params string[] names)
    { Require(value.ValueKind == JsonValueKind.Object); var fields = value.EnumerateObject().Select(p => p.Name).ToArray(); Require(fields.Length == names.Length && fields.ToHashSet(StringComparer.Ordinal).SetEquals(names)); }
    private static string Text(JsonElement value, string name) => value.GetProperty(name).GetString() ?? throw new InvalidOperationException();
    private static bool Lower(string value, int length) => value.Length == length && value.All(char.IsAsciiHexDigitLower);
    private static bool Upper(string value) => value.Length == 64 && value.All(c => char.IsAsciiDigit(c) || c is >= 'A' and <= 'F');
    private static void Require(bool condition) { if (!condition) throw new InvalidOperationException(); }
    private static async Task Denied(Func<Task> call, StatusCode expected)
    { try { await call(); } catch (RpcException error) when (error.StatusCode == expected) { return; } throw new InvalidOperationException(); }
}
