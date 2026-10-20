using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Grpc.Core;
using Grpc.Net.Client;
using WhaleDeck.Contracts.Agent.V1;

/// <summary>Installed RPC only. An independent observer must attest each checkpoint before irreversible cleanup.</summary>
internal static class FileTreePurgingProbe
{
    public static string Phase { get; private set; } = "purge-startup";
    private sealed record Capture(string Name, FileTreePurgingCommand Command, long Bytes);

    public static async Task RunAsync()
    {
        Check(OperatingSystem.IsLinux());
        var prefix = Environment.GetEnvironmentVariable("WHALEDECK_PURGE_PROBE_RELATIVE_PATH") ?? "";
        Check(Regex.IsMatch(prefix, "\\Awdv-agent-purge-[a-f0-9]{16}\\z"));
        const string root = "whaledeck.files.shared", method = "/whaledeck.agent.v1.FileTreePurgingService/";
        const string recyclingMethod = "/whaledeck.agent.v1.FileTreeRecyclingService/";
        const string overwritingMethod = "/whaledeck.agent.v1.FileTreeOverwriteRestoringService/";
        const string filesMethod = "/whaledeck.agent.v1.FileTransferService/";
        var key = File.ReadAllBytes("/etc/whaledeck/agent-capability.key");
        try
        {
            Check(key.Length == 32);
            using var handler = new SocketsHttpHandler { UseProxy = false, ConnectCallback = async (_, token) => {
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                try { await socket.ConnectAsync(new UnixDomainSocketEndPoint("/run/whaledeck/agent.sock"), token); return new NetworkStream(socket, true); }
                catch { socket.Dispose(); throw; }
            } };
            using var channel = GrpcChannel.ForAddress("http://localhost", new() { HttpHandler = handler });
            var files = new FileTransferService.FileTransferServiceClient(channel);
            var recycling = new FileTreeRecyclingService.FileTreeRecyclingServiceClient(channel);
            var overwriting = new FileTreeOverwriteRestoringService.FileTreeOverwriteRestoringServiceClient(channel);
            var purging = new FileTreePurgingService.FileTreePurgingServiceClient(channel);
            var actor = new RequestContext { ActorSubject = "whaledeck-validation-agent-purge", TraceId = Guid.NewGuid().ToString("N") };
            var trashOwner = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var overwriteOwner = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var purgeOwner = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            Check(new[] { trashOwner, overwriteOwner, purgeOwner }.Distinct(StringComparer.Ordinal).Count() == 3);
            Phase = "purge-capabilities";
            var capabilities = await new AgentService.AgentServiceClient(channel).GetCapabilitiesAsync(new(), deadline: DateTime.UtcNow.AddSeconds(10));
            Check(capabilities.ProtocolMajor == 1 && capabilities.ProtocolMinor >= 36 && capabilities.Capabilities.Contains("files.tree-purge-intents"));
            var roots = await files.ListRootsAsync(new(), Headers(filesMethod + "ListRoots"), deadline: DateTime.UtcNow.AddSeconds(10));
            Check(roots.Roots.Single(row => row.Id == root).Actions.Contains("files-tree-purge"));
            var captures = new List<Capture>(); var completed = new List<string>(); var canceled = new List<string>(); var rejected = new List<string>();
            foreach (var size in new[] { "small", "large" })
            {
                Phase = "purge-" + size + "-capture";
                var bytes = size == "small" ? 2L * 1024 * 1024 + 7 : 1024L * 1024 * 1024 + 7;
                var listing = await files.ListAsync(new() { Location = new() { RootId = root, Path = prefix }, Take = 100 },
                    Headers(filesMethod + "List"), deadline: DateTime.UtcNow.AddSeconds(30));
                var source = listing.Entries.Single(row => row.Name == size + "-source");
                var target = listing.Entries.Single(row => row.Name == size + "-target");
                var recyclePlan = await recycling.PlanAsync(new() { Context = actor, Command = new() {
                    Source = new() { RootId = root, Path = prefix + "/" + size + "-source" }, ExpectedVersion = source.Version } },
                    Headers(recyclingMethod + "Plan"), deadline: DateTime.UtcNow.AddMinutes(10));
                var recycle = await recycling.ApplyAsync(new() { Context = actor, OperationId = Guid.NewGuid().ToString("N"), Owner = trashOwner, Plan = recyclePlan },
                    Headers(recyclingMethod + "Apply"), deadline: DateTime.UtcNow.AddMinutes(10));
                Check(recycle.State == "Completed" && recycle.CaptureVerified && recycle.OriginalsRetained && recycle.SourceRemoved && recyclePlan.TotalBytes == bytes);
                var overwritePlan = await overwriting.PlanAsync(new() { Context = actor, Command = new() {
                    Source = recyclePlan.Command.Source.Clone(), ExpectedVersion = recyclePlan.Command.ExpectedVersion,
                    Target = new() { RootId = root, Path = prefix + "/" + size + "-target" }, TargetExpectedVersion = target.Version,
                    CaptureId = recycle.CaptureId, CaptureOwner = trashOwner, CaptureKind = "Trash", ManifestSha256 = recycle.ManifestSha256 } },
                    Headers(overwritingMethod + "Plan"), deadline: DateTime.UtcNow.AddMinutes(10));
                var overwrite = await overwriting.ApplyAsync(new() { Context = actor, OperationId = Guid.NewGuid().ToString("N"), Owner = overwriteOwner, Plan = overwritePlan },
                    Headers(overwritingMethod + "Apply"), deadline: DateTime.UtcNow.AddMinutes(10));
                Check(overwrite.State == "Completed" && overwrite.BackupVerified && overwrite.OriginalsRetained && overwrite.TargetMayHaveChanged);
                captures.Add(new(size + "-source", new() { Source = recyclePlan.Command.Source.Clone(), ExpectedVersion = recyclePlan.Command.ExpectedVersion,
                    CaptureId = recycle.CaptureId, CaptureOwner = trashOwner, CaptureKind = "Trash", ManifestSha256 = recycle.ManifestSha256 }, bytes));
                captures.Add(new(size + "-target", new() { Source = overwritePlan.Command.Target.Clone(), ExpectedVersion = overwritePlan.Command.TargetExpectedVersion,
                    CaptureId = overwrite.OverwrittenCaptureId, CaptureOwner = overwriteOwner, CaptureKind = "Overwrite", ManifestSha256 = overwritePlan.TargetManifestSha256 }, bytes));
            }
            Console.WriteLine(JsonSerializer.Serialize(new { checkpoint = "captures-ready", captures = captures.Select(row => new {
                name = row.Name, captureId = row.Command.CaptureId, kind = row.Command.CaptureKind, bytes = row.Bytes }) }));
            await Checkpoint("attested-captures");
            foreach (var capture in captures.Where(row => row.Name.StartsWith("small-", StringComparison.Ordinal)))
                await Purge(capture, FileTreePurgeScope.Archive);
            Console.WriteLine(JsonSerializer.Serialize(new { checkpoint = "archives-purged", completedOperationIds = completed, canceledOperationIds = canceled }));
            await Checkpoint("attested-archives");
            foreach (var capture in captures)
                await Purge(capture, capture.Name.StartsWith("small-", StringComparison.Ordinal) ? FileTreePurgeScope.Originals : FileTreePurgeScope.All);
            Console.WriteLine(JsonSerializer.Serialize(new { installedTreePurgingRpc = "passed", completedOperationIds = completed,
                canceledOperationIds = canceled, rejectedFingerprintOperationIds = rejected,
                independentDiskObserver = "required", bffWorkerHumanAuditAndUi = "not-covered", actualInterruption = "not-covered" }));

            async Task Purge(Capture capture, FileTreePurgeScope scope)
            {
                Phase = "purge-" + capture.Name + "-" + scope;
                var command = capture.Command.Clone(); command.Scope = scope;
                await Denied(async () => _ = await purging.PlanAsync(new() { Context = actor, Command = command }, deadline: DateTime.UtcNow.AddSeconds(10)), StatusCode.PermissionDenied);
                await Denied(async () => _ = await purging.PlanAsync(new() { Context = actor, Command = command }, Headers(overwritingMethod + "Plan"), deadline: DateTime.UtcNow.AddSeconds(10)), StatusCode.PermissionDenied);
                var plan = await purging.PlanAsync(new() { Context = actor, Command = command }, Headers(method + "Plan"), deadline: DateTime.UtcNow.AddMinutes(10));
                Check(plan.Command.Equals(command) && plan.EntryCount == 3 && plan.TotalBytes == capture.Bytes && plan.ArchiveBytes > 0);
                var discardRequest = new FileTreePurgingApplyRequest { OperationId = Guid.NewGuid().ToString("N"), Owner = purgeOwner, Context = actor, Plan = plan };
                var aborted = await purging.DiscardAsync(discardRequest, Headers(method + "Discard"), deadline: DateTime.UtcNow.AddMinutes(10));
                Check(aborted.State == "Aborted" && !aborted.DeletionStarted && !aborted.ArchiveRemoved && !aborted.OriginalsRemoved);
                Check((await purging.ApplyAsync(discardRequest, Headers(method + "Apply"), deadline: DateTime.UtcNow.AddMinutes(10))).Equals(aborted));
                canceled.Add(discardRequest.OperationId);
                var bad = discardRequest.Clone(); bad.OperationId = Guid.NewGuid().ToString("N"); bad.Plan = plan.Clone(); bad.Plan.Fingerprint = new('0', 64);
                await Denied(async () => _ = await purging.ApplyAsync(bad, Headers(method + "Apply"), deadline: DateTime.UtcNow.AddMinutes(10)), StatusCode.FailedPrecondition);
                rejected.Add(bad.OperationId);
                var request = discardRequest.Clone(); request.OperationId = Guid.NewGuid().ToString("N");
                await Denied(async () => _ = await purging.ApplyAsync(request, Headers(recyclingMethod + "Apply"), deadline: DateTime.UtcNow.AddSeconds(10)), StatusCode.PermissionDenied);
                var receipt = await purging.ApplyAsync(request, Headers(method + "Apply"), deadline: DateTime.UtcNow.AddMinutes(10));
                Check(receipt.State == "Completed" && receipt.DeletionStarted && receipt.Scope == scope && receipt.CaptureId == capture.Command.CaptureId &&
                    receipt.ArchiveRemoved == (scope is FileTreePurgeScope.Archive or FileTreePurgeScope.All) &&
                    receipt.OriginalsRemoved == (scope is FileTreePurgeScope.Originals or FileTreePurgeScope.All));
                Check((await purging.ApplyAsync(request, Headers(method + "Apply"), deadline: DateTime.UtcNow.AddSeconds(20))).Equals(receipt));
                Check((await purging.DiscardAsync(request, Headers(method + "Discard"), deadline: DateTime.UtcNow.AddSeconds(20))).Equals(receipt));
                var lookup = new FileTreePurgingLookup { OperationId = request.OperationId, Owner = purgeOwner };
                var token = Headers(method + "Inspect");
                Check((await purging.InspectAsync(lookup, token, deadline: DateTime.UtcNow.AddSeconds(20))).Equals(receipt));
                Check(DateTimeOffset.UtcNow.ToUnixTimeSeconds() < long.Parse(token[0].Value.Split('.')[0], System.Globalization.CultureInfo.InvariantCulture));
                await Denied(async () => _ = await purging.InspectAsync(lookup, token, deadline: DateTime.UtcNow.AddSeconds(10)), StatusCode.PermissionDenied);
                await Denied(async () => _ = await purging.InspectAsync(new() { OperationId = request.OperationId, Owner = trashOwner }, Headers(method + "Inspect"), deadline: DateTime.UtcNow.AddSeconds(20)), StatusCode.NotFound);
                completed.Add(request.OperationId);
            }
        }
        finally { CryptographicOperations.ZeroMemory(key); }

        Metadata Headers(string name)
        {
            var nonce = DateTimeOffset.UtcNow.AddSeconds(45).ToUnixTimeSeconds() + "." + Guid.NewGuid().ToString("N");
            return new() { { "x-whaledeck-capability", nonce + "." + Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(nonce + "." + name))) } };
        }
    }
    private static async Task Checkpoint(string expected)
    {
        Phase = "purge-observer-" + expected;
        Check(await Console.In.ReadLineAsync().WaitAsync(TimeSpan.FromMinutes(10)) == expected);
    }
    private static async Task Denied(Func<Task> action, StatusCode status)
    {
        try { await action(); }
        catch (RpcException error) when (error.StatusCode == status) { return; }
        Check(false);
    }
    private static void Check(bool value) { if (!value) throw new InvalidOperationException("PURGE_PROBE_CHECK_FAILED"); }
}
