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

/// <summary>Signed installed-UDS validation on the native observer's owned fixture only.</summary>
internal static class ContainerFileWriteProbe
{
    private const string Prefix = "/whaledeck.agent.v1.ContainerFileWriteService/";
    private const int Size = 2 * 1024 * 1024 + 7;
    public static string Phase { get; private set; } = "startup";

    public static async Task RunAsync()
    {
        Require(OperatingSystem.IsLinux());
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(180));
        var input = new char[8193]; var count = 0;
        while (count < input.Length)
        {
            var read = await Console.In.ReadAsync(input.AsMemory(count), lifetime.Token);
            if (read == 0) break;
            count += read;
        }
        Require(count is > 0 and <= 8192);
        using var document = JsonDocument.Parse(new string(input, 0, count));
        var fixture = document.RootElement;
        var revision = fixture.GetProperty("revision").GetString()!;
        Require(revision.Length == 40 && revision.All(char.IsAsciiHexDigitLower));
        Require(Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion.EndsWith("+" + revision, StringComparison.Ordinal));
        var location = new ContainerFileLocation { ContainerId = fixture.GetProperty("containerId").GetString()!,
            InstanceId = fixture.GetProperty("instanceId").GetString()!, Path = "work/original.bin" };
        Require(location.ContainerId.Length == 64 && location.ContainerId.All(char.IsAsciiHexDigitLower));
        var operation = fixture.GetProperty("operationId").GetString()!;
        var cancelOperation = fixture.GetProperty("cancelOperationId").GetString()!;
        Require(Guid.TryParseExact(operation, "N", out var firstId) && firstId != Guid.Empty &&
            Guid.TryParseExact(cancelOperation, "N", out var secondId) && secondId != Guid.Empty && firstId != secondId);
        var body = new byte[Size];
        for (var index = 0; index < body.Length; index++) body[index] = (byte)(index % 251);
        var digest = Convert.ToHexString(SHA256.HashData(body));
        var key = await File.ReadAllBytesAsync("/etc/whaledeck/agent-capability.key", lifetime.Token);
        try
        {
            using var handler = new SocketsHttpHandler { UseProxy = false, ConnectCallback = async (_, token) =>
            {
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                try { await socket.ConnectAsync(new UnixDomainSocketEndPoint("/run/whaledeck/agent.sock"), token); return new NetworkStream(socket, true); }
                catch { socket.Dispose(); throw; }
            } };
            using var channel = GrpcChannel.ForAddress("http://localhost", new() { HttpHandler = handler });
            var client = new ContainerFileWriteService.ContainerFileWriteServiceClient(channel);
            DateTime Until() => DateTime.UtcNow.AddSeconds(60);
            Metadata Headers(string action, IMessage message, bool legacy = false)
            {
                // Independently encode the wire domain, rather than using the
                // production helper to manufacture its own expected signature.
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                hash.AppendData(Encoding.UTF8.GetBytes("whaledeck-container-file-request-v1\n" + message.Descriptor.FullName + "\n"));
                hash.AppendData(message.ToByteArray());
                var requestHash = Convert.ToHexString(hash.GetHashAndReset());
                var expires = DateTimeOffset.UtcNow.AddSeconds(45).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
                var nonce = Guid.NewGuid().ToString("N");
                var payload = legacy ? expires + "." + nonce + "." + Prefix + action :
                    "whaledeck-container-file-capability-v1\n" + expires + "." + nonce + "." + Prefix + action + "." + requestHash;
                return new() { { "x-whaledeck-capability", expires + "." + nonce + "." + Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload))) },
                    { "x-whaledeck-request-sha256", requestHash } };
            }
            async Task<ContainerUploadTarget> Target(ContainerFileLocation value) =>
                await client.InspectTargetAsync(value, Headers("InspectTarget", value), Until(), lifetime.Token);
            async Task<ContainerUploadReceipt> Action(string action, ContainerUploadReference value)
            {
                var headers = Headers(action, value);
                using var call = action switch
                {
                    "Inspect" => client.InspectAsync(value, headers, Until(), lifetime.Token),
                    "Discard" => client.DiscardAsync(value, headers, Until(), lifetime.Token),
                    "Publish" => client.PublishAsync(value, headers, Until(), lifetime.Token),
                    _ => throw new InvalidOperationException()
                };
                return await call;
            }
            Phase = "capability-and-request-binding";
            var caps = await new AgentService.AgentServiceClient(channel).GetCapabilitiesAsync(new(), deadline: Until(), cancellationToken: lifetime.Token);
            Require(caps.ProtocolMajor == 1 && caps.ProtocolMinor >= 39 && caps.Capabilities.Contains("docker.files.bound-uploads"));
            await Denied(async () => await client.InspectTargetAsync(location, [], Until()), StatusCode.PermissionDenied);
            await Denied(async () => await client.InspectTargetAsync(location, Headers("InspectTarget", location, legacy: true), Until()), StatusCode.PermissionDenied);
            var changed = location.Clone(); changed.Path = "work/unapproved.bin";
            await Denied(async () => await client.InspectTargetAsync(changed, Headers("InspectTarget", location), Until()), StatusCode.PermissionDenied);
            var replay = Headers("InspectTarget", location);
            var plan = await client.InspectTargetAsync(location, replay, Until(), lifetime.Token);
            await Denied(async () => await client.InspectTargetAsync(location, replay, Until()), StatusCode.PermissionDenied);
            Require(plan.Location.Equals(location) && plan.CurrentSizeBytes == Size && plan.ExpectedVersion != "ABSENT");
            var core = new ContainerFileLocation { ContainerId = fixture.GetProperty("coreId").GetString()!,
                InstanceId = fixture.GetProperty("coreInstanceId").GetString()!, Path = "etc/issue" };
            await Denied(async () => await Target(core), StatusCode.PermissionDenied);
            var secret = location.Clone(); secret.Path = "work/.env";
            await Denied(async () => await Target(secret), StatusCode.PermissionDenied);
            var stale = location.Clone(); stale.InstanceId = "2:" + location.InstanceId[(location.InstanceId.IndexOf(':', StringComparison.Ordinal) + 1)..];
            await Denied(async () => await Target(stale), StatusCode.FailedPrecondition);

            RequestContext Context() => new() { ActorSubject = "native-container-write-owned-validation", TraceId = "native-container-write-owned-validation",
                JobId = Guid.NewGuid().ToString("N"), IdempotencyKey = Guid.NewGuid().ToString("N") };
            ContainerUploadHeader Open(ContainerUploadTarget target, string id) => new() { Location = target.Location.Clone(), OperationId = id,
                Owner = new('B', 64), SizeBytes = Size, Sha256 = digest, ExpectedVersion = target.ExpectedVersion,
                ExpectedParentVersion = target.ExpectedParentVersion, ExpectedTargetProof = target.TargetProof, Context = Context() };
            static ContainerUploadReference Reference(ContainerUploadHeader header) => new() { ContainerId = header.Location.ContainerId,
                InstanceId = header.Location.InstanceId, OperationId = header.OperationId, Owner = header.Owner,
                ExpectedTargetProof = header.ExpectedTargetProof, Context = header.Context.Clone() };
            async Task<ContainerUploadReceipt> Stage(ContainerUploadHeader header)
            {
                using var call = client.StageUpload(Headers("StageUpload", header), Until(), lifetime.Token);
                await call.RequestStream.WriteAsync(new() { Header = header }, lifetime.Token);
                for (var offset = 0; offset < body.Length; offset += 32768)
                    await call.RequestStream.WriteAsync(new() { Offset = offset, Data = ByteString.CopyFrom(body, offset, Math.Min(32768, body.Length - offset)) }, lifetime.Token);
                await call.RequestStream.WriteAsync(new() { Commit = true }, lifetime.Token);
                await call.RequestStream.CompleteAsync();
                var staged = await call;
                Require(staged.Upload.State == "Staged" && staged.Upload.SizeBytes == Size && staged.Upload.Sha256 == digest &&
                    staged.ContainerId == location.ContainerId && staged.InstanceId == location.InstanceId && staged.TargetProof == header.ExpectedTargetProof);
                return staged;
            }
            Phase = "complete-stage-and-atomic-publication";
            var header = Open(plan, operation); var reference = Reference(header);
            Require((await Stage(header)).Backup is null);
            Require((await Action("Inspect", reference)).Upload.State == "Staged");
            var published = await Action("Publish", reference);
            Require(published.Upload.State == "Published" && published.Upload.BindingCurrent && published.Upload.PreviousRetained &&
                published.Upload.SizeBytes == Size && published.Upload.Sha256 == digest && published.Backup is not null &&
                published.Backup.ContainerId == location.ContainerId && published.Backup.InstanceId == location.InstanceId &&
                published.Backup.TargetProof == plan.TargetProof && published.Backup.Proof.OperationId == operation &&
                published.Backup.Proof.SourceVersion == plan.ExpectedVersion && published.Backup.Proof.SizeBytes == Size &&
                published.Backup.Proof.Sha256 == fixture.GetProperty("originalSha256").GetString());
            var replayed = await Action("Publish", reference);
            Require(replayed.Upload.State == "Published" && replayed.Upload.Replayed && (await Action("Inspect", reference)).Upload.BindingCurrent);
            await Denied(async () => await Action("Discard", reference), StatusCode.FailedPrecondition);
            var wrong = reference.Clone(); wrong.Owner = new('C', 64);
            await Denied(async () => await Action("Inspect", wrong), StatusCode.PermissionDenied);
            wrong = reference.Clone(); wrong.ExpectedTargetProof = new('F', 64);
            await Denied(async () => await Action("Inspect", wrong), StatusCode.FailedPrecondition);
            await Denied(async () => await client.DiscardAsync(reference, Headers("Publish", reference), Until()), StatusCode.PermissionDenied);

            Phase = "complete-installed-readback";
            var readClient = new ContainerFileService.ContainerFileServiceClient(channel);
            var readMethod = "/whaledeck.agent.v1.ContainerFileService/Download";
            var readNonce = DateTimeOffset.UtcNow.AddSeconds(45).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture) + "." + Guid.NewGuid().ToString("N");
            var readHeaders = new Metadata { { "x-whaledeck-capability", readNonce + "." + Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(readNonce + "." + readMethod))) } };
            using (var call = readClient.Download(new() { Location = location, ExpectedVersion = published.Upload.Version }, readHeaders, Until(), lifetime.Token))
            {
                var offset = 0; var metadata = false; var ended = false;
                while (await call.ResponseStream.MoveNext(lifetime.Token))
                {
                    var frame = call.ResponseStream.Current; Require(!ended);
                    if (frame.Kind == "metadata") { Require(!metadata && offset == 0 && frame.Metadata.SizeBytes == Size && frame.Metadata.Version == published.Upload.Version); metadata = true; }
                    else if (frame.Kind == "chunk")
                    {
                        Require(metadata && frame.Data.Length is > 0 and <= 32768 && frame.Offset == offset && offset + frame.Data.Length <= Size &&
                            frame.Data.Span.SequenceEqual(body.AsSpan(offset, frame.Data.Length))); offset += frame.Data.Length;
                    }
                    else if (frame.Kind == "end") { Require(metadata && offset == Size && frame.SizeBytes == Size && frame.Sha256 == digest && frame.Version == published.Upload.Version); ended = true; }
                    else throw new InvalidOperationException();
                }
                Require(metadata && ended && offset == Size);
            }
            Phase = "explicit-staged-discard";
            var cancel = location.Clone(); cancel.Path = "work/canceled.bin";
            var cancelPlan = await Target(cancel); Require(cancelPlan.ExpectedVersion == "ABSENT");
            var cancelHeader = Open(cancelPlan, cancelOperation); var cancelReference = Reference(cancelHeader);
            await Stage(cancelHeader);
            Require((await Action("Discard", cancelReference)).Upload.State == "Discarded");
            Require((await Action("Discard", cancelReference)).Upload.State == "Discarded" && (await Action("Inspect", cancelReference)).Upload.State == "Discarded");
            Console.WriteLine(JsonSerializer.Serialize(new { installedContainerFileWriteRpc = "passed", revision, bytes = Size,
                fiveMethods = true, bodyMethodReplayAndLegacyRefused = true, completeInstalledReadback = true,
                hddOverwriteProof = true, explicitStagedDiscard = true, operationId = operation, cancelOperationId = cancelOperation,
                targetProof = plan.TargetProof, sha256 = digest, humanBffOrUi = false }));
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
