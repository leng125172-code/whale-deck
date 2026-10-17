using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using WhaleDeck.Contracts.Agent.V1;

/// <summary>Installed signed UDS probe only; no BFF, human audit or UI acceptance claim.</summary>
internal static class FileTreeMoveProbe
{
    public static string Phase { get; private set; } = "tree-move-startup";
    public static async Task RunAsync()
    {
        var prefix = Environment.GetEnvironmentVariable("WHALEDECK_TREE_MOVE_PROBE_RELATIVE_PATH") ?? "";
        Check(Regex.IsMatch(prefix, "^wdv-agent-move-[a-f0-9]{16}$"));
        const string root = "whaledeck.files.shared";
        var key = File.ReadAllBytes("/etc/whaledeck/agent-capability.key");
        using var handler = new SocketsHttpHandler { UseProxy = false, ConnectCallback = async (_, token) =>
        {
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try { await socket.ConnectAsync(new UnixDomainSocketEndPoint("/run/whaledeck/agent.sock"), token); return new NetworkStream(socket, true); }
            catch { socket.Dispose(); throw; }
        } };
        using var channel = GrpcChannel.ForAddress("http://localhost", new() { HttpHandler = handler });
        var files = new FileTransferService.FileTransferServiceClient(channel); var trees = new FileTreeMoveService.FileTreeMoveServiceClient(channel);
        var context = new RequestContext { ActorSubject = "whaledeck-validation-agent-move", TraceId = Guid.NewGuid().ToString("N") };
        var owner = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        const string service = "/whaledeck.agent.v1.FileTreeMoveService/";
        const string fileService = "/whaledeck.agent.v1.FileTransferService/";
        try
        {
            Phase = "tree-move-capabilities";
            var capabilities = await new AgentService.AgentServiceClient(channel).GetCapabilitiesAsync(new Empty());
            Check(capabilities.ProtocolMajor == 1 && capabilities.ProtocolMinor >= 28 && capabilities.Capabilities.Contains("files.tree-move-intents"));
            var roots = await files.ListRootsAsync(new(), Headers(fileService + "ListRoots"), deadline: DateTime.UtcNow.AddSeconds(20));
            Check(roots.Roots.Single(item => item.Id == root).Actions.Contains("files-tree-move"));
            var originalHash = await Download("source/代码/原始.bin");
            var command = await Command("source", "moved");
            Phase = "tree-move-missing-and-wrong-method";
            await Denied(async () => _ = await trees.PlanAsync(new() { Command = command, Context = context }), StatusCode.PermissionDenied);
            await Denied(async () => _ = await trees.PlanAsync(new() { Command = command, Context = context }, Headers("/whaledeck.agent.v1.FileTreeCopyService/Plan")), StatusCode.PermissionDenied);
            Phase = "tree-move-plan";
            var reused = Headers(service + "Plan");
            var plan = await trees.PlanAsync(new() { Command = command, Context = context }, reused, deadline: DateTime.UtcNow.AddMinutes(5));
            Check(plan.CopyPlan.Command.Equals(command) && plan.CopyPlan.EntryCount == 4 && plan.CopyPlan.TotalBytes == 2 * 1024 * 1024 + 7 && plan.SourceParent.Inode != 0);
            await Denied(async () => _ = await trees.PlanAsync(new() { Command = command, Context = context }, reused), StatusCode.PermissionDenied);
            var stale = command.Clone(); stale.ExpectedVersion = new('A', 64);
            await Denied(async () => _ = await trees.PlanAsync(new() { Command = stale, Context = context }, Headers(service + "Plan")), StatusCode.Aborted);
            var request = new FileTreeMoveApplyRequest { OperationId = Guid.NewGuid().ToString("N"), Owner = owner, Plan = plan, Context = context };
            Phase = "tree-move-original-plan-binding";
            await Denied(async () => _ = await trees.ApplyAsync(request, Headers(service + "Plan")), StatusCode.PermissionDenied);
            var tampered = request.Clone(); tampered.OperationId = Guid.NewGuid().ToString("N"); tampered.Plan.Fingerprint = new('0', 64);
            await Denied(async () => _ = await trees.ApplyAsync(tampered, Headers(service + "Apply")), StatusCode.FailedPrecondition);
            Phase = "tree-move-apply";
            var small = await trees.ApplyAsync(request, Headers(service + "Apply"), deadline: DateTime.UtcNow.AddMinutes(8));
            Complete(small, plan);
            Check((await trees.InspectAsync(new() { OperationId = request.OperationId, Owner = owner }, Headers(service + "Inspect"))).Equals(small));
            Check((await trees.ApplyAsync(request, Headers(service + "Apply"))).Equals(small));
            await Denied(async () => _ = await trees.InspectAsync(new() { OperationId = request.OperationId, Owner = new('A', 64) }, Headers(service + "Inspect")), StatusCode.NotFound);
            Phase = "tree-move-independent-download";
            Check(await Download("moved/代码/原始.bin") == originalHash);

            Phase = "tree-move-cancel-fence";
            var cancelCommand = await Command("cancel-source", "canceled");
            var cancelPlan = await trees.PlanAsync(new() { Command = cancelCommand, Context = context }, Headers(service + "Plan"));
            var cancel = new FileTreeMoveApplyRequest { OperationId = Guid.NewGuid().ToString("N"), Owner = owner, Plan = cancelPlan, Context = context };
            var discarded = await trees.DiscardAsync(cancel, Headers(service + "Discard"));
            Check(discarded.State == "Aborted" && !discarded.DestinationPublished && !discarded.SourceRemoved && !discarded.PartialRetained);
            Check((await trees.ApplyAsync(cancel, Headers(service + "Apply"))).Equals(discarded));
            var protectedCommand = await Command("protected", "protected-move");
            await Denied(async () => _ = await trees.PlanAsync(new() { Command = protectedCommand, Context = context }, Headers(service + "Plan")), StatusCode.PermissionDenied);
            var existingTarget = await Command("cancel-source", "moved");
            await Denied(async () => _ = await trees.PlanAsync(new() { Command = existingTarget, Context = context }, Headers(service + "Plan")), StatusCode.Aborted);

            Phase = "tree-move-large-plan";
            var largeCommand = await Command("large-source", "large-moved");
            var largePlan = await trees.PlanAsync(new() { Command = largeCommand, Context = context }, Headers(service + "Plan"), deadline: DateTime.UtcNow.AddMinutes(5));
            Check(largePlan.CopyPlan.TotalBytes == 1024L * 1024 * 1024 + 7 && largePlan.CopyPlan.EntryCount == 3);
            Phase = "tree-move-large-apply";
            var large = await trees.ApplyAsync(new() { OperationId = Guid.NewGuid().ToString("N"), Owner = owner, Plan = largePlan, Context = context }, Headers(service + "Apply"), deadline: DateTime.UtcNow.AddMinutes(8));
            Complete(large, largePlan);
            var contents = await files.ListAsync(new() { Location = new() { RootId = root, Path = prefix }, Take = 100 }, Headers(fileService + "List"));
            Check(contents.Entries.Any(item => item.Name == "moved") && contents.Entries.Any(item => item.Name == "large-moved") && contents.Entries.Any(item => item.Name == "cancel-source"));
            Check(!contents.Entries.Any(item => item.Name is "source" or "large-source" or "canceled" or "protected-move"));
            Console.WriteLine(JsonSerializer.Serialize(new { installedTreeMoveRpc = "passed", smallBytes = small.TotalBytes, largeBytes = large.TotalBytes,
                completedOperationIds = new[] { small.OperationId, large.OperationId }, canceledOperationId = cancel.OperationId,
                methodNonceOwnerVersionPlanAndCancelFence = true, smallDownloadChecksumMatched = true, sourceLeavesRetainedNotHddBackup = true,
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
            var listed = await files.ListAsync(new() { Location = location, Take = 1 }, Headers(fileService + "List"));
            return new() { Source = location, ExpectedVersion = listed.Version, Target = new() { RootId = root, Path = prefix + "/" + target } };
        }
        async Task<string> Download(string path)
        {
            var location = new FileLocation { RootId = root, Path = prefix + "/" + path }; var entry = await files.InspectAsync(location, Headers(fileService + "Inspect"));
            Check(entry.Kind == "File" && entry.SizeBytes == 2 * 1024 * 1024 + 7 && !entry.IsProtected);
            using var call = files.Download(new() { Location = location, ExpectedVersion = entry.Version }, Headers(fileService + "Download"), deadline: DateTime.UtcNow.AddMinutes(1));
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); long bytes = 0; string? reported = null;
            while (await call.ResponseStream.MoveNext(default))
            {
                var row = call.ResponseStream.Current;
                if (row.Kind == "chunk") { Check(row.Offset == bytes); hash.AppendData(row.Data.Span); bytes += row.Data.Length; }
                else if (row.Kind == "end") { Check(row.SizeBytes == bytes); reported = row.Sha256; }
                else Check(row.Kind == "metadata");
            }
            var digest = Convert.ToHexString(hash.GetHashAndReset()); Check(reported == digest && bytes == entry.SizeBytes); return digest;
        }
    }
    private static void Complete(FileTreeMoveReceipt value, FileTreeMovePlan plan) => Check(value.State == "Completed" && value.Fingerprint == plan.Fingerprint && value.EntryCount == plan.CopyPlan.EntryCount &&
        value.TotalBytes == plan.CopyPlan.TotalBytes && value.DestinationPublished && value.SourceRemoved && value.SourceRetained && !value.PartialRetained);
    private static async Task Denied(Func<Task> action, StatusCode code)
    {
        try { await action(); }
        catch (RpcException error) when (error.StatusCode == code) { return; }
        throw new InvalidOperationException("TREE_MOVE_PROBE_REJECTION_MISMATCH");
    }
    private static void Check(bool valid) { if (!valid) throw new InvalidOperationException("TREE_MOVE_PROBE_CHECK_FAILED"); }
}
