using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Grpc.Core;
using Grpc.Net.Client;
using WhaleDeck.Contracts.Agent.V1;

/// <summary>Consumes only the native validator's synthetic fixture; never creates or deletes containers.</summary>
internal static class ContainerFileReadProbe
{
    private const string Prefix = "/whaledeck.agent.v1.ContainerFileService/";
    public static string Phase { get; private set; } = "startup";

    public static async Task RunAsync()
    {
        Require(OperatingSystem.IsLinux());
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(150));
        var input = new char[4097]; var count = 0;
        while (count < input.Length)
        {
            var read = await Console.In.ReadAsync(input.AsMemory(count), deadline.Token);
            if (read == 0) break;
            count += read;
        }
        Require(count is > 0 and <= 4096);
        using var request = JsonDocument.Parse(new string(input, 0, count));
        var fixture = request.RootElement;
        var revision = fixture.GetProperty("revision").GetString()!;
        Require(revision.Length == 40 && revision.All(char.IsAsciiHexDigitLower));
        Require(Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion.EndsWith("+" + revision, StringComparison.Ordinal));
        var identifier = fixture.GetProperty("containerId").GetString()!;
        var core = fixture.GetProperty("coreId").GetString()!;
        Require(identifier.Length == 64 && identifier.All(char.IsAsciiHexDigitLower) && core.Length == 64 && core.All(char.IsAsciiHexDigitLower));
        var instance = fixture.GetProperty("instanceId").GetString()!;
        var version = fixture.GetProperty("version").GetString()!;
        var digest = fixture.GetProperty("sha256").GetString()!;
        var size = fixture.GetProperty("sizeBytes").GetInt64();
        Require(size == 2 * 1024 * 1024 + 7 && version.Length == 64 && digest.Length == 64);
        var key = await File.ReadAllBytesAsync("/etc/whaledeck/agent-capability.key", deadline.Token);
        try
        {
            using var handler = new SocketsHttpHandler { UseProxy = false, ConnectCallback = async (_, token) =>
            {
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                try { await socket.ConnectAsync(new UnixDomainSocketEndPoint("/run/whaledeck/agent.sock"), token); return new NetworkStream(socket, true); }
                catch { socket.Dispose(); throw; }
            } };
            using var channel = GrpcChannel.ForAddress("http://localhost", new() { HttpHandler = handler });
            var client = new ContainerFileService.ContainerFileServiceClient(channel);
            Metadata Headers(string method, bool otherService = false)
            {
                var nonce = DateTimeOffset.UtcNow.AddSeconds(45).ToUnixTimeSeconds() + "." + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
                var path = otherService ? "/whaledeck.agent.v1.FileTransferService/" + method : Prefix + method;
                return new() { { "x-whaledeck-capability", nonce + "." + Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(nonce + "." + path))) } };
            }
            ContainerFileLocation Location(string path = "work/中文.bin") => new() { ContainerId = identifier, InstanceId = instance, Path = path };
            ContainerFileListRequest ListRequest() => new() { Location = Location("work"), Take = 100 };
            DateTime Until() => DateTime.UtcNow.AddSeconds(30);
            Phase = "capabilities-and-method-binding";
            var capabilities = await new AgentService.AgentServiceClient(channel).GetCapabilitiesAsync(new(), deadline: Until(), cancellationToken: deadline.Token);
            Require(capabilities.ProtocolMajor == 1 && capabilities.ProtocolMinor >= 38 && capabilities.Capabilities.Contains("docker.files.read"));
            await Denied(async () => await client.InspectTargetAsync(new() { ResourceId = identifier, ResourceType = "Container" }, [], Until()), StatusCode.PermissionDenied);
            await Denied(async () => await client.ListAsync(ListRequest(), Headers("Inspect"), Until()), StatusCode.PermissionDenied);
            await Denied(async () => await client.ListAsync(ListRequest(), Headers("List", otherService: true), Until()), StatusCode.PermissionDenied);
            var used = Headers("List"); await client.ListAsync(ListRequest(), used, Until());
            await Denied(async () => await client.ListAsync(ListRequest(), used, Until()), StatusCode.PermissionDenied);

            Phase = "inspect-and-protected-core";
            var target = await client.InspectTargetAsync(new() { ResourceId = identifier, ResourceType = "Container" }, Headers("InspectTarget"), Until());
            Require(target.ContainerId == identifier && target.InstanceId == instance);
            await Denied(async () => await client.InspectTargetAsync(new() { ResourceId = core, ResourceType = "Container" }, Headers("InspectTarget"), Until()), StatusCode.PermissionDenied);
            var listing = await client.ListAsync(ListRequest(), Headers("List"), Until());
            Require(listing.NextCursor == "" && listing.Entries.Select(entry => entry.Name).ToHashSet(StringComparer.Ordinal).SetEquals(["中文.bin", ".env", "empty", "link"]));
            Require(listing.Entries.Single(entry => entry.Name == ".env").IsProtected && listing.Entries.Single(entry => entry.Name == "link").IsProtected);
            var metadata = await client.InspectAsync(Location(), Headers("Inspect"), Until());
            Require(metadata.Name == "中文.bin" && metadata.SizeBytes == size && metadata.Version == version && !metadata.IsProtected);

            Phase = "complete-binary-download";
            using (var download = client.Download(new() { Location = Location(), ExpectedVersion = version }, Headers("Download"), DateTime.UtcNow.AddSeconds(60), deadline.Token))
            {
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                long offset = 0; var started = false; var ended = false;
                while (await download.ResponseStream.MoveNext(deadline.Token))
                {
                    var frame = download.ResponseStream.Current; Require(!ended);
                    if (frame.Kind == "metadata")
                    {
                        Require(!started && offset == 0 && frame.Metadata.Name == metadata.Name && frame.Metadata.Version == version && frame.Metadata.SizeBytes == size);
                        started = true;
                    }
                    else if (frame.Kind == "chunk")
                    {
                        Require(started && frame.Offset == offset && frame.Data.Length is > 0 and <= 32768 && offset + frame.Data.Length <= size);
                        hash.AppendData(frame.Data.Span); offset += frame.Data.Length;
                    }
                    else if (frame.Kind == "end")
                    {
                        Require(started && offset == size && frame.SizeBytes == size && frame.Version == version && frame.Sha256 == digest && Convert.ToHexString(hash.GetHashAndReset()) == digest);
                        ended = true;
                    }
                    else throw new InvalidOperationException();
                }
                Require(started && ended && offset == size);
            }
            Phase = "stale-version-secret-path-and-instance-refusals";
            await DeniedDownload(new() { Location = Location(), ExpectedVersion = new('F', 64) }, StatusCode.Aborted);
            foreach (var path in new[] { "work/.env", "proc/self/status", "work/link/passwd" })
                await Denied(async () => await client.InspectAsync(Location(path), Headers("Inspect"), Until()), StatusCode.PermissionDenied);
            await Denied(async () => await client.InspectAsync(Location("../outside"), Headers("Inspect"), Until()), StatusCode.InvalidArgument);
            var stale = Location(); stale.InstanceId = "2:" + instance[(instance.IndexOf(':', StringComparison.Ordinal) + 1)..];
            await Denied(async () => await client.InspectAsync(stale, Headers("Inspect"), Until()), StatusCode.FailedPrecondition);
            Require((await client.InspectAsync(Location(), Headers("Inspect"), Until())).Version == version);
            Console.WriteLine(JsonSerializer.Serialize(new { installedContainerFileRpc = "passed", revision, bytes = size,
                fullByteDigest = true, fourMethods = true, methodAndServiceBinding = true, replayRefused = true,
                coreSecretSymlinkOldVersionAndInstanceRefused = true, productionMutation = false }));

            async Task DeniedDownload(ContainerFileDownloadRequest value, StatusCode expected)
            {
                using var download = client.Download(value, Headers("Download"), Until());
                await Denied(async () =>
                {
                    while (await download.ResponseStream.MoveNext(deadline.Token))
                        Require(download.ResponseStream.Current.Kind is not ("metadata" or "chunk" or "end"));
                }, expected);
            }
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    private static void Require(bool condition) { if (!condition) throw new InvalidOperationException(); }
    private static async Task Denied(Func<Task> action, StatusCode expected)
    {
        try { await action(); throw new InvalidOperationException(); }
        catch (RpcException error) when (error.StatusCode == expected) { }
    }
}
