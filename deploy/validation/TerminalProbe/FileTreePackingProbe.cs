using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using WhaleDeck.Contracts.Agent.V1;

/// <summary>Installed signed UDS/fixed-helper acceptance only, not BFF/audit/UI.</summary>
internal static class FileTreePackingProbe
{
    public static string Phase { get; private set; } = "packing-startup";
    public static async Task RunAsync()
    {
        var prefix = Environment.GetEnvironmentVariable("WHALEDECK_PACK_PROBE_RELATIVE_PATH") ?? "";
        Check(Regex.IsMatch(prefix, "^wdv-agent-pack-[a-f0-9]{16}$"));
        const string root = "whaledeck.files.shared";
        const string method = "/whaledeck.agent.v1.FileTreePackingService/";
        const string fileMethod = "/whaledeck.agent.v1.FileTransferService/";
        var key = File.ReadAllBytes("/etc/whaledeck/agent-capability.key");
        using var handler = new SocketsHttpHandler { UseProxy = false, ConnectCallback = async (_, token) =>
        {
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try { await socket.ConnectAsync(new UnixDomainSocketEndPoint("/run/whaledeck/agent.sock"), token); return new NetworkStream(socket, true); }
            catch { socket.Dispose(); throw; }
        } };
        using var channel = GrpcChannel.ForAddress("http://localhost", new() { HttpHandler = handler });
        var files = new FileTransferService.FileTransferServiceClient(channel);
        var packing = new FileTreePackingService.FileTreePackingServiceClient(channel);
        var actor = new RequestContext { ActorSubject = "whaledeck-validation-agent-pack", TraceId = Guid.NewGuid().ToString("N") };
        var owner = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var completed = new List<string>(); var canceled = new List<string>(); long largeBytes = 0;
        try
        {
            Phase = "packing-capabilities-and-roots";
            var capabilities = await new AgentService.AgentServiceClient(channel).GetCapabilitiesAsync(new Empty());
            Check(capabilities.ProtocolMajor == 1 && capabilities.ProtocolMinor >= 30 && capabilities.Capabilities.Contains("files.tree-pack-intents") && capabilities.Capabilities.Contains("files.tree-pack-download"));
            var roots = await files.ListRootsAsync(new(), Headers(fileMethod + "ListRoots"));
            Check(roots.Roots.Single(item => item.Id == root).Actions.Contains("files-tree-pack"));
            foreach (var format in new[] { "tar", "tar.gz" })
            {
                Phase = "packing-" + format + "-plan";
                var command = await Command("source", "small." + format, format);
                var reused = Headers(method + "Plan");
                var plan = await packing.PlanAsync(new() { Command = command, Context = actor }, reused, deadline: DateTime.UtcNow.AddMinutes(5));
                Check(plan.Command.Equals(command) && plan.EntryCount == 3 && plan.TotalBytes == 2 * 1024 * 1024 + 7);
                Phase = "packing-nonce-and-method-fences";
                await Denied(async () => _ = await packing.PlanAsync(new() { Command = command, Context = actor }, reused), StatusCode.PermissionDenied);
                var request = new FileTreePackingApplyRequest { OperationId = Guid.NewGuid().ToString("N"), Owner = owner, Plan = plan, Context = actor };
                await Denied(async () => _ = await packing.ApplyAsync(request, Headers(method + "Plan")), StatusCode.PermissionDenied);
                var tampered = request.Clone(); tampered.OperationId = Guid.NewGuid().ToString("N"); tampered.Plan.Fingerprint = new('0', 64);
                await Denied(async () => _ = await packing.ApplyAsync(tampered, Headers(method + "Apply")), StatusCode.FailedPrecondition);
                var stale = command.Clone(); stale.Tree.ExpectedVersion = new('A', 64);
                await Denied(async () => _ = await packing.PlanAsync(new() { Command = stale, Context = actor }, Headers(method + "Plan")), StatusCode.Aborted);
                Phase = "packing-" + format + "-publish";
                var receipt = await packing.ApplyAsync(request, Headers(method + "Apply"), deadline: DateTime.UtcNow.AddMinutes(8));
                Check(receipt.State == "Completed" && !receipt.PartialRetained && receipt.ArchiveBytes > 0 && receipt.Format == format && receipt.Fingerprint == plan.Fingerprint);
                completed.Add(request.OperationId);
                Check((await packing.InspectAsync(new() { OperationId = request.OperationId, Owner = owner }, Headers(method + "Inspect"))).Equals(receipt));
                Check((await packing.ApplyAsync(request, Headers(method + "Apply"))).Equals(receipt));
                await Denied(async () => _ = await packing.PlanAsync(new() { Command = command, Context = actor }, Headers(method + "Plan")), StatusCode.Aborted);
                await Denied(async () => _ = await packing.InspectAsync(new() { OperationId = request.OperationId, Owner = new('A', 64) }, Headers(method + "Inspect")), StatusCode.NotFound);
                Phase = "packing-" + format + "-signed-download";
                await Download(receipt);
            }
            Phase = "packing-cancel-before-execution";
            var canceledPlan = await packing.PlanAsync(new() { Command = await Command("source", "canceled.tar.gz", "tar.gz"), Context = actor }, Headers(method + "Plan"));
            var cancel = new FileTreePackingApplyRequest { OperationId = Guid.NewGuid().ToString("N"), Owner = owner, Plan = canceledPlan, Context = actor };
            var discarded = await packing.DiscardAsync(cancel, Headers(method + "Discard")); Check(discarded.State == "Aborted" && !discarded.PartialRetained);
            Check((await packing.ApplyAsync(cancel, Headers(method + "Apply"))).Equals(discarded)); canceled.Add(cancel.OperationId);
            Phase = "packing-protected-source";
            var forbidden = await Command("protected", "forbidden.tar.gz", "tar.gz");
            await Denied(async () => _ = await packing.PlanAsync(new() { Command = forbidden, Context = actor }, Headers(method + "Plan")), StatusCode.PermissionDenied);
            if (Environment.GetEnvironmentVariable("WHALEDECK_PACK_PROBE_LARGE") == "1")
            {
                Phase = "packing-large-raw-tar-plan";
                var plan = await packing.PlanAsync(new() { Command = await Command("large-source", "large.tar", "tar"), Context = actor }, Headers(method + "Plan"), deadline: DateTime.UtcNow.AddMinutes(5));
                Check(plan.TotalBytes == 1024L * 1024 * 1024 + 7 && plan.EntryCount == 3);
                var receipt = await packing.ApplyAsync(new() { OperationId = Guid.NewGuid().ToString("N"), Owner = owner, Plan = plan, Context = actor }, Headers(method + "Apply"), deadline: DateTime.UtcNow.AddMinutes(8));
                Check(receipt.State == "Completed" && receipt.ArchiveBytes > 1024L * 1024 * 1024); completed.Add(receipt.OperationId);
                Phase = "packing-large-dedicated-stream-not-general-download";
                await Download(receipt); largeBytes = plan.TotalBytes;
                await Denied(async () =>
                {
                    using var call = files.Download(new() { Location = plan.Command.Tree.Target, ExpectedVersion = receipt.Version }, Headers(fileMethod + "Download"), deadline: DateTime.UtcNow.AddSeconds(30));
                    _ = await call.ResponseStream.MoveNext(default);
                }, StatusCode.FailedPrecondition);
            }
            Phase = "packing-final-membership";
            var listing = await files.ListAsync(new() { Location = new() { RootId = root, Path = prefix }, Take = 100 }, Headers(fileMethod + "List"));
            Check(!listing.Entries.Any(item => item.Name is "canceled.tar.gz" or "forbidden.tar.gz"));
            Console.WriteLine(JsonSerializer.Serialize(new { installedTreePackingRpc = "passed", bothFormats = true, smallBytes = 2 * 1024 * 1024 + 7, largeBytes,
                methodNonceOwnerVersionPlanAndCancelFences = true, protectedAndExistingTargetsRejected = true, signedDownloadWholeArchiveChecksumMatched = true,
                ordinaryDownloadLimitUnchanged = true, completedOperationIds = completed, canceledOperationIds = canceled, bffWorkerHumanAuditAndUi = "not-covered" }));
        }
        finally { CryptographicOperations.ZeroMemory(key); }
        Metadata Headers(string name)
        {
            var nonce = DateTimeOffset.UtcNow.AddSeconds(45).ToUnixTimeSeconds() + "." + Guid.NewGuid().ToString("N");
            return new() { { "x-whaledeck-capability", nonce + "." + Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(nonce + "." + name))) } };
        }
        async Task<FileTreePackingCommand> Command(string source, string target, string format)
        {
            var location = new FileLocation { RootId = root, Path = prefix + "/" + source };
            var page = await files.ListAsync(new() { Location = location, Take = 1 }, Headers(fileMethod + "List"));
            return new() { Tree = new() { Source = location, ExpectedVersion = page.Version, Target = new() { RootId = root, Path = prefix + "/" + target } }, Format = format };
        }
        async Task Download(FileTreePackingReceipt receipt)
        {
            var request = new FileTreePackingDownloadRequest { OperationId = receipt.OperationId, Owner = owner, Context = actor };
            await Denied(async () => { using var invalid = packing.Download(request); _ = await invalid.ResponseStream.MoveNext(default); }, StatusCode.PermissionDenied);
            using var call = packing.Download(request, Headers(method + "Download"), deadline: DateTime.UtcNow.AddMinutes(8));
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); long bytes = 0; var metadata = false; var ended = false;
            while (await call.ResponseStream.MoveNext(default))
            {
                var frame = call.ResponseStream.Current; Check(!ended);
                if (frame.Kind == "metadata") { Check(!metadata && bytes == 0 && frame.Metadata.SizeBytes == receipt.ArchiveBytes && frame.Metadata.Version == receipt.Version); metadata = true; }
                else if (frame.Kind == "chunk") { Check(metadata && frame.Offset == bytes && frame.Data.Length is > 0 and <= 32768); hash.AppendData(frame.Data.Span); bytes += frame.Data.Length; }
                else { Check(frame.Kind == "end" && metadata && frame.SizeBytes == bytes && frame.Sha256 == receipt.ArchiveSha256 && frame.Version == receipt.Version); ended = true; }
            }
            Check(metadata && ended && bytes == receipt.ArchiveBytes && Convert.ToHexString(hash.GetHashAndReset()) == receipt.ArchiveSha256);
        }
    }
    private static async Task Denied(Func<Task> action, StatusCode status)
    {
        try { await action(); }
        catch (RpcException error) when (error.StatusCode == status) { return; }
        throw new InvalidOperationException("PACKING_PROBE_REJECTION_MISMATCH");
    }
    private static void Check(bool value) { if (!value) throw new InvalidOperationException("PACKING_PROBE_CHECK_FAILED"); }
}
