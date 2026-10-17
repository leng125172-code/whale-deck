using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using WhaleDeck.Contracts.Agent.V1;

/// <summary>Installed UDS/real fixed-helper probe, never a BFF or human-audit acceptance claim.</summary>
internal static class FileTreeCopyProbe
{
    public static string Phase { get; private set; } = "tree-startup";
    public static async Task RunAsync()
    {
        var prefix = Environment.GetEnvironmentVariable("WHALEDECK_TREE_PROBE_RELATIVE_PATH") ?? "";
        Check(Regex.IsMatch(prefix, "^wdv-agent-tree-[a-f0-9]{16}$"));
        const string root = "whaledeck.files.shared";
        var key = File.ReadAllBytes("/etc/whaledeck/agent-capability.key");
        using var handler = new SocketsHttpHandler { UseProxy = false, ConnectCallback = async (_, token) =>
        {
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try { await socket.ConnectAsync(new UnixDomainSocketEndPoint("/run/whaledeck/agent.sock"), token); return new NetworkStream(socket, true); }
            catch { socket.Dispose(); throw; }
        } };
        using var channel = GrpcChannel.ForAddress("http://localhost", new() { HttpHandler = handler });
        var files = new FileTransferService.FileTransferServiceClient(channel); var trees = new FileTreeCopyService.FileTreeCopyServiceClient(channel);
        var context = new RequestContext { ActorSubject = "whaledeck-validation-agent-tree", TraceId = Guid.NewGuid().ToString("N") };
        var owner = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var completed = new List<string>(); var canceled = new List<string>();
        const string treeService = "/whaledeck.agent.v1.FileTreeCopyService/";
        const string fileService = "/whaledeck.agent.v1.FileTransferService/";
        try
        {
            Phase = "tree-capabilities";
            var capabilities = await new AgentService.AgentServiceClient(channel).GetCapabilitiesAsync(new Empty());
            Check(capabilities.ProtocolMajor == 1 && capabilities.ProtocolMinor >= 27 && capabilities.Capabilities.Contains("files.tree-copy-intents"));
            var roots = await files.ListRootsAsync(new(), Headers(fileService + "ListRoots"), deadline: DateTime.UtcNow.AddSeconds(20));
            Check(roots.Roots.Single(item => item.Id == root).Actions.Contains("files-tree-copy"));

            Phase = "tree-plan";
            var command = await Command("source", "copied");
            var reused = Headers(treeService + "Plan");
            var planned = await trees.PlanAsync(new() { Command = command, Context = context }, reused, deadline: DateTime.UtcNow.AddMinutes(5));
            Check(planned.Command.Equals(command) && planned.EntryCount == 4 && planned.TotalBytes == 2 * 1024 * 1024 + 7);
            Phase = "tree-plan-replay-rejection";
            await Denied(async () => _ = await trees.PlanAsync(new() { Command = command, Context = context }, reused), StatusCode.PermissionDenied);
            Phase = "tree-stale-version-rejection";
            var stale = command.Clone(); stale.ExpectedVersion = new('A', 64);
            await Denied(async () => _ = await trees.PlanAsync(new() { Command = stale, Context = context }, Headers(treeService + "Plan")), StatusCode.Aborted);

            var request = new FileTreeCopyApplyRequest { OperationId = Guid.NewGuid().ToString("N"), Owner = owner, Plan = planned, Context = context };
            Phase = "tree-method-bound-rejection";
            await Denied(async () => _ = await trees.ApplyAsync(request, Headers(treeService + "Plan")), StatusCode.PermissionDenied);
            Phase = "tree-plan-tamper-rejection";
            var changed = request.Clone(); changed.OperationId = Guid.NewGuid().ToString("N"); changed.Plan.Fingerprint = new('0', 64);
            await Denied(async () => _ = await trees.ApplyAsync(changed, Headers(treeService + "Apply")), StatusCode.FailedPrecondition);
            Phase = "tree-copy";
            var receipt = await trees.ApplyAsync(request, Headers(treeService + "Apply"), deadline: DateTime.UtcNow.AddMinutes(8));
            Check(receipt.State == "Completed" && receipt.Fingerprint == planned.Fingerprint && receipt.EntryCount == 4 && !receipt.PartialRetained);
            completed.Add(request.OperationId);
            var lookup = await trees.InspectAsync(new() { OperationId = request.OperationId, Owner = owner }, Headers(treeService + "Inspect"));
            Check(lookup.Equals(receipt));
            var replay = await trees.ApplyAsync(request, Headers(treeService + "Apply")); Check(replay.Equals(receipt));
            await Denied(async () => _ = await trees.InspectAsync(new() { OperationId = request.OperationId, Owner = new('A', 64) }, Headers(treeService + "Inspect")), StatusCode.NotFound);
            await Denied(async () => _ = await trees.PlanAsync(new() { Command = command, Context = context }, Headers(treeService + "Plan")), StatusCode.Aborted);
            Check(await Download("source/代码/原始.bin") == await Download("copied/代码/原始.bin"));

            Phase = "tree-cancel-fence";
            var cancelCommand = await Command("source", "canceled");
            var cancelPlan = await trees.PlanAsync(new() { Command = cancelCommand, Context = context }, Headers(treeService + "Plan"));
            var cancel = new FileTreeCopyApplyRequest { OperationId = Guid.NewGuid().ToString("N"), Owner = owner, Plan = cancelPlan, Context = context };
            var discarded = await trees.DiscardAsync(cancel, Headers(treeService + "Discard")); Check(discarded.State == "Aborted" && !discarded.PartialRetained);
            var late = await trees.ApplyAsync(cancel, Headers(treeService + "Apply")); Check(late.Equals(discarded)); canceled.Add(cancel.OperationId);

            Phase = "tree-protected";
            var protectedCommand = await Command("protected", "protected-copy");
            await Denied(async () => _ = await trees.PlanAsync(new() { Command = protectedCommand, Context = context }, Headers(treeService + "Plan")), StatusCode.PermissionDenied);

            long largeBytes = 0;
            if (Environment.GetEnvironmentVariable("WHALEDECK_TREE_PROBE_LARGE") == "1")
            {
                Phase = "tree-large-source-plan";
                var largeCommand = await Command("large-source", "large-copy");
                var largePlan = await trees.PlanAsync(new() { Command = largeCommand, Context = context }, Headers(treeService + "Plan"), deadline: DateTime.UtcNow.AddMinutes(5));
                Check(largePlan.TotalBytes == 1024L * 1024 * 1024 + 7 && largePlan.EntryCount == 3);
                Phase = "tree-large-copy";
                var large = await trees.ApplyAsync(new() { OperationId = Guid.NewGuid().ToString("N"), Owner = owner, Plan = largePlan, Context = context },
                    Headers(treeService + "Apply"), deadline: DateTime.UtcNow.AddMinutes(8));
                Check(large.State == "Completed" && large.TotalBytes == largePlan.TotalBytes && !large.PartialRetained);
                completed.Add(large.OperationId); largeBytes = large.TotalBytes;
            }
            Phase = "tree-final-membership";
            var contents = await files.ListAsync(new() { Location = new() { RootId = root, Path = prefix }, Take = 100 }, Headers(fileService + "List"));
            Check(contents.Entries.Any(item => item.Name == "copied") && !contents.Entries.Any(item => item.Name is "canceled" or "protected-copy"));
            Console.WriteLine(JsonSerializer.Serialize(new { installedTreeCopyRpc = "passed", smallBytes = planned.TotalBytes, largeBytes,
                methodNonceOwnerVersionAndCancelFence = true, protectedAndExistingTargetsRejected = true,
                smallDownloadChecksumMatched = true, completedOperationIds = completed, canceledOperationIds = canceled,
                bffWorkerHumanAuditAndUi = "not-covered" }));
        }
        finally { CryptographicOperations.ZeroMemory(key); }

        Metadata Headers(string method)
        {
            var nonce = DateTimeOffset.UtcNow.AddSeconds(45).ToUnixTimeSeconds() + "." + Guid.NewGuid().ToString("N");
            return new() { { "x-whaledeck-capability", nonce + "." + Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(nonce + "." + method))) } };
        }
        async Task<FileTreeCopyCommand> Command(string source, string target)
        {
            var location = new FileLocation { RootId = root, Path = prefix + "/" + source };
            var page = await files.ListAsync(new() { Location = location, Take = 1 }, Headers(fileService + "List"), deadline: DateTime.UtcNow.AddSeconds(20));
            return new() { Source = location, ExpectedVersion = page.Version, Target = new() { RootId = root, Path = prefix + "/" + target } };
        }
        async Task<string> Download(string path)
        {
            using var download = files.Download(new() { Location = new() { RootId = root, Path = prefix + "/" + path } }, Headers(fileService + "Download"), deadline: DateTime.UtcNow.AddMinutes(1));
            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); long bytes = 0; string? reported = null;
            while (await download.ResponseStream.MoveNext(default))
            {
                var frame = download.ResponseStream.Current;
                if (frame.Kind == "chunk") { Check(frame.Offset == bytes); digest.AppendData(frame.Data.Span); bytes += frame.Data.Length; }
                else if (frame.Kind == "end") { Check(frame.SizeBytes == bytes); reported = frame.Sha256; }
                else Check(frame.Kind == "metadata");
            }
            var hash = Convert.ToHexString(digest.GetHashAndReset()); Check(reported == hash && bytes == 2 * 1024 * 1024 + 7); return hash;
        }
    }
    private static async Task Denied(Func<Task> action, StatusCode code)
    {
        try { await action(); }
        catch (RpcException error) when (error.StatusCode == code) { return; }
        throw new InvalidOperationException("TREE_PROBE_REJECTION_MISMATCH");
    }
    private static void Check(bool condition) { if (!condition) throw new InvalidOperationException("TREE_PROBE_CHECK_FAILED"); }
}
