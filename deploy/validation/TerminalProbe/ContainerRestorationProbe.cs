using System.Globalization;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using WhaleDeck.Contracts.Agent.V1;

/// <summary>Independent signed installed UDS observer, on the Python-owned fixture only.</summary>
internal static class ContainerRestorationProbe
{
    public static string Phase { get; private set; } = "startup";
    public static async Task RunAsync()
    {
        Require(OperatingSystem.IsLinux());
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(180));
        var input = new char[8193]; var count = 0;
        while (count < input.Length)
        {
            var read = await Console.In.ReadAsync(input.AsMemory(count), lifetime.Token); if (read == 0) break; count += read;
        }
        Require(count is > 0 and <= 8192);
        using var document = JsonDocument.Parse(new string(input, 0, count)); var fixture = document.RootElement;
        var revision = fixture.GetProperty("revision").GetString()!;
        Require(revision.Length == 40 && revision.All(char.IsAsciiHexDigitLower) && Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.EndsWith("+" + revision, StringComparison.Ordinal));
        var location = new ContainerFileLocation { ContainerId = fixture.GetProperty("containerId").GetString()!,
            InstanceId = fixture.GetProperty("instanceId").GetString()!, Path = "work/original.bin" };
        Require(location.ContainerId.Length == 64 && location.ContainerId.All(char.IsAsciiHexDigitLower));
        var source = new ContainerRestoreSource { CaptureId = fixture.GetProperty("captureId").GetString()!,
            CaptureHash = fixture.GetProperty("captureHash").GetString()!, Path = "work/original.bin" };
        var size = fixture.GetProperty("sizeBytes").GetInt32(); var sha = fixture.GetProperty("sha256").GetString()!;
        Require(size == 2 * 1024 * 1024 + 7 && sha.Length == 64);
        var key = File.ReadAllBytes("/etc/whaledeck/agent-capability.key"); Require(key.Length >= 32);
        try
        {
            using var handler = new SocketsHttpHandler { UseProxy = false, ConnectCallback = async (_, token) =>
            {
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                try { await socket.ConnectAsync(new UnixDomainSocketEndPoint("/run/whaledeck/agent.sock"), token); return new NetworkStream(socket, true); }
                catch { socket.Dispose(); throw; }
            } };
            using var channel = GrpcChannel.ForAddress("http://localhost", new() { HttpHandler = handler });
            var writes = new ContainerFileWriteService.ContainerFileWriteServiceClient(channel);
            DateTime Until() => DateTime.UtcNow.AddSeconds(120);
            Metadata Headers(string action, IMessage request, bool history = false)
            {
                var kind = history ? "history" : "file";
                using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                digest.AppendData(Encoding.UTF8.GetBytes("whaledeck-container-" + kind + "-request-v1\n" + request.Descriptor.FullName + "\n"));
                digest.AppendData(request.ToByteArray()); var hash = Convert.ToHexString(digest.GetHashAndReset());
                var expires = DateTimeOffset.UtcNow.AddSeconds(45).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture); var nonce = Guid.NewGuid().ToString("N");
                var method = "/whaledeck.agent.v1." + (history ? "ContainerHistoryService/" : "ContainerFileWriteService/") + action;
                var payload = "whaledeck-container-" + kind + "-capability-v1\n" + expires + "." + nonce + "." + method + "." + hash;
                return new() { { "x-whaledeck-capability", expires + "." + nonce + "." + Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload))) }, { "x-whaledeck-request-sha256", hash } };
            }
            Phase = "independent-restoration-capability";
            var caps = await new AgentService.AgentServiceClient(channel).GetCapabilitiesAsync(new(), deadline: Until(), cancellationToken: lifetime.Token);
            Require(caps.ProtocolMajor == 1 && caps.ProtocolMinor >= 43 && caps.Capabilities.Contains("docker.files.restore-attributes"));
            Phase = "fully-verified-installed-hdd-source";
            var request = new ContainerHistorySourceRequest { Location = location.Clone(), CaptureId = source.CaptureId, ExpectedCaptureHash = source.CaptureHash };
            using var content = new MemoryStream(size); var metadata = false; var ended = false; var sourceProof = "";
            using (var read = new ContainerHistoryService.ContainerHistoryServiceClient(channel).ReadSource(request, Headers("ReadSource", request, true), Until(), lifetime.Token))
            {
                while (await read.ResponseStream.MoveNext(lifetime.Token))
                {
                    var frame = read.ResponseStream.Current; Require(!ended);
                    if (frame.Kind == "metadata")
                    { Require(!metadata && frame.Metadata.CaptureId == source.CaptureId && frame.Metadata.ContainerId == location.ContainerId && frame.Metadata.Path == source.Path && frame.Metadata.SizeBytes == size && frame.Metadata.Sha256 == sha && frame.CaptureHash == source.CaptureHash && frame.TargetProof.Length == 64); metadata = true; sourceProof = frame.TargetProof; }
                    else if (frame.Kind == "chunk")
                    { Require(metadata && frame.Offset == content.Length && frame.Data.Length is > 0 and <= 32768 && content.Length + frame.Data.Length <= size); content.Write(frame.Data.Span); }
                    else if (frame.Kind == "end") { Require(metadata && content.Length == size && frame.SizeBytes == size && frame.Sha256 == sha && frame.CaptureHash == source.CaptureHash && frame.TargetProof == sourceProof); ended = true; }
                    else throw new InvalidOperationException();
                }
            }
            var bytes = content.ToArray(); Require(metadata && ended && Convert.ToHexString(SHA256.HashData(bytes)) == sha);
            async Task<ContainerUploadHeader> Header(string path, string operation)
            {
                Require(Guid.TryParseExact(operation, "N", out var id) && id != Guid.Empty);
                var target = location.Clone(); target.Path = path;
                var inspected = await writes.InspectTargetAsync(target, Headers("InspectTarget", target), Until(), lifetime.Token);
                return new() { Location = target, OperationId = operation, Owner = new('B', 64), SizeBytes = size, Sha256 = sha,
                    ExpectedVersion = inspected.ExpectedVersion, ExpectedParentVersion = inspected.ExpectedParentVersion, ExpectedTargetProof = inspected.TargetProof,
                    Context = new() { ActorSubject = "native-restoration-owned-observer", TraceId = "native-restoration-owned-observer", JobId = operation, IdempotencyKey = Guid.NewGuid().ToString("N") }, RestoreSource = source.Clone() };
            }
            async Task<ContainerUploadReceipt> Stage(ContainerUploadHeader header, Metadata? headers = null)
            {
                using var call = writes.StageUpload(headers ?? Headers("StageUpload", header), Until(), lifetime.Token);
                await call.RequestStream.WriteAsync(new() { Header = header }, lifetime.Token);
                for (var offset = 0; offset < size; offset += 32768)
                    await call.RequestStream.WriteAsync(new() { Offset = offset, Data = ByteString.CopyFrom(bytes, offset, Math.Min(32768, size - offset)) }, lifetime.Token);
                await call.RequestStream.WriteAsync(new() { Commit = true }, lifetime.Token); await call.RequestStream.CompleteAsync();
                return await call;
            }
            async Task<ContainerUploadReceipt> RejectedHeader(ContainerUploadHeader header, Metadata headers)
            {
                using var call = writes.StageUpload(headers, Until(), lifetime.Token);
                await call.RequestStream.WriteAsync(new() { Header = header }, lifetime.Token);
                return await call;
            }
            var header = await Header("work/original.bin", fixture.GetProperty("restoreOperationId").GetString()!);
            Phase = "source-field-tamper-invalidates-signature";
            var altered = header.Clone(); altered.RestoreSource.Path = "work/unapproved.bin";
            await Denied(async () => await RejectedHeader(altered, Headers("StageUpload", header)), StatusCode.PermissionDenied);
            Phase = "valid-signature-wrong-source-hash-refused";
            var bad = await Header("work/rejected.bin", fixture.GetProperty("badOperationId").GetString()!); bad.RestoreSource.CaptureHash = new('F', 64);
            await Denied(async () => await RejectedHeader(bad, Headers("StageUpload", bad)), StatusCode.Aborted);
            var results = new List<object>();
            foreach (var create in new[] { false, true })
            {
                Phase = create ? "signed-other-path-attribute-publication" : "signed-original-path-attribute-publication";
                if (create) header = await Header("work/recovered.bin", fixture.GetProperty("newOperationId").GetString()!);
                var signed = Headers("StageUpload", header); Require((await Stage(header, signed)).Upload.State == "Staged");
                await Denied(async () => await RejectedHeader(header, signed), StatusCode.PermissionDenied);
                var reference = new ContainerUploadReference { ContainerId = location.ContainerId, InstanceId = location.InstanceId, OperationId = header.OperationId,
                    Owner = header.Owner, ExpectedTargetProof = header.ExpectedTargetProof, Context = header.Context.Clone() };
                var published = await writes.PublishAsync(reference, Headers("Publish", reference), Until(), lifetime.Token);
                Require(published.Upload.State == "Published" && published.Upload.BindingCurrent && published.Upload.PreviousRetained == !create);
                var observed = await writes.InspectAsync(reference, Headers("Inspect", reference), Until(), lifetime.Token);
                Require(observed.Upload.State == "Published" && observed.Upload.Version == published.Upload.Version);
                results.Add(new { operationId = header.OperationId, path = header.Location.Path, expectedVersion = header.ExpectedVersion, publishedVersion = published.Upload.Version, targetProof = header.ExpectedTargetProof });
            }
            Console.WriteLine(JsonSerializer.Serialize(new { installedContainerRestoration = "passed", revision, bytes = size,
                fullVerifiedSource = true, sourceTamperAndReplayDenied = true, signedWrongHashRefused = true, results, humanJobOrUi = false }));
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
    private static void Require(bool value) { if (!value) throw new InvalidOperationException(); }
    private static async Task Denied(Func<Task> action, StatusCode status)
    {
        try { await action(); throw new InvalidOperationException(); }
        catch (RpcException error) when (error.StatusCode == status) { }
    }
}
