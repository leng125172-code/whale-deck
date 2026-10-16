using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using WhaleDeck.Contracts.Agent.V1;

internal static class FileTransferProbe
{
    public static string Phase { get; private set; } = "startup";
    private const string Prefix = "/whaledeck.agent.v1.FileTransferService/";
    private const string Root = "whaledeck.files.shared";
    public static async Task RunAsync()
    {
        if (!OperatingSystem.IsLinux()) throw new InvalidOperationException("Linux required");
        var key = File.ReadAllBytes("/etc/whaledeck/agent-capability.key");
        var name = "wdv-file-" + Guid.NewGuid().ToString("N") + ".bin";
        var target = Path.Combine("/data/WhaleDeck/files", name);
        var payload = RandomNumberGenerator.GetBytes(2 * 1024 * 1024 + 7);
        var digest = Convert.ToHexString(SHA256.HashData(payload));
        var owner = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("files-native-" + name)));
        var owned = new List<string>();
        using var handler = new SocketsHttpHandler { UseProxy = false, ConnectCallback = async (_, cancellationToken) =>
        {
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try { await socket.ConnectAsync(new UnixDomainSocketEndPoint("/run/whaledeck/agent.sock"), cancellationToken); return new NetworkStream(socket, true); }
            catch { socket.Dispose(); throw; }
        } };
        using var channel = GrpcChannel.ForAddress("http://localhost", new() { HttpHandler = handler });
        var client = new FileTransferService.FileTransferServiceClient(channel);
        Metadata Headers(string method)
        {
            var prefix = DateTimeOffset.UtcNow.AddSeconds(45).ToUnixTimeSeconds() + "." + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            var token = prefix + "." + Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(prefix + "." + Prefix + method)));
            return new() { { "x-whaledeck-capability", token } };
        }
        async Task Denied(Func<Task> action, StatusCode status)
        {
            try { await action(); throw new InvalidOperationException("Rejected call succeeded"); }
            catch (RpcException error) when (error.StatusCode == status) { }
        }
        FileLocation Location() => new() { RootId = Root, Path = name };
        FileUploadHeader Header(byte[] bytes, string expected = "ABSENT")
        {
            var id = Guid.NewGuid().ToString("N"); owned.Add(id);
            return new() { Location = Location(), OperationId = id, Owner = owner, SizeBytes = bytes.Length, Sha256 = Convert.ToHexString(SHA256.HashData(bytes)),
                ExpectedVersion = expected, Context = new() { ActorSubject = "files-native-probe", TraceId = Guid.NewGuid().ToString("N") } };
        }
        async Task<FileUploadReceipt> Upload(FileUploadHeader header, byte[] bytes)
        {
            using var call = client.Upload(Headers("Upload"), deadline: DateTime.UtcNow.AddSeconds(60));
            await call.RequestStream.WriteAsync(new() { Header = header });
            for (var offset = 0; offset < bytes.Length; offset += 32768)
                await call.RequestStream.WriteAsync(new() { Offset = offset, Data = ByteString.CopyFrom(bytes, offset, Math.Min(32768, bytes.Length - offset)) });
            await call.RequestStream.WriteAsync(new() { Commit = true }); await call.RequestStream.CompleteAsync();
            return await call.ResponseAsync;
        }
        try
        {
            Phase = "capability-and-root-protection";
            var capabilities = await new AgentService.AgentServiceClient(channel).GetCapabilitiesAsync(new());
            Require(capabilities.ProtocolMajor == 1 && capabilities.ProtocolMinor >= 20 && capabilities.Capabilities.Contains("files.registered-streams"));
            await Denied(async () => await client.ListRootsAsync(new(), new Metadata()), StatusCode.PermissionDenied);
            await Denied(async () => await client.ListRootsAsync(new(), Headers("Inspect")), StatusCode.PermissionDenied);
            var used = Headers("ListRoots"); await client.ListRootsAsync(new(), used);
            await Denied(async () => await client.ListRootsAsync(new(), used), StatusCode.PermissionDenied);
            var roots = await client.ListRootsAsync(new(), Headers("ListRoots")); Require(roots.Roots.Any(item => item.Id == Root));
            await Denied(async () => await client.InspectAsync(new() { RootId = "database-platform.repository", Path = ".env" }, Headers("Inspect")), StatusCode.PermissionDenied);
            await Denied(async () => await client.InspectAsync(new() { RootId = Root, Path = ".env" }, Headers("Inspect")), StatusCode.PermissionDenied);
            await Denied(async () => await client.InspectAsync(new() { RootId = Root, Path = "../outside" }, Headers("Inspect")), StatusCode.InvalidArgument);

            Phase = "binary-stream-upload-download";
            var create = Header(payload); var result = await Upload(create, payload);
            Require(result.State == "Completed" && result.BindingCurrent && !result.Replayed && result.Sha256 == digest);
            var inspect = await client.InspectAsync(Location(), Headers("Inspect")); Require(inspect.SizeBytes == payload.Length && inspect.Version == result.Version);
            var listing = await client.ListAsync(new() { Location = new() { RootId = Root }, Take = 100 }, Headers("List")); Require(listing.Entries.Any(item => item.Name == name));
            using (var download = client.Download(new() { Location = Location(), ExpectedVersion = result.Version }, Headers("Download"), deadline: DateTime.UtcNow.AddSeconds(60)))
            {
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); long total = 0; var ended = false;
                while (await download.ResponseStream.MoveNext())
                {
                    var frame = download.ResponseStream.Current;
                    if (frame.Kind == "chunk") { Require(frame.Offset == total); hash.AppendData(frame.Data.Span); total += frame.Data.Length; }
                    if (frame.Kind == "end") { Require(frame.Sha256 == digest && frame.SizeBytes == payload.Length); ended = true; }
                }
                Require(ended && total == payload.Length && Convert.ToHexString(hash.GetHashAndReset()) == digest);
            }
            Require((await Upload(create, payload)).Replayed);
            await Denied(async () => await client.GetUploadStatusAsync(new() { OperationId = create.OperationId, Owner = new('B', 64) }, Headers("GetUploadStatus")), StatusCode.NotFound);

            Phase = "overwrite-concurrency-and-retention";
            var replacement = Encoding.UTF8.GetBytes("native-transfer-validation-only\n");
            var change = Header(replacement, result.Version); var changed = await Upload(change, replacement);
            Require(changed.State == "Completed" && changed.PreviousRetained);
            Require(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine("/data/WhaleDeck/files/.whaledeck-files", change.OperationId + ".upload")))) == digest);
            await Denied(async () => await Upload(Header(payload, result.Version), payload), StatusCode.Aborted);
            var replay = await Upload(create, payload); Require(replay.Replayed && !replay.BindingCurrent);
            Require(File.ReadAllBytes(target).SequenceEqual(replacement));

            Phase = "invalid-chunk-and-disconnect-cleanup";
            var invalid = Header(payload, changed.Version);
            using (var call = client.Upload(Headers("Upload"), deadline: DateTime.UtcNow.AddSeconds(20)))
            {
                await call.RequestStream.WriteAsync(new() { Header = invalid });
                await call.RequestStream.WriteAsync(new() { Offset = 1, Data = ByteString.CopyFromUtf8("bad") });
                await call.RequestStream.CompleteAsync();
                await Denied(async () => await call.ResponseAsync, StatusCode.InvalidArgument);
            }
            var disconnected = Header(payload, changed.Version);
            using (var call = client.Upload(Headers("Upload"), deadline: DateTime.UtcNow.AddSeconds(20)))
            {
                await call.RequestStream.WriteAsync(new() { Header = disconnected });
                await call.RequestStream.WriteAsync(new() { Data = ByteString.CopyFrom(payload, 0, 32768) });
                await Task.Delay(250);
            }
            await Task.Delay(500);
            Require(File.ReadAllBytes(target).SequenceEqual(replacement));
            foreach (var id in new[] { invalid.OperationId, disconnected.OperationId })
            {
                try { var state = await client.GetUploadStatusAsync(new() { OperationId = id, Owner = owner }, Headers("GetUploadStatus")); Require(state.State == "Aborted" && !state.IsInProgress); }
                catch (RpcException error) when (error.StatusCode == StatusCode.NotFound) { /* Cancellation before helper created a receipt is also no publication. */ }
            }
            Console.WriteLine(JsonSerializer.Serialize(new { nativeFilesAgent = "passed", binaryBytes = payload.Length, methodCapabilityReplay = "passed", protectedRootAndPath = "passed",
                uploadDownloadChecksum = "passed", overwritePreviousAndIdempotency = "passed", disconnectCleanup = "passed", bffAndUi = "not-implemented" }));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            if (File.Exists(target))
            {
                var actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(target)));
                var expectedReplacement = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("native-transfer-validation-only\n")));
                Require(target.StartsWith("/data/WhaleDeck/files/wdv-file-", StringComparison.Ordinal) && (actual == digest || actual == expectedReplacement));
                File.Delete(target);
            }
            foreach (var id in owned)
            {
                var previous = Path.Combine("/data/WhaleDeck/files/.whaledeck-files", id + ".upload");
                if (!File.Exists(previous)) continue;
                Require(id.Length == 32 && id.All(char.IsAsciiHexDigit));
                var actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(previous)));
                Require(actual == digest || actual == Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("native-transfer-validation-only\n"))));
                File.Delete(previous);
            }
            Console.WriteLine(JsonSerializer.Serialize(new { ownedNativeFilesRemoved = true, privateReceiptsRetained = true }));
        }
    }
    private static void Require(bool condition) { if (!condition) throw new InvalidOperationException("File transfer assertion failed"); }
}
