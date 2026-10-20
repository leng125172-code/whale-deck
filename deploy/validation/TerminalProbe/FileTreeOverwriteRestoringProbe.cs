using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Grpc.Core;
using Grpc.Net.Client;
using WhaleDeck.Contracts.Agent.V1;

/// <summary>
/// Installed fixed-UDS draft only. An external observer must pre-create and attest
/// small-source/small-target and large-source/large-target beneath the exact fixture.
/// Each tree has three entries and a 2 MiB+7 / 1 GiB+7 payload; target bytes differ.
/// This client neither creates a helper nor deletes, cleans, or directly reads fixture objects.
/// </summary>
internal static class FileTreeOverwriteRestoringProbe
{
    public static string Phase { get; private set; } = "overwrite-startup";
    public static string FailureCode { get; private set; } = "OVERWRITE_PROBE_FAILED";
    public static async Task RunAsync()
    {
        Check(OperatingSystem.IsLinux(), "OVERWRITE_PROBE_LINUX_REQUIRED");
        var prefix = Environment.GetEnvironmentVariable("WHALEDECK_OVERWRITE_PROBE_RELATIVE_PATH") ?? "";
        Check(Regex.IsMatch(prefix, "\\Awdv-agent-overwrite-[a-f0-9]{16}\\z"), "OVERWRITE_PROBE_FIXTURE_INVALID");
        const string root = "whaledeck.files.shared";
        const string method = "/whaledeck.agent.v1.FileTreeOverwriteRestoringService/";
        const string restoringMethod = "/whaledeck.agent.v1.FileTreeRestoringService/";
        const string recyclingMethod = "/whaledeck.agent.v1.FileTreeRecyclingService/";
        const string filesMethod = "/whaledeck.agent.v1.FileTransferService/";
        var key = File.ReadAllBytes("/etc/whaledeck/agent-capability.key");
        try
        {
            Check(key.Length == 32, "OVERWRITE_PROBE_KEY_INVALID");
            using var handler = new SocketsHttpHandler { UseProxy = false, ConnectCallback = async (_, token) => {
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                try { await socket.ConnectAsync(new UnixDomainSocketEndPoint("/run/whaledeck/agent.sock"), token); return new NetworkStream(socket, true); }
                catch { socket.Dispose(); throw; }
            } };
            using var channel = GrpcChannel.ForAddress("http://localhost", new() { HttpHandler = handler });
            var files = new FileTransferService.FileTransferServiceClient(channel);
            var recycling = new FileTreeRecyclingService.FileTreeRecyclingServiceClient(channel);
            var overwriting = new FileTreeOverwriteRestoringService.FileTreeOverwriteRestoringServiceClient(channel);
            var actor = new RequestContext { ActorSubject = "whaledeck-validation-agent-overwrite", TraceId = Guid.NewGuid().ToString("N") };
            var captureOwner = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var currentOwner = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            Check(captureOwner != currentOwner);
            var completed = new List<string>(); var canceled = new List<string>();
            var captures = new List<string>(); var backups = new List<string>(); var rejected = new List<string>();

            Phase = "overwrite-capabilities-and-roots";
            var capabilities = await new AgentService.AgentServiceClient(channel).GetCapabilitiesAsync(new(), deadline: DateTime.UtcNow.AddSeconds(10));
            Check(capabilities.ProtocolMajor == 1 && capabilities.ProtocolMinor >= 35 && capabilities.Capabilities.Contains("files.tree-overwrite-restore-intents") &&
                capabilities.Capabilities.Contains("files.tree-overwrite-restore-content-proof"));
            var roots = await files.ListRootsAsync(new(), Headers(filesMethod + "ListRoots"), deadline: DateTime.UtcNow.AddSeconds(10));
            var registered = roots.Roots.Single(item => item.Id == root);
            Check(registered.Actions.Contains("files-tree-overwrite-restore") && registered.Actions.Contains("files-tree-trash"));
            foreach (var size in new[] { "small", "large" })
            {
                var expectedBytes = size == "small" ? 2L * 1024 * 1024 + 7 : 1024L * 1024 * 1024 + 7;
                Phase = "overwrite-" + size + "-fixture";
                var listing = await List();
                var sourceEntry = listing.Entries.Single(item => item.Name == size + "-source");
                var targetEntry = listing.Entries.Single(item => item.Name == size + "-target");
                Check(Hash(sourceEntry.Version) && Hash(targetEntry.Version));

                Phase = "overwrite-" + size + "-capture";
                var capturePlan = await recycling.PlanAsync(new() { Context = actor, Command = new() {
                    Source = new() { RootId = root, Path = prefix + "/" + size + "-source" }, ExpectedVersion = sourceEntry.Version } },
                    Headers(recyclingMethod + "Plan"), deadline: DateTime.UtcNow.AddMinutes(10));
                Check(capturePlan.EntryCount == 3 && capturePlan.TotalBytes == expectedBytes);
                var capture = await recycling.ApplyAsync(new() { Context = actor, Owner = captureOwner,
                    OperationId = Guid.NewGuid().ToString("N"), Plan = capturePlan },
                    Headers(recyclingMethod + "Apply"), deadline: DateTime.UtcNow.AddMinutes(10));
                Check(capture.State == "Completed" && capture.SourceRemoved && capture.OriginalsRetained && capture.CaptureVerified &&
                    Identity(capture.CaptureId) && Hash(capture.ArchiveSha256) && capture.ArchiveBytes > 0);
                captures.Add(capture.CaptureId);

                var command = new FileTreeOverwriteRestoringCommand {
                    Source = capturePlan.Command.Source.Clone(), ExpectedVersion = capturePlan.Command.ExpectedVersion,
                    Target = new() { RootId = root, Path = prefix + "/" + size + "-target" }, TargetExpectedVersion = targetEntry.Version,
                    CaptureId = capture.CaptureId, CaptureOwner = captureOwner, CaptureKind = "Trash", ManifestSha256 = capture.ManifestSha256 };
                Phase = "overwrite-" + size + "-plan-and-denials";
                await Denied(async () => _ = await overwriting.PlanAsync(new() { Context = actor, Command = command },
                    deadline: DateTime.UtcNow.AddSeconds(10)), StatusCode.PermissionDenied);
                await Denied(async () => _ = await overwriting.PlanAsync(new() { Context = actor, Command = command },
                    Headers(restoringMethod + "Plan"), deadline: DateTime.UtcNow.AddSeconds(10)), StatusCode.PermissionDenied);
                var plan = await overwriting.PlanAsync(new() { Context = actor, Command = command },
                    Headers(method + "Plan"), deadline: DateTime.UtcNow.AddMinutes(10));
                Check(plan.Command.Equals(command) && plan.EntryCount == 3 && plan.TotalBytes == expectedBytes &&
                    plan.ArchiveBytes == capture.ArchiveBytes && plan.ArchiveSha256 == capture.ArchiveSha256 &&
                    plan.TargetEntryCount == 3 && plan.TargetTotalBytes == expectedBytes &&
                    plan.TargetParent is { Inode: > 0 } && plan.TargetIdentity is { Inode: > 0 } &&
                    Hash(plan.TargetManifestSha256) && Hash(plan.TargetTreeSha256) && Hash(plan.TargetContentSha256) &&
                    plan.TargetContentSha256 != plan.TargetTreeSha256 && Hash(plan.ContentSha256) &&
                    Hash(plan.SourcePolicyHash) && Hash(plan.TargetPolicyHash) && Hash(plan.Fingerprint));
                var wrongOwner = command.Clone(); wrongOwner.CaptureOwner = new('0', 64);
                await Denied(async () => _ = await overwriting.PlanAsync(new() { Context = actor, Command = wrongOwner },
                    Headers(method + "Plan"), deadline: DateTime.UtcNow.AddMinutes(10)), StatusCode.NotFound);
                var stale = command.Clone(); stale.TargetExpectedVersion = new('0', 64);
                await Denied(async () => _ = await overwriting.PlanAsync(new() { Context = actor, Command = stale },
                    Headers(method + "Plan"), deadline: DateTime.UtcNow.AddMinutes(10)), StatusCode.Aborted);

                Phase = "overwrite-" + size + "-cancel";
                var discardedRequest = new FileTreeOverwriteRestoringApplyRequest { Context = actor, Owner = currentOwner,
                    OperationId = Guid.NewGuid().ToString("N"), Plan = plan };
                var discarded = await overwriting.DiscardAsync(discardedRequest, Headers(method + "Discard"), deadline: DateTime.UtcNow.AddMinutes(10));
                Check(discarded.State == "Aborted" && discarded.OperationId == discardedRequest.OperationId &&
                    discarded.Kind == "DirectoryOverwriteRestore" && !discarded.PartialRetained && discarded.CaptureId == capture.CaptureId &&
                    discarded.Fingerprint == plan.Fingerprint && discarded.ArchiveBytes == plan.ArchiveBytes && discarded.ArchiveSha256 == plan.ArchiveSha256 &&
                    !discarded.BackupMayBeRetained && !discarded.BackupVerified && !discarded.TargetMayHaveChanged && !discarded.OriginalsRetained &&
                    discarded.OverwrittenCaptureId.Length == 0 && discarded.BackupArchiveBytes == 0 && discarded.BackupArchiveSha256.Length == 0);
                Check((await overwriting.ApplyAsync(discardedRequest, Headers(method + "Apply"), deadline: DateTime.UtcNow.AddMinutes(10))).Equals(discarded));
                Check((await List()).Entries.Single(item => item.Name == size + "-target").Version == targetEntry.Version);
                canceled.Add(discardedRequest.OperationId);

                Phase = "overwrite-" + size + "-method-and-fingerprint-denials";
                var request = discardedRequest.Clone(); request.OperationId = Guid.NewGuid().ToString("N");
                await Denied(async () => _ = await overwriting.ApplyAsync(request, Headers(recyclingMethod + "Apply"),
                    deadline: DateTime.UtcNow.AddSeconds(10)), StatusCode.PermissionDenied);
                await Denied(async () => _ = await overwriting.ApplyAsync(request, Headers(restoringMethod + "Apply"),
                    deadline: DateTime.UtcNow.AddSeconds(10)), StatusCode.PermissionDenied);
                var changed = request.Clone(); changed.OperationId = Guid.NewGuid().ToString("N"); changed.Plan.Fingerprint = new('0', 64);
                await Denied(async () => _ = await overwriting.ApplyAsync(changed, Headers(method + "Apply"),
                    deadline: DateTime.UtcNow.AddMinutes(10)), StatusCode.FailedPrecondition);
                rejected.Add(changed.OperationId);

                Phase = "overwrite-" + size + "-publish";
                var result = await overwriting.ApplyAsync(request, Headers(method + "Apply"), deadline: DateTime.UtcNow.AddMinutes(10));
                Check(result.State == "Completed" && result.Kind == "DirectoryOverwriteRestore" && result.OperationId == request.OperationId &&
                    result.CaptureId == capture.CaptureId && result.OverwrittenCaptureId == result.OperationId && result.OverwrittenCaptureId != result.CaptureId &&
                    !result.PartialRetained && Hash(result.Version) && result.Fingerprint == plan.Fingerprint &&
                    result.ArchiveSha256 == plan.ArchiveSha256 && result.ArchiveBytes == plan.ArchiveBytes &&
                    result.EntryCount == plan.EntryCount && result.TotalBytes == plan.TotalBytes &&
                    result.BackupVerified && result.BackupMayBeRetained && result.OriginalsRetained && result.TargetMayHaveChanged &&
                    result.BackupArchiveBytes is > 0 and <= 17L * 1024 * 1024 * 1024 && Hash(result.BackupArchiveSha256) &&
                    result.BackupArchiveSha256 != result.ArchiveSha256);
                completed.Add(result.OperationId); backups.Add(result.OverwrittenCaptureId);
                Check((await overwriting.ApplyAsync(request, Headers(method + "Apply"), deadline: DateTime.UtcNow.AddMinutes(10))).Equals(result));
                Check((await overwriting.DiscardAsync(request, Headers(method + "Discard"), deadline: DateTime.UtcNow.AddMinutes(10))).Equals(result));
                var lookup = new FileTreeOverwriteRestoringLookup { OperationId = result.OperationId, Owner = currentOwner };
                var replayToken = Headers(method + "Inspect");
                Check((await overwriting.InspectAsync(lookup, replayToken, deadline: DateTime.UtcNow.AddSeconds(20))).Equals(result));
                Check(DateTimeOffset.UtcNow.ToUnixTimeSeconds() < long.Parse(replayToken[0].Value.Split('.')[0], System.Globalization.CultureInfo.InvariantCulture),
                    "OVERWRITE_PROBE_REPLAY_WINDOW_EXPIRED");
                await Denied(async () => _ = await overwriting.InspectAsync(lookup, replayToken, deadline: DateTime.UtcNow.AddSeconds(10)), StatusCode.PermissionDenied);
                await Denied(async () => _ = await overwriting.InspectAsync(new() { OperationId = result.OperationId, Owner = captureOwner },
                    Headers(method + "Inspect"), deadline: DateTime.UtcNow.AddSeconds(20)), StatusCode.NotFound);
                await Denied(async () => _ = await overwriting.PlanAsync(new() { Context = actor, Command = command },
                    Headers(method + "Plan"), deadline: DateTime.UtcNow.AddMinutes(10)), StatusCode.Aborted);
            }
            Console.WriteLine(JsonSerializer.Serialize(new {
                installedTreeOverwriteRestoringRpc = "passed", completedOperationIds = completed, canceledOperationIds = canceled,
                rejectedFingerprintOperationIds = rejected, captureIds = captures, overwrittenCaptureIds = backups,
                smallBytes = 2L * 1024 * 1024 + 7, largeBytes = 1024L * 1024 * 1024 + 7,
                independentCaptureAndCurrentOwner = true, existingTargetVersionBound = true,
                nonceMethodOwnerFingerprintCancelAndReplayFences = true, independentDiskObserver = "required",
                overwrittenCaptureReuse = "not-covered", bffWorkerHumanAuditAndUi = "not-covered" }));

            async Task<FileListResponse> List() => await files.ListAsync(new() { Location = new() { RootId = root, Path = prefix }, Take = 100 },
                Headers(filesMethod + "List"), deadline: DateTime.UtcNow.AddSeconds(30));
        }
        finally { CryptographicOperations.ZeroMemory(key); }

        Metadata Headers(string name)
        {
            var nonce = DateTimeOffset.UtcNow.AddSeconds(45).ToUnixTimeSeconds() + "." + Guid.NewGuid().ToString("N");
            return new() { { "x-whaledeck-capability", nonce + "." + Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(nonce + "." + name))) } };
        }
    }

    private static bool Hash(string value) => Regex.IsMatch(value, "\\A[A-F0-9]{64}\\z");
    private static bool Identity(string value) => Guid.TryParseExact(value, "N", out var id) && id != Guid.Empty && id.ToString("N") == value;
    private static async Task Denied(Func<Task> action, StatusCode status)
    {
        try { await action(); }
        catch (RpcException error) when (error.StatusCode == status) { return; }
        Check(false, "OVERWRITE_PROBE_REJECTION_MISMATCH");
    }
    private static void Check(bool value, string code = "OVERWRITE_PROBE_CHECK_FAILED")
    {
        if (value) return;
        FailureCode = code; throw new InvalidOperationException(code);
    }
}
