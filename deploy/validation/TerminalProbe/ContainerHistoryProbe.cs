using System.Globalization;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;

/// <summary>Read-only installed-UDS validation over the native fixture; it never creates or deletes container files.</summary>
internal static class ContainerHistoryProbe
{
    private const int Size = 2 * 1024 * 1024 + 7;
    public static string Phase { get; private set; } = "startup";

    public static async Task RunAsync()
    {
        Require(OperatingSystem.IsLinux());
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(150));
        Phase = "input-validation";
        var input = new char[4097]; var count = 0;
        while (count < input.Length)
        {
            var read = await Console.In.ReadAsync(input.AsMemory(count), lifetime.Token);
            if (read == 0) break;
            count += read;
        }
        Require(count is > 0 and <= 4096);
        using var document = JsonDocument.Parse(new string(input, 0, count));
        var fixture = document.RootElement;
        var fields = fixture.ValueKind == JsonValueKind.Object ? fixture.EnumerateObject().Select(property => property.Name).ToArray() : [];
        Require(fields.Length == 10 && fields.ToHashSet(StringComparer.Ordinal).SetEquals(
            ["revision", "containerId", "instanceId", "originalInstanceId", "path",
                "captureId", "sourceVersion", "sha256", "sizeBytes", "coreId"]));
        var revision = Text(fixture, "revision");
        Require(revision.Length == 40 && revision.All(char.IsAsciiHexDigitLower));
        Require(Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion.EndsWith("+" + revision, StringComparison.Ordinal));
        var containerId = Text(fixture, "containerId");
        var coreId = Text(fixture, "coreId");
        Require(LowerHash(containerId) && LowerHash(coreId));
        var instanceId = Text(fixture, "instanceId");
        var originalInstanceId = Text(fixture, "originalInstanceId");
        Require(Instance(instanceId) && Instance(originalInstanceId));
        var path = Text(fixture, "path");
        Require(path == "work/original.bin");
        var captureId = Text(fixture, "captureId");
        Require(Guid.TryParseExact(captureId, "N", out var capture) && capture != Guid.Empty && capture.ToString("N") == captureId);
        var sourceVersion = Text(fixture, "sourceVersion");
        var expectedSha = Text(fixture, "sha256");
        Require(UpperHash(sourceVersion) && UpperHash(expectedSha) && fixture.GetProperty("sizeBytes").GetInt64() == Size);

