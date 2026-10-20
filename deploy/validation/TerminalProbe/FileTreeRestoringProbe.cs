using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Grpc.Core;
using Grpc.Net.Client;
using WhaleDeck.Contracts.Agent.V1;

/// <summary>Real installed RPC only. Independent observer checks disk bytes and private captures.</summary>
internal static class FileTreeRestoringProbe
{
    public static string Phase { get; private set; } = "restoring-startup";
    public static async Task RunAsync()
    {
        var prefix = Environment.GetEnvironmentVariable("WHALEDECK_RESTORE_PROBE_RELATIVE_PATH") ?? "";
        Check(Regex.IsMatch(prefix, "^wdv-agent-restore-[a-f0-9]{16}$"));
        const string root = "whaledeck.files.shared";
        const string method = "/whaledeck.agent.v1.FileTreeRestoringService/";
        const string recyclingMethod = "/whaledeck.agent.v1.FileTreeRecyclingService/";
        const string fileMethod = "/whaledeck.agent.v1.FileTransferService/";
        var key = File.ReadAllBytes("/etc/whaledeck/agent-capability.key");
        using var handler = new SocketsHttpHandler { UseProxy = false, ConnectCallback = async (_, token) => {
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try { await socket.ConnectAsync(new UnixDomainSocketEndPoint("/run/whaledeck/agent.sock"), token); return new NetworkStream(socket, true); }
            catch { socket.Dispose(); throw; }
        } };
        using var channel = GrpcChannel.ForAddress("http://localhost", new() { HttpHandler = handler });
        var files = new FileTransferService.FileTransferServiceClient(channel);
        var recycling = new FileTreeRecyclingService.FileTreeRecyclingServiceClient(channel);
        var restoring = new FileTreeRestoringService.FileTreeRestoringServiceClient(channel);
        var actor = new RequestContext { ActorSubject = "whaledeck-validation-agent-restore", TraceId = Guid.NewGuid().ToString("N") };
        var captureOwner = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var currentOwner = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var completed = new List<string>(); var canceled = new List<string>(); var captures = new List<string>();
        try
        {
            Phase = "restoring-capabilities-and-roots";
            var capabilities = await new AgentService.AgentServiceClient(channel).GetCapabilitiesAsync(new());
            Check(capabilities.ProtocolMajor == 1 && capabilities.ProtocolMinor >= 33 && capabilities.Capabilities.Contains("files.tree-restore-intents"));
            var roots = await files.ListRootsAsync(new(), Headers(fileMethod + "ListRoots"));
            Check(roots.Roots.Single(item => item.Id == root).Actions.Contains("files-tree-restore"));
            foreach (var size in new[] { "small", "large" })
            {
                Phase = "restoring-" + size + "-capture";
                var listing = await files.ListAsync(new() { Location = new() { RootId = root, Path = prefix }, Take = 100 }, Headers(fileMethod + "List"));
                var entry = listing.Entries.Single(item => item.Name == size);
                var capturePlan = await recycling.PlanAsync(new() { Context = actor,
                    Command = new() { Source = new() { RootId = root, Path = prefix + "/" + size }, ExpectedVersion = entry.Version } },
                    Headers(recyclingMethod + "Plan"), deadline: DateTime.UtcNow.AddMinutes(8));
                var capture = await recycling.ApplyAsync(new() { Context = actor, Owner = captureOwner, OperationId = Guid.NewGuid().ToString("N"), Plan = capturePlan },
                    Headers(recyclingMethod + "Apply"), deadline: DateTime.UtcNow.AddMinutes(10));
                Check(capture.State == "Completed" && capture.SourceRemoved && capture.OriginalsRetained && capture.CaptureVerified);
                captures.Add(capture.CaptureId);
                var command = new FileTreeRestoringCommand { Source = capturePlan.Command.Source.Clone(), ExpectedVersion = capturePlan.Command.ExpectedVersion,
                    Target = new() { RootId = root, Path = prefix + "/" + (size == "small" ? "small" : "large-restored") }, CaptureId = capture.CaptureId,
                    CaptureOwner = captureOwner, CaptureKind = "Trash", ManifestSha256 = capture.ManifestSha256 };
                Phase = "restoring-" + size + "-plan-and-denials";
                await Denied(async () => _ = await restoring.PlanAsync(new() { Context = actor, Command = command }), StatusCode.PermissionDenied);
                var token = Headers(method + "Plan");
                var plan = await restoring.PlanAsync(new() { Context = actor, Command = command }, token, deadline: DateTime.UtcNow.AddMinutes(8));
                Check(plan.Command.Equals(command) && plan.EntryCount == 3 && plan.TotalBytes == (size == "small" ? 2 * 1024 * 1024 + 7 : 1024L * 1024 * 1024 + 7) &&
                    plan.ArchiveBytes == capture.ArchiveBytes && plan.ArchiveSha256 == capture.ArchiveSha256);
                await Denied(async () => _ = await restoring.PlanAsync(new() { Context = actor, Command = command }, token), StatusCode.PermissionDenied);
                var substituted = command.Clone(); substituted.CaptureOwner = new('0', 64);
                Phase = "restoring-" + size + "-capture-owner-denial";
                // A foreign owner must not learn whether this private capture exists.
                await Denied(async () => _ = await restoring.PlanAsync(new() { Context = actor, Command = substituted }, Headers(method + "Plan")), StatusCode.NotFound);
                Phase = "restoring-" + size + "-cancel-and-method-denials";
                var canceledRequest = new FileTreeRestoringApplyRequest { Context = actor, Owner = currentOwner, OperationId = Guid.NewGuid().ToString("N"), Plan = plan };
                var aborted = await restoring.DiscardAsync(canceledRequest, Headers(method + "Discard"));
                Check(aborted.State == "Aborted" && !aborted.PartialRetained && aborted.CaptureId == capture.CaptureId);
                Check((await restoring.ApplyAsync(canceledRequest, Headers(method + "Apply"))).Equals(aborted)); canceled.Add(canceledRequest.OperationId);
                var request = canceledRequest.Clone(); request.OperationId = Guid.NewGuid().ToString("N");
                await Denied(async () => _ = await restoring.ApplyAsync(request, Headers(recyclingMethod + "Apply")), StatusCode.PermissionDenied);
                var bad = request.Clone(); bad.OperationId = Guid.NewGuid().ToString("N"); bad.Plan.Fingerprint = new('0', 64);
                await Denied(async () => _ = await restoring.ApplyAsync(bad, Headers(method + "Apply")), StatusCode.FailedPrecondition);
                Phase = "restoring-" + size + "-publish";
                var result = await restoring.ApplyAsync(request, Headers(method + "Apply"), deadline: DateTime.UtcNow.AddMinutes(10));
                Check(result.State == "Completed" && result.Kind == "DirectoryRestore" && result.CaptureId == capture.CaptureId && result.OperationId != result.CaptureId &&
                    !result.PartialRetained && result.Version.Length == 64 && result.ArchiveSha256 == plan.ArchiveSha256 && result.ArchiveBytes == plan.ArchiveBytes);
                completed.Add(result.OperationId);
                Check((await restoring.ApplyAsync(request, Headers(method + "Apply"), deadline: DateTime.UtcNow.AddMinutes(8))).Equals(result));
                Check((await restoring.DiscardAsync(request, Headers(method + "Discard"))).Equals(result));
                Check((await restoring.InspectAsync(new() { OperationId = result.OperationId, Owner = currentOwner }, Headers(method + "Inspect"))).Equals(result));
                await Denied(async () => _ = await restoring.InspectAsync(new() { OperationId = result.OperationId, Owner = captureOwner }, Headers(method + "Inspect")), StatusCode.NotFound);
                await Denied(async () => _ = await restoring.PlanAsync(new() { Context = actor, Command = command }, Headers(method + "Plan"), deadline: DateTime.UtcNow.AddMinutes(8)), StatusCode.Aborted);
            }
            Console.WriteLine(JsonSerializer.Serialize(new { installedTreeRestoringRpc = "passed", completedOperationIds = completed, canceledOperationIds = canceled,
                captureIds = captures, smallBytes = 2 * 1024 * 1024 + 7, largeBytes = 1024L * 1024 * 1024 + 7,
                independentCaptureAndCurrentOwner = true, absentOriginalAndDifferentDestination = true,
                nonceMethodOwnerFingerprintCancelAndReplayFences = true, independentDiskObserver = "required", bffWorkerHumanAuditAndUi = "not-covered" }));
        }
        finally { CryptographicOperations.ZeroMemory(key); }
        Metadata Headers(string name)
        {
            var nonce = DateTimeOffset.UtcNow.AddSeconds(45).ToUnixTimeSeconds() + "." + Guid.NewGuid().ToString("N");
            return new() { { "x-whaledeck-capability", nonce + "." + Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(nonce + "." + name))) } };
        }
    }
    private static async Task Denied(Func<Task> action, StatusCode status)
    {
        try { await action(); }
        catch (RpcException error) when (error.StatusCode == status) { return; }
        throw new InvalidOperationException("RESTORING_PROBE_REJECTION_MISMATCH");
    }
    private static void Check(bool value) { if (!value) throw new InvalidOperationException("RESTORING_PROBE_CHECK_FAILED"); }
}
