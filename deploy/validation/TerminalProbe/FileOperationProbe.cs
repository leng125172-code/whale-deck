using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using WhaleDeck.Contracts.Agent.V1;

internal static class FileOperationProbe
{
    public static string Phase { get; private set; } = "startup";
    private const string Root = "whaledeck.files.shared";
    private const string Prefix = "/whaledeck.agent.v1.FileOperationService/";
    private const string Files = "/whaledeck.agent.v1.FileTransferService/";
    public static async Task RunAsync()
    {
        if (!OperatingSystem.IsLinux()) throw new InvalidOperationException("Linux required");
        var key = File.ReadAllBytes("/etc/whaledeck/agent-capability.key");
        var marker = "wdv-file-operation-" + Guid.NewGuid().ToString("N");
        var sourceName = marker + ".source"; var copyName = marker + ".copy"; var movedName = marker + ".moved"; var directoryName = marker + ".dir";
        var payload = RandomNumberGenerator.GetBytes(65543); var changed = Encoding.UTF8.GetBytes("owned-operation-stale-version-fixture\n");
        var uploadId = Guid.NewGuid().ToString("N"); var uploaded = false;
        var owner = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(marker)));
        var ownedFiles = new[] { sourceName, copyName, movedName }; var operationIds = new List<string>();
        using var handler = new SocketsHttpHandler { UseProxy = false, ConnectCallback = async (_, cancellationToken) =>
        {
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try { await socket.ConnectAsync(new UnixDomainSocketEndPoint("/run/whaledeck/agent.sock"), cancellationToken); return new NetworkStream(socket, true); }
            catch { socket.Dispose(); throw; }
        } };
        using var channel = GrpcChannel.ForAddress("http://localhost", new() { HttpHandler = handler });
        var client = new FileOperationService.FileOperationServiceClient(channel);
        var files = new FileTransferService.FileTransferServiceClient(channel);
        Metadata Headers(string method)
        {
            var prefix = DateTimeOffset.UtcNow.AddSeconds(45).ToUnixTimeSeconds() + "." + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            return new() { { "x-whaledeck-capability", prefix + "." + Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(prefix + "." + method))) } };
        }
        async Task Denied(Func<Task> action, StatusCode expected)
        {
            try { await action(); throw new InvalidOperationException("Rejected operation succeeded"); }
            catch (RpcException error) when (error.StatusCode == expected) { }
        }
        FileOperationPlanRequest PlanRequest(FileOperationKind kind, string source = "", string target = "", string version = "ABSENT", string mode = "") => new()
        {
            Command = new() { Kind = kind, Source = kind == FileOperationKind.CreateDirectory ? null : new() { RootId = Root, Path = source },
                Target = kind == FileOperationKind.SetPermissions ? null : new() { RootId = Root, Path = target }, ExpectedVersion = version, Mode = mode },
            Context = new() { ActorSubject = "file-operations-native-probe" }
        };
        FileOperationApplyRequest ApplyRequest(FileOperationPlan plan)
        {
            var id = Guid.NewGuid().ToString("N"); operationIds.Add(id);
            return new() { OperationId = id, Owner = owner, Plan = plan, Context = new() { ActorSubject = "file-operations-native-probe" } };
        }
        try
        {
            Phase = "capability-methods-and-protection";
            var capability = await new AgentService.AgentServiceClient(channel).GetCapabilitiesAsync(new());
            Require(capability.ProtocolMajor == 1 && capability.ProtocolMinor >= 23 && capability.Capabilities.Contains("files.mutation-tombstones"));
            var directory = PlanRequest(FileOperationKind.CreateDirectory, target: directoryName, mode: "0750");
            await Denied(async () => await client.PlanAsync(directory, new Metadata()), StatusCode.PermissionDenied);
            await Denied(async () => await client.PlanAsync(directory, Headers(Prefix + "Apply")), StatusCode.PermissionDenied);
            var replay = Headers(Prefix + "Plan"); var directoryPlan = await client.PlanAsync(directory, replay, deadline: DateTime.UtcNow.AddSeconds(30));
            await Denied(async () => await client.PlanAsync(directory, replay), StatusCode.PermissionDenied);
            var protectedTarget = directory.Clone(); protectedTarget.Command.Target.Path = ".env";
            await Denied(async () => await client.PlanAsync(protectedTarget, Headers(Prefix + "Plan")), StatusCode.PermissionDenied);
            protectedTarget.Command.Target.RootId = "database-platform.repository";
            await Denied(async () => await client.PlanAsync(protectedTarget, Headers(Prefix + "Plan")), StatusCode.PermissionDenied);
            var traversal = directory.Clone(); traversal.Command.Target.Path = "../escape";
            await Denied(async () => await client.PlanAsync(traversal, Headers(Prefix + "Plan")), StatusCode.InvalidArgument);

            Phase = "directory-and-upload-fixture";
            Require(!Directory.Exists(Path.Combine("/data/WhaleDeck/files", directoryName)));
            var canceled = ApplyRequest(directoryPlan);
            await Denied(async () => await client.DiscardAsync(canceled, Headers(Prefix + "Apply")), StatusCode.PermissionDenied);
            Require((await client.DiscardAsync(canceled, Headers(Prefix + "Discard"))).State == "Aborted");
            Require((await client.ApplyAsync(canceled, Headers(Prefix + "Apply"))).State == "Aborted");
            Require(!Directory.Exists(Path.Combine("/data/WhaleDeck/files", directoryName)));
            var mkdir = ApplyRequest(directoryPlan);
            Require((await client.ApplyAsync(mkdir, Headers(Prefix + "Apply"), deadline: DateTime.UtcNow.AddSeconds(30))).State == "Completed");
            Require((await client.ApplyAsync(mkdir, Headers(Prefix + "Apply"))).State == "Completed");
            Require(Directory.Exists(Path.Combine("/data/WhaleDeck/files", directoryName)));
            Require((await client.DiscardAsync(mkdir, Headers(Prefix + "Discard"))).State == "Completed");
            Require((await client.ApplyAsync(canceled, Headers(Prefix + "Apply"))).State == "Aborted");
            Require((File.GetUnixFileMode(Path.Combine("/data/WhaleDeck/files", directoryName)) & (UnixFileMode)0xFFF) == (UnixFileMode)0x1E8);
            using (var upload = files.Upload(Headers(Files + "Upload"), deadline: DateTime.UtcNow.AddSeconds(60)))
            {
                await upload.RequestStream.WriteAsync(new() { Header = new() { Location = new() { RootId = Root, Path = sourceName }, OperationId = uploadId, Owner = owner,
                    SizeBytes = payload.Length, Sha256 = Convert.ToHexString(SHA256.HashData(payload)), ExpectedVersion = "ABSENT", Context = new() { ActorSubject = "file-operations-native-probe" } } });
                for (var offset = 0; offset < payload.Length; offset += 32768)
                    await upload.RequestStream.WriteAsync(new() { Offset = offset, Data = ByteString.CopyFrom(payload, offset, Math.Min(32768, payload.Length - offset)) });
                await upload.RequestStream.WriteAsync(new() { Commit = true }); await upload.RequestStream.CompleteAsync();
                Require((await upload.ResponseAsync).State == "Completed");
                uploaded = true;
            }
            Phase = "copy-permissions-move-and-historical-replay";
            var metadata = await files.InspectAsync(new() { RootId = Root, Path = sourceName }, Headers(Files + "Inspect"));
            var copy = ApplyRequest(await client.PlanAsync(PlanRequest(FileOperationKind.CopyFile, sourceName, copyName, metadata.Version), Headers(Prefix + "Plan")));
            var copied = await client.ApplyAsync(copy, Headers(Prefix + "Apply"), deadline: DateTime.UtcNow.AddSeconds(60));
            Require(copied.State == "Completed" && copied.BindingCurrent && copied.SizeBytes == payload.Length);
            Require(File.ReadAllBytes(Path.Combine("/data/WhaleDeck/files", copyName)).SequenceEqual(payload));
            var chmod = ApplyRequest(await client.PlanAsync(PlanRequest(FileOperationKind.SetPermissions, copyName, version: copied.Version, mode: "0600"), Headers(Prefix + "Plan")));
            var changedMode = await client.ApplyAsync(chmod, Headers(Prefix + "Apply")); Require(changedMode.State == "Completed");
            Require(File.GetUnixFileMode(Path.Combine("/data/WhaleDeck/files", copyName)) == (UnixFileMode)0x180);
            var move = ApplyRequest(await client.PlanAsync(PlanRequest(FileOperationKind.MoveFile, copyName, movedName, changedMode.Version), Headers(Prefix + "Plan")));
            var moved = await client.ApplyAsync(move, Headers(Prefix + "Apply"), deadline: DateTime.UtcNow.AddSeconds(60));
            Require(moved.State == "Completed" && moved.SourceRetained && !File.Exists(Path.Combine("/data/WhaleDeck/files", copyName)));
            Require(File.ReadAllBytes(Path.Combine("/data/WhaleDeck/files", movedName)).SequenceEqual(payload));
            Require(File.ReadAllBytes(Path.Combine("/data/WhaleDeck/files/.whaledeck-files", move.OperationId + ".mutation")).SequenceEqual(payload));
            Require(!(await client.ApplyAsync(copy, Headers(Prefix + "Apply"))).BindingCurrent);
            Require(!File.Exists(Path.Combine("/data/WhaleDeck/files", copyName)));
            await Denied(async () => await client.InspectAsync(new() { OperationId = move.OperationId, Owner = new('F', 64) }, Headers(Prefix + "Inspect")), StatusCode.NotFound);
            Require((await client.InspectAsync(new() { OperationId = move.OperationId, Owner = owner }, Headers(Prefix + "Inspect"))).State == "Completed");

            Phase = "stale-plan-rejection";
            var stale = ApplyRequest(await client.PlanAsync(PlanRequest(FileOperationKind.CopyFile, sourceName, marker + ".stale", metadata.Version), Headers(Prefix + "Plan")));
            File.WriteAllBytes(Path.Combine("/data/WhaleDeck/files", sourceName), changed);
            await Denied(async () => await client.ApplyAsync(stale, Headers(Prefix + "Apply")), StatusCode.Aborted);
            Require(!File.Exists(Path.Combine("/data/WhaleDeck/files", marker + ".stale")));
            Require((await client.DiscardAsync(stale, Headers(Prefix + "Discard"))).State == "Aborted");
            Require((await client.ApplyAsync(stale, Headers(Prefix + "Apply"))).State == "Aborted");
            Console.WriteLine(JsonSerializer.Serialize(new { installedFileOperationsAgent = "passed", protocolMinor = capability.ProtocolMinor,
                methodCapabilityReplayAndProtection = "passed", directoryCopyMovePermissions = "passed", ownerStaleVersionAndHistoricalReplay = "passed",
                fullSourceRetentionChecksum = "passed", durableCancelAndDelayedApply = "passed", bffAndUi = "not-exercised-by-agent-probe" }));
        }
        finally
        {
            try
            {
                if (!uploaded)
                {
                    try
                    {
                        var uploadStatus = await files.GetUploadStatusAsync(new() { OperationId = uploadId, Owner = owner }, Headers(Files + "GetUploadStatus"), deadline: DateTime.UtcNow.AddSeconds(20));
                        Require(uploadStatus.State is "Aborted" or "Completed" && !uploadStatus.IsInProgress);
                    }
                    catch (RpcException error) when (error.StatusCode == StatusCode.NotFound) { }
                }
                foreach (var id in operationIds)
                {
                    try
                    {
                        var status = await client.InspectAsync(new() { OperationId = id, Owner = owner }, Headers(Prefix + "Inspect"), deadline: DateTime.UtcNow.AddSeconds(20));
                        Require(status.State is "Completed" or "Aborted");
                    }
                    catch (RpcException error) when (error.StatusCode == StatusCode.NotFound) { }
                }
            }
            finally { CryptographicOperations.ZeroMemory(key); }
            foreach (var name in ownedFiles)
            {
                var path = Path.Combine("/data/WhaleDeck/files", name);
                if (!File.Exists(path)) continue;
                Require(name.StartsWith(marker, StringComparison.Ordinal) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0);
                var bytes = File.ReadAllBytes(path); Require(bytes.SequenceEqual(payload) || bytes.SequenceEqual(changed)); File.Delete(path);
            }
            foreach (var id in operationIds)
            {
                var path = Path.Combine("/data/WhaleDeck/files/.whaledeck-files", id + ".mutation");
                if (!File.Exists(path)) continue;
                Require((File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0 && File.ReadAllBytes(path).SequenceEqual(payload)); File.Delete(path);
            }
            var folder = Path.Combine("/data/WhaleDeck/files", directoryName);
            if (Directory.Exists(folder))
            {
                Require((File.GetAttributes(folder) & FileAttributes.ReparsePoint) == 0 && !Directory.EnumerateFileSystemEntries(folder).Any()); Directory.Delete(folder, false);
            }
            Console.WriteLine(JsonSerializer.Serialize(new { ownedOperationFixturesRemoved = true, privateReceiptsRetained = true }));
        }
    }
    private static void Require(bool condition) { if (!condition) throw new InvalidOperationException("File operation assertion failed"); }
}
