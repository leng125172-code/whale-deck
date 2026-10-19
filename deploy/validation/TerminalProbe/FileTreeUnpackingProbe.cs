using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using WhaleDeck.Contracts.Agent.V1;

/// <summary>Real installed UDS/helper probe; no direct data/Docker access or human BFF claim.</summary>
internal static class FileTreeUnpackingProbe
{
    public static string Phase { get; private set; } = "unpacking-startup";
    public static async Task RunAsync()
    {
        var prefix = Environment.GetEnvironmentVariable("WHALEDECK_UNPACK_PROBE_RELATIVE_PATH") ?? "";
        Check(Regex.IsMatch(prefix, "^wdv-agent-unpack-[a-f0-9]{16}$"));
        const string root = "whaledeck.files.shared";
        const string method = "/whaledeck.agent.v1.FileTreeUnpackingService/";
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
        var unpacking = new FileTreeUnpackingService.FileTreeUnpackingServiceClient(channel);
        var actor = new RequestContext { ActorSubject = "whaledeck-validation-agent-unpack", TraceId = Guid.NewGuid().ToString("N") };
        var owner = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var completed = new List<string>(); var canceled = new List<string>();
        try
        {
            Phase = "unpacking-capabilities-and-roots";
            var capabilities = await new AgentService.AgentServiceClient(channel).GetCapabilitiesAsync(new Empty());
            Check(capabilities.ProtocolMajor == 1 && capabilities.ProtocolMinor >= 31 && capabilities.Capabilities.Contains("files.tree-unpack-intents"));
            var roots = await files.ListRootsAsync(new(), Headers(fileMethod + "ListRoots"));
            Check(roots.Roots.Single(item => item.Id == root).Actions.Contains("files-tree-unpack"));
            foreach (var standard in new[] { "pax", "gnu" })
            foreach (var format in new[] { "tar", "tar.gz" })
            {
                Phase = "unpacking-" + standard + "-" + format.Replace('.', '-') + "-plan";
                var command = await Command(standard + "." + format, standard + "-" + format, format);
                var token = Headers(method + "Plan");
                var plan = await unpacking.PlanAsync(new() { Command = command, Context = actor }, token, deadline: DateTime.UtcNow.AddMinutes(5));
                Check(plan.Command.Equals(command) && plan.EntryCount == 4 && plan.TotalBytes == 2 * 1024 * 1024 + 7 && plan.ArchiveBytes > 0 && plan.ContentSha256.Length == 64);
                await Denied(async () => _ = await unpacking.PlanAsync(new() { Command = command, Context = actor }, token), StatusCode.PermissionDenied);
                var request = new FileTreeUnpackingApplyRequest { OperationId = Guid.NewGuid().ToString("N"), Owner = owner, Plan = plan, Context = actor };
                await Denied(async () => _ = await unpacking.ApplyAsync(request, Headers("/whaledeck.agent.v1.FileTreePackingService/Apply")), StatusCode.PermissionDenied);
                var bad = request.Clone(); bad.OperationId = Guid.NewGuid().ToString("N"); bad.Plan.Fingerprint = new('0', 64);
                await Denied(async () => _ = await unpacking.ApplyAsync(bad, Headers(method + "Apply")), StatusCode.FailedPrecondition);
                var stale = command.Clone(); stale.Tree.ExpectedVersion = new('A', 64);
                await Denied(async () => _ = await unpacking.PlanAsync(new() { Command = stale, Context = actor }, Headers(method + "Plan")), StatusCode.Aborted);
                Phase = "unpacking-" + standard + "-" + format.Replace('.', '-') + "-publish";
                var receipt = await unpacking.ApplyAsync(request, Headers(method + "Apply"), deadline: DateTime.UtcNow.AddMinutes(8));
                Check(receipt.State == "Completed" && !receipt.PartialRetained && receipt.Version.Length == 64 && receipt.Fingerprint == plan.Fingerprint &&
                    receipt.ArchiveBytes == plan.ArchiveBytes && receipt.ArchiveSha256 == plan.ArchiveSha256 && receipt.TotalBytes == plan.TotalBytes);
                completed.Add(request.OperationId);
                Check((await unpacking.InspectAsync(new() { OperationId = request.OperationId, Owner = owner }, Headers(method + "Inspect"))).Equals(receipt));
                Check((await unpacking.ApplyAsync(request, Headers(method + "Apply"))).Equals(receipt));
                await Denied(async () => _ = await unpacking.PlanAsync(new() { Command = command, Context = actor }, Headers(method + "Plan")), StatusCode.Aborted);
                await Denied(async () => _ = await unpacking.InspectAsync(new() { OperationId = request.OperationId, Owner = new('A', 64) }, Headers(method + "Inspect")), StatusCode.NotFound);
            }
            Phase = "unpacking-cancel-before-execution";
            var canceledPlan = await unpacking.PlanAsync(new() { Command = await Command("pax.tar", "canceled", "tar"), Context = actor }, Headers(method + "Plan"));
            var cancel = new FileTreeUnpackingApplyRequest { OperationId = Guid.NewGuid().ToString("N"), Owner = owner, Plan = canceledPlan, Context = actor };
            var discarded = await unpacking.DiscardAsync(cancel, Headers(method + "Discard"));
            Check(discarded.State == "Aborted" && !discarded.PartialRetained && discarded.ArchiveSha256 == canceledPlan.ArchiveSha256 && discarded.ArchiveBytes == canceledPlan.ArchiveBytes);
            Check((await unpacking.ApplyAsync(cancel, Headers(method + "Apply"))).Equals(discarded)); canceled.Add(cancel.OperationId);
            Phase = "unpacking-protected-and-malformed-members";
            await Denied(async () => _ = await unpacking.PlanAsync(new() { Command = await Command("protected.tar", "forbidden", "tar"), Context = actor }, Headers(method + "Plan")), StatusCode.PermissionDenied);
            await Denied(async () => _ = await unpacking.PlanAsync(new() { Command = await Command("traversal.tar", "traversal", "tar"), Context = actor }, Headers(method + "Plan")), StatusCode.FailedPrecondition);
            Phase = "unpacking-large-archive-and-full-materialization";
            var largePlan = await unpacking.PlanAsync(new() { Command = await Command("large.tar", "large-output", "tar"), Context = actor }, Headers(method + "Plan"), deadline: DateTime.UtcNow.AddMinutes(5));
            Check(largePlan.TotalBytes == 1024L * 1024 * 1024 + 7 && largePlan.ArchiveBytes > 1024L * 1024 * 1024 && largePlan.EntryCount == 3);
            var largeReceipt = await unpacking.ApplyAsync(new() { OperationId = Guid.NewGuid().ToString("N"), Owner = owner, Plan = largePlan, Context = actor }, Headers(method + "Apply"), deadline: DateTime.UtcNow.AddMinutes(8));
            Check(largeReceipt.State == "Completed" && largeReceipt.ArchiveSha256 == largePlan.ArchiveSha256 && largeReceipt.TotalBytes == largePlan.TotalBytes);
            completed.Add(largeReceipt.OperationId);
            Phase = "unpacking-final-membership";
            var listing = await files.ListAsync(new() { Location = new() { RootId = root, Path = prefix }, Take = 100 }, Headers(fileMethod + "List"));
            Check(!listing.Entries.Any(item => item.Name is "canceled" or "forbidden" or "traversal" or "escaped"));
            Console.WriteLine(JsonSerializer.Serialize(new { installedTreeUnpackingRpc = "passed", bothFormatsAndPaxGnu = true, smallBytes = 2 * 1024 * 1024 + 7,
                largeBytes = largePlan.TotalBytes, methodNonceOwnerVersionPlanAndCancelFences = true, inputProofRetainedInAllStates = true,
                protectedTraversalAndExistingTargetsRejected = true, completedOperationIds = completed, canceledOperationIds = canceled,
                independentDiskVerification = "required-by-observer", bffWorkerHumanAuditAndUi = "not-covered" }));
        }
        finally { CryptographicOperations.ZeroMemory(key); }
        Metadata Headers(string name)
        {
            var nonce = DateTimeOffset.UtcNow.AddSeconds(45).ToUnixTimeSeconds() + "." + Guid.NewGuid().ToString("N");
            return new() { { "x-whaledeck-capability", nonce + "." + Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(nonce + "." + name))) } };
        }
        async Task<FileTreeUnpackingCommand> Command(string source, string target, string format)
        {
            var location = new FileLocation { RootId = root, Path = prefix + "/" + source };
            var entry = await files.InspectAsync(location, Headers(fileMethod + "Inspect")); Check(entry.Kind == "File" && entry.Version.Length == 64);
            return new() { Tree = new() { Source = location, ExpectedVersion = entry.Version, Target = new() { RootId = root, Path = prefix + "/" + target } }, Format = format };
        }
    }
    private static async Task Denied(Func<Task> action, StatusCode status)
    {
        try { await action(); }
        catch (RpcException error) when (error.StatusCode == status) { return; }
        throw new InvalidOperationException("UNPACKING_PROBE_REJECTION_MISMATCH");
    }
    private static void Check(bool value) { if (!value) throw new InvalidOperationException("UNPACKING_PROBE_CHECK_FAILED"); }
}
