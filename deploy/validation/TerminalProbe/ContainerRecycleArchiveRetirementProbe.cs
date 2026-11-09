using System.Diagnostics.CodeAnalysis;
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

/// <summary>Parent-paced owned QA only. No setup, automatic Apply, cleanup or retry.</summary>
internal static class ContainerRecycleArchiveRetirementProbe
{
    public static string Phase { get; private set; } = "startup";
    public static string FailureCode { get; private set; } = "";
    public static object? CapabilityObservation { get; private set; }
    public static async Task RunAsync()
    {
        Require(OperatingSystem.IsLinux());
        using var lifetime = new CancellationTokenSource(TimeSpan.FromMinutes(12));
        using var stdin = Console.OpenStandardInput();
        using var header = await ReadAsync(stdin, 8192, lifetime.Token);
        var input = header.RootElement; Fields(input, "revision", "probeRevision", "containerId", "instanceId", "coreId");
        var revision = Text(input, "revision"); var probeRevision = Text(input, "probeRevision");
        var id = Text(input, "containerId"); var instance = Text(input, "instanceId"); var core = Text(input, "coreId");
        Require(Lower(revision, 40) && Lower(probeRevision, 40) && Lower(id, 64) && Lower(core, 64) && id != core && instance.Length is > 20 and < 64 && !instance.Any(char.IsControl));
        Require(Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.EndsWith("+" + probeRevision, StringComparison.Ordinal));
        using var handler = new SocketsHttpHandler { UseProxy = false, ConnectCallback = async (_, token) =>
        {
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try { await socket.ConnectAsync(new UnixDomainSocketEndPoint("/run/whaledeck/agent.sock"), token); return new NetworkStream(socket, true); }
            catch { socket.Dispose(); throw; }
        } };
        using var channel = GrpcChannel.ForAddress("http://localhost", new() { HttpHandler = handler });
        Phase = "capabilities";
        var caps = await new AgentService.AgentServiceClient(channel).GetCapabilitiesAsync(new(), deadline: DateTime.UtcNow.AddSeconds(10), cancellationToken: lifetime.Token);
        CapabilityObservation = new { protocolMajor = caps.ProtocolMajor, protocolMinor = caps.ProtocolMinor, retirementCapability = caps.Capabilities.Contains(ContainerRecycleArchiveRetirementBinding.Capability) };
        if (!ContainerRecycleArchiveRetirementBinding.IsSupported(caps))
        {
            FailureCode = "CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_CAPABILITY_UNAVAILABLE";
            throw new InvalidOperationException();
        }
        // This location is deliberately after the live version/capability gate.
        Phase = "signing-key";
        var key = await File.ReadAllBytesAsync("/etc/whaledeck/agent-capability.key", lifetime.Token);
        try
        {
            Require(key.Length == 32);
            var client = new ContainerRecycleArchiveRetirementService.ContainerRecycleArchiveRetirementServiceClient(channel);
            var plans = new Dictionary<string, ContainerRecycleArchiveRetirementPlan>(StringComparer.Ordinal);
            var operations = new Dictionary<string, ContainerRecycleArchiveRetirementRequest>(StringComparer.Ordinal);
            var states = new Dictionary<string, string>(StringComparer.Ordinal);
            var applied = new HashSet<string>(StringComparer.Ordinal);
            var formatter = new JsonFormatter(new JsonFormatter.Settings(formatDefaultValues: true));
            DateTime Until() => DateTime.UtcNow.AddSeconds(120);
            RequestContext Context() => new() { ActorSubject = "installed-recycle-retirement-probe", TraceId = "owned-retirement-validation" };
            Metadata Headers(string action, IMessage request)
            {
                var hash = ContainerRecycleArchiveRetirementBinding.Hash(request);
                var expires = DateTimeOffset.UtcNow.AddSeconds(45).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture); var nonce = Guid.NewGuid().ToString("N");
                var mac = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(ContainerRecycleArchiveRetirementBinding.Payload(expires, nonce, ContainerRecycleArchiveRetirementBinding.Service + action, hash)));
                return new() { { "x-whaledeck-capability", expires + "." + nonce + "." + Convert.ToHexString(mac) }, { ContainerRecycleArchiveRetirementBinding.Header, hash } };
            }
            async Task Emit(object value) { await Console.Out.WriteLineAsync(JsonSerializer.Serialize(value)); await Console.Out.FlushAsync(lifetime.Token); }
            ContainerRecycleArchiveRequest? approvedArchive = null;
            Phase = "ready"; await Emit(new { kind = "ready", revision, probeRevision });
            for (var count = 0; count < 64; count++)
            {
                using var document = await ReadAsync(stdin, 65536, lifetime.Token); input = document.RootElement;
                var action = Text(input, "action"); Phase = action;
                if (action == "quit") { Fields(input, "action"); await Emit(new { kind = "stopped", automaticCleanup = false }); return; }
                if (action == "plan")
                {
                    Fields(input, "action", "archive", "retainDays");
                    var archive = JsonParser.Default.Parse<ContainerRecycleArchiveRequest>(Text(input, "archive")); ValidateScope(archive, id, instance);
                    Require(approvedArchive is null || archive.Equals(approvedArchive)); approvedArchive ??= archive.Clone();
                    var days = input.GetProperty("retainDays").GetUInt32(); Require(days is >= 1 and <= 3650);
                    var request = new ContainerRecycleArchiveRetirementPlanRequest { Command = new() { Archive = new() { OperationId = archive.OperationId, Owner = archive.Owner, Plan = archive.Plan.Clone() }, RetainDays = days, Mode = "ArchiveData" }, Context = Context() };
                    var authority = Headers("Plan", request);
                    var plan = await client.PlanAsync(request, authority, Until(), lifetime.Token); ValidatePlan(request, plan);
                    plans[plan.Fingerprint] = plan.Clone();
                    // The accepted nonce must not authorize a second request.
                    await Denied(async () => await client.PlanAsync(request, authority, Until(), lifetime.Token));
                    await Emit(new { kind = "retirement-plan", plan = formatter.Format(plan), replayRefused = true }); continue;
                }
                Require(action is "reserve" or "apply" or "inspect" or "discard"); Fields(input, "action", "operationId", "plan");
                var operationId = Text(input, "operationId"); Identity(operationId);
                var supplied = JsonParser.Default.Parse<ContainerRecycleArchiveRetirementPlan>(Text(input, "plan")); ContainerRecycleArchiveRetirementWire.RequireKnown(supplied);
                Require(plans.TryGetValue(supplied.Fingerprint, out var approved) && supplied.Equals(approved));
                if (!operations.TryGetValue(operationId, out var operation))
                {
                    Require(action is "reserve" or "discard"); ValidateNewId(operationId, supplied);
                    var context = Context(); context.JobId = operationId; context.IdempotencyKey = Guid.NewGuid().ToString("N");
                    operation = new() { OperationId = operationId, Owner = new('E', 64), Plan = supplied.Clone(), Context = context };
                    operations.Add(operationId, operation); states.Add(operationId, "unreserved");
                }
                Require(operation.Plan.Equals(supplied)); RequireAction(action, states[operationId], applied.Contains(operationId));
                // Mark before awaiting: an interrupted Apply is never retried by
                // this process, even if a caller submits the same line again.
                if (action == "apply") applied.Add(operationId);
                var result = action switch {
                    "reserve" => await client.ReserveAsync(operation, Headers("Reserve", operation), Until(), lifetime.Token),
                    "apply" => await client.ApplyAsync(operation, Headers("Apply", operation), Until(), lifetime.Token),
                    "inspect" => await client.InspectAsync(operation, Headers("Inspect", operation), Until(), lifetime.Token),
                    _ => await client.DiscardAsync(operation, Headers("Discard", operation), Until(), lifetime.Token) };
                ContainerRecycleArchiveRetirementWire.Receipt(operation, result); states[operationId] = result.State;
                await Emit(new { kind = "retirement-receipt", action, operationId, request = formatter.Format(operation), result = formatter.Format(result) });
            }
            throw new InvalidOperationException();
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
    internal static void RequireAction(string action, string state, bool applied)
    {
        Require(action is "reserve" or "apply" or "inspect" or "discard");
        Require(action != "apply" || state == "Reserved" && !applied);
        Require(action != "reserve" || state == "unreserved");
    }
    internal static void ValidateNewId(string id, ContainerRecycleArchiveRetirementPlan p)
    {
        Identity(id); var archive = p.Command.Archive; var purge = archive.Plan.Command.Purge;
        Require(id != archive.OperationId && id != purge.OperationId && id != purge.Plan.Command.Source.OperationId);
    }
    internal static void ValidateScope(ContainerRecycleArchiveRequest archive, string id, string instance)
    {
        ContainerRecycleArchiveWire.RequireKnown(archive); Identity(archive.OperationId); Require(Upper(archive.Owner));
        var p = archive.Plan; var purge = p.Command.Purge; var original = purge.Plan.Command.Source.Plan;
        Require(original.Command.Location.ContainerId == id && original.Command.Location.InstanceId == instance && original.Command.Location.Path == "work/recycle-rpc.bin");
        Require(original.OwnerUid == 1000 && original.OwnerGid == 1000 && original.SourceIdentity is { OwnerUid: 1000, OwnerGid: 1000, LinkCount: 1, SizeBytes: >= 0 and <= 16777216 });
        Require(p.Fingerprint == ContainerRecycleArchiveWire.Fingerprint(p) && purge.Plan.Fingerprint == ContainerRecyclePurgeWire.Fingerprint(purge.Plan));
        Require(archive.OperationId != purge.OperationId && archive.OperationId != purge.Plan.Command.Source.OperationId);
    }
    internal static void ValidatePlan(ContainerRecycleArchiveRetirementPlanRequest q, ContainerRecycleArchiveRetirementPlan p)
    {
        ContainerRecycleArchiveRetirementWire.RequireKnown(p);
        Require(p.SchemaVersion == 1 && p.Command is not null && p.Command.Equals(q.Command) && p.Fingerprint == ContainerRecycleArchiveRetirementWire.Fingerprint(p));
        var old = q.Command.Archive.Plan;
        Require(p.HistoryCreatedAtUnixMs == old.Command.Purge.Plan.HistoryCreatedAtUnixMs && p.HistoryCreatedAtUnixMs is >= 0 and <= 253402300799999);
        Require(p.Command.RetainDays is >= 1 and <= 3650 && p.NotBeforeUnixMs == p.HistoryCreatedAtUnixMs + (long)p.Command.RetainDays * 86400000 && p.NotBeforeUnixMs <= 253402300799999);
        Require(p.RetirementEligible == (p.RetentionElapsed && p.BindingCurrent));
        Require(p.SourceRecordDigest == old.SourceRecordDigest && p.PurgeRecordDigest == old.PurgeRecordDigest && p.PurgeMarkerDigest == old.PurgeMarkerDigest && p.HistoryMetadataDigest == old.HistoryMetadataDigest);
        Require(p.StorageHash == old.StorageHash && p.StoreIdentity is not null && p.StoreIdentity.Equals(old.StoreIdentity) && p.ArchiveIdentity is not null && p.ArchiveIdentity.Equals(old.ArchiveIdentity) && p.ArchiveChangedAtUnixNs == old.ArchiveChangedAtUnixNs && p.ArchiveBytes == old.ArchiveBytes && p.ArchiveSha256 == old.ArchiveSha256);
        foreach (var hash in new[] { p.ArchiveRecordDigest, p.ClaimDigest, p.Fingerprint }) Require(Upper(hash));
        var original = old.Command.Purge.Plan.Command.Source.Plan.SourceIdentity; var i = p.PublishedIdentity;
        Require(i is not null && i.Inode > 0 && i.Device == old.ParentIdentity.Device && i.LinkCount == 1 && !(i.Device == original.Device && i.Inode == original.Inode) && !(i.Device == old.ArchiveIdentity.Device && i.Inode == old.ArchiveIdentity.Inode));
        Require(i!.Mode == original.Mode && i.OwnerUid == original.OwnerUid && i.OwnerGid == original.OwnerGid && i.SizeBytes == original.SizeBytes && i.ModifiedAtUnixNs == original.ModifiedAtUnixNs && p.PublishedChangedAtUnixNs >= 0);
    }
    internal static async Task<JsonDocument> ReadAsync(Stream source, int maximum, CancellationToken token)
    {
        using var bytes = new MemoryStream(); var one = new byte[1]; var terminated = false;
        while (await source.ReadAsync(one, token) != 0)
        {
            if (one[0] == 10) { terminated = true; break; }
            Require(bytes.Length < maximum && one[0] != 13); bytes.WriteByte(one[0]);
        }
        Require(terminated && bytes.Length > 0);
        return JsonDocument.Parse(bytes.ToArray(), new JsonDocumentOptions { MaxDepth = 24 });
    }
    private static void Fields(JsonElement value, params string[] names)
    {
        Require(value.ValueKind == JsonValueKind.Object); var keys = value.EnumerateObject().Select(p => p.Name).ToArray();
        Require(keys.Length == names.Length && keys.ToHashSet(StringComparer.Ordinal).SetEquals(names));
    }
    private static string Text(JsonElement value, string name) => value.GetProperty(name).GetString() ?? throw new InvalidOperationException();
    private static bool Lower(string value, int length) => value.Length == length && value.All(char.IsAsciiHexDigitLower);
    private static bool Upper(string value) => value.Length == 64 && value.All(c => char.IsAsciiDigit(c) || c is >= 'A' and <= 'F');
    private static void Identity(string value) => Require(Lower(value, 32) && Guid.ParseExact(value, "N") != Guid.Empty);
    private static void Require([DoesNotReturnIf(false)] bool condition) { if (!condition) throw new InvalidOperationException(); }
    private static async Task Denied(Func<Task> call)
    {
        try { await call(); } catch (RpcException e) when (e.StatusCode == StatusCode.PermissionDenied) { return; }
        throw new InvalidOperationException();
    }
}