        var key = await File.ReadAllBytesAsync("/etc/whaledeck/agent-capability.key", lifetime.Token);
        try
        {
            Require(key.Length == 32);
            using var handler = new SocketsHttpHandler { UseProxy = false, ConnectCallback = async (_, token) =>
            {
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                try
                {
                    await socket.ConnectAsync(new UnixDomainSocketEndPoint("/run/whaledeck/agent.sock"), token);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch { socket.Dispose(); throw; }
            } };
            using var channel = GrpcChannel.ForAddress("http://localhost", new() { HttpHandler = handler });
            var client = new ContainerHistoryService.ContainerHistoryServiceClient(channel);
            DateTime Until(int seconds = 30) => DateTime.UtcNow.AddSeconds(seconds);
            ContainerFileLocation Location(string id = "") => new()
            {
                ContainerId = id.Length == 0 ? containerId : id,
                InstanceId = instanceId,
                Path = path
            };
            ContainerHistoryListRequest ListRequest(string id = "") => new()
            {
                Location = Location(id),
                After = "",
                Take = 10,
                ExpectedVersion = ""
            };
            Metadata Headers(string action, IMessage request, bool uploadDomain = false)
            {
                var requestHash = ContainerHistoryBinding.Hash(request);
                var expires = DateTimeOffset.UtcNow.AddSeconds(45).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
                var nonce = Guid.NewGuid().ToString("N");
                var method = ContainerHistoryBinding.Service + action;
                var payload = uploadDomain
                    ? "whaledeck-container-file-capability-v1\n" + expires + "." + nonce + "." + method + "." + requestHash
                    : ContainerHistoryBinding.Payload(expires, nonce, method, requestHash);
                return new()
                {
                    { "x-whaledeck-capability", expires + "." + nonce + "." + Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload))) },
                    { ContainerHistoryBinding.Header, requestHash }
                };
            }
            Metadata HashOnly(IMessage request) => new() { { ContainerHistoryBinding.Header, ContainerHistoryBinding.Hash(request) } };

            Phase = "capability-negotiation";
            var capabilities = await new AgentService.AgentServiceClient(channel).GetCapabilitiesAsync(new(), deadline: Until(), cancellationToken: lifetime.Token);
            Require(capabilities.ProtocolMajor == 1 && capabilities.ProtocolMinor >= 42 &&
                capabilities.Capabilities.Contains("docker.files.history") && capabilities.Capabilities.Contains("docker.files.history-source"));

            Phase = "request-binding-rejections";
            var listRequest = ListRequest();
            await Denied(async () => await client.ListAsync(listRequest, HashOnly(listRequest), Until(), lifetime.Token), StatusCode.PermissionDenied);
            await Denied(async () => await client.ListAsync(listRequest, Headers("List", listRequest, uploadDomain: true), Until(), lifetime.Token), StatusCode.PermissionDenied);
            var changed = listRequest.Clone(); changed.Location.Path = "work/changed.bin";
            await Denied(async () => await client.ListAsync(changed, Headers("List", listRequest), Until(), lifetime.Token), StatusCode.PermissionDenied);

            Phase = "list-owned-history";
            var replay = Headers("List", listRequest);
            var listing = await client.ListAsync(listRequest, replay, Until(), lifetime.Token);
            Phase = "list-replay-rejected";
            await Denied(async () => await client.ListAsync(listRequest, replay, Until(), lifetime.Token), StatusCode.PermissionDenied);
            Phase = "list-protected-core-rejected";
            await Denied(async () =>
            {
                var core = ListRequest(coreId);
                await client.ListAsync(core, Headers("List", core), Until(), lifetime.Token);
            }, StatusCode.PermissionDenied);
            Require(listing.Location.Equals(listRequest.Location) && UpperHash(listing.TargetProof) && UpperHash(listing.Version) &&
                listing.NextCursor.Length == 0 && listing.Scanned is >= 1 and <= 500 && listing.InvalidRecords <= listing.Scanned && listing.Complete && listing.Entries.Count == 1);
            var entry = listing.Entries[0];
            Require(entry.CaptureId == captureId && entry.ContainerId == containerId && entry.OriginalInstanceId == originalInstanceId &&
                entry.Path == path && entry.SourceVersion == sourceVersion && entry.SizeBytes == Size && entry.Sha256 == expectedSha &&
                entry.CreatedAtUnixMs is >= 0 and <= 253402300799999L && entry.Availability == "Present" && UpperHash(entry.CaptureHash));

            Phase = "complete-history-source";
            var source = new ContainerHistorySourceRequest
            {
                Location = Location(),
                CaptureId = captureId,
                ExpectedCaptureHash = entry.CaptureHash
            };
            using var call = client.ReadSource(source, Headers("ReadSource", source), Until(120), lifetime.Token);
            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var metadata = false; var ended = false; var proof = ""; long offset = 0;
            while (await call.ResponseStream.MoveNext(lifetime.Token))
            {
                var frame = call.ResponseStream.Current;
                Require(!ended);
                if (frame.Kind == "metadata")
                {
                    Require(!metadata && frame.Metadata is not null && frame.Metadata.Equals(entry) && frame.Data.Length == 0 && frame.Offset == 0 &&
                        frame.Sha256.Length == 0 && frame.SizeBytes == 0 && frame.CaptureHash == entry.CaptureHash &&
                        frame.TargetProof == listing.TargetProof && UpperHash(frame.TargetProof));
                    proof = frame.TargetProof; metadata = true;
                }
                else if (frame.Kind == "chunk")
                {
                    Require(metadata && frame.Metadata is null && frame.Data.Length is > 0 and <= 32768 && frame.Offset == offset &&
                        offset + frame.Data.Length <= Size && frame.Sha256.Length == 0 && frame.SizeBytes == 0 &&
                        frame.TargetProof.Length == 0 && frame.CaptureHash.Length == 0);
                    digest.AppendData(frame.Data.Span); offset += frame.Data.Length;
                }
                else if (frame.Kind == "end")
                {
                    Require(metadata && frame.Metadata is null && frame.Data.Length == 0 && frame.Offset == 0 && offset == Size &&
                        frame.SizeBytes == Size && frame.Sha256 == expectedSha && frame.CaptureHash == entry.CaptureHash &&
                        frame.TargetProof == proof && Convert.ToHexString(digest.GetHashAndReset()) == expectedSha);
                    ended = true;
                }
                else throw new InvalidOperationException();
            }
            Require(metadata && ended && offset == Size);
            Phase = "success";
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                installedContainerHistoryRpc = true,
                bytes = Size,
                capabilities = true,
                requestHashMethodDomainAndReplay = true,
                protectedCoreRefused = true,
                fullSourceVerifiedAfterRpcEof = true,
                productionMutation = false
            }));
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    private static string Text(JsonElement value, string name) => value.GetProperty(name).GetString() ?? throw new InvalidOperationException();
    private static bool LowerHash(string value) => value.Length == 64 && value.All(char.IsAsciiHexDigitLower);
    private static bool UpperHash(string value) => value.Length == 64 && value.All(character => char.IsAsciiDigit(character) || character is >= 'A' and <= 'F');

    private static bool Instance(string value)
    {
        var separator = value.IndexOf(':', StringComparison.Ordinal);
        if (separator < 1 || !int.TryParse(value.AsSpan(0, separator), NumberStyles.None, CultureInfo.InvariantCulture, out var pid) || pid < 2 ||
            pid.ToString(CultureInfo.InvariantCulture).Length != separator) return false;
        var timestamp = value[(separator + 1)..];
        if (timestamp.Length != 20 && timestamp.Length is not (>= 22 and <= 30) || timestamp[^1] != 'Z' || timestamp[10] != 'T' ||
            timestamp[13] != ':' || timestamp[16] != ':' || timestamp[4] != '-' || timestamp[7] != '-' ||
            timestamp.Length > 20 && timestamp[19] != '.') return false;
        for (var index = 0; index < timestamp.Length - 1; index++)
            if (index is not (4 or 7 or 10 or 13 or 16 or 19) && !char.IsAsciiDigit(timestamp[index])) return false;
        var fraction = timestamp.Length == 20 ? "" : timestamp.Substring(20, Math.Min(7, timestamp.Length - 21));
        var parsed = timestamp[..19] + (fraction.Length == 0 ? "" : "." + fraction) + "Z";
        var format = "yyyy-MM-dd'T'HH:mm:ss" + (fraction.Length == 0 ? "" : "." + new string('f', fraction.Length)) + "'Z'";
        return DateTimeOffset.TryParseExact(parsed, format,
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out _);
    }

    private static void Require(bool condition) { if (!condition) throw new InvalidOperationException(); }

    private static async Task Denied(Func<Task> action, StatusCode expected)
    {
        try { await action(); throw new InvalidOperationException(); }
        catch (RpcException error) when (error.StatusCode == expected) { }
    }
}
