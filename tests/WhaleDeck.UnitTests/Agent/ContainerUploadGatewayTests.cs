using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WhaleDeck.Infrastructure.Agent;
using App = WhaleDeck.Application.Models;
using Rpc = WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.UnitTests.Agent;

/// <summary>SDK/HTTP2 over a private local UDS to a synthetic Agent; no helper or installed Agent evidence.</summary>
public sealed partial class ContainerUploadGatewayTests
{
    private const string Service = "/whaledeck.agent.v1.ContainerFileWriteService/";
    private static readonly int[] ChunkSizes = [32768, 32768, 7];
    private static App.ContainerFileLocationDto Location => new(new('a', 64),
        "777:2026-10-22T01:02:03.123456789Z", "目录/🫧.bin");
    private static readonly Guid Operation = Guid.ParseExact("aa0a8865343e49bdb01a72680131bf39", "N");
    private static App.ContainerFileWriteContextDto Context => new("fixture-admin", "fixture-trace",
        Guid.ParseExact("bb0a8865343e49bdb01a72680131bf39", "N"), Guid.ParseExact("cc0a8865343e49bdb01a72680131bf39", "N"));
    private static App.ContainerFileUploadReferenceDto Reference => new(Location.ContainerId, Location.InstanceId,
        Operation, new('B', 64), new('A', 64), Context);
    private static App.ContainerFileStageDto Stage(byte[] bytes) => new(Location, Operation, Reference.Owner,
        bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)), new('D', 64), new('E', 64), Reference.ExpectedTargetProof, Context);

    [Theory]
    [InlineData(1U, 39U, true)][InlineData(1U, 40U, false)][InlineData(2U, 40U, true)]
    public async Task PreReceiveCancellationRequiresItsOwnNegotiatedCapability(uint major, uint minor, bool fence)
    {
        await using var harness = await Harness.Start(major, minor, cancellationFence: fence);
        var error = await Assert.ThrowsAsync<RpcException>(() => harness.Gateway.PreventContainerUploadAsync(Reference, CancellationToken.None));
        Assert.Equal(StatusCode.Unimplemented, error.StatusCode); Assert.Empty(harness.Calls);
    }

    [Theory]
    [InlineData(1U, 39U, true, true)]
    [InlineData(1U, 40U, true, false)]
    [InlineData(1U, 40U, false, true)]
    [InlineData(2U, 40U, true, true)]
    public async Task IncompleteWorkflowCannotNegotiateStageOrPublish(uint major, uint minor, bool capability, bool fence)
    {
        await using var harness = await Harness.Start(major, minor, capability, fence);
        var enumerated = false;
        async IAsyncEnumerable<ReadOnlyMemory<byte>> UnreadBytes()
        { enumerated = true; await Task.CompletedTask; yield return new byte[] { 42 }; }
        var negotiation = await Assert.ThrowsAsync<RpcException>(() => harness.Gateway.RequireContainerUploadWorkflowAsync(default));
        var stage = await Assert.ThrowsAsync<RpcException>(() => harness.Gateway.StageContainerFileAsync(Stage([42]), UnreadBytes(), default));
        var publish = await Assert.ThrowsAsync<RpcException>(() => harness.Gateway.PublishContainerFileAsync(Reference, default));
        Assert.Equal(StatusCode.Unimplemented, negotiation.StatusCode);
        Assert.Equal(StatusCode.Unimplemented, stage.StatusCode);
        Assert.Equal(StatusCode.Unimplemented, publish.StatusCode);
        Assert.False(enumerated); Assert.Empty(harness.Calls); Assert.Empty(harness.Frames);
    }

    [Fact]
    public async Task FullWorkflowNegotiationIsReadOnly()
    {
        await using var harness = await Harness.Start(minor: 41, cancellationFence: true, recoverySeal: true);
        await harness.Gateway.RequireContainerUploadWorkflowAsync(default);
        Assert.Empty(harness.Calls); Assert.Empty(harness.Frames);
    }

    [Theory]
    [InlineData(40U, true)]
    [InlineData(41U, false)]
    public async Task BusinessUploadRequiresRecoveryEvenWhenPrimitiveWritesAreSupported(uint minor, bool recovery)
    {
        await using var harness = await Harness.Start(minor: minor, cancellationFence: true, recoverySeal: recovery);
        var error = await Assert.ThrowsAsync<RpcException>(() => harness.Gateway.RequireContainerUploadWorkflowAsync(default));
        Assert.Equal(StatusCode.Unimplemented, error.StatusCode); Assert.Empty(harness.Calls); Assert.Empty(harness.Frames);
    }

    [Fact]
    public async Task PreventedReceiptUsesTheSameBodyBoundDiscardMethodWithoutClaimingUploadedBytes()
    {
        await using var harness = await Harness.Start(minor: 40, cancellationFence: true);
        harness.ReturnPrevented = true;
        var result = await harness.Gateway.PreventContainerUploadAsync(Reference, CancellationToken.None);
        Assert.Equal("Prevented", result.Upload.State); Assert.Equal(0, result.Upload.SizeBytes);
        Assert.Equal(Convert.ToHexString(SHA256.HashData([])), result.Upload.Sha256);
        Assert.False(result.Upload.BindingCurrent); Assert.False(result.Upload.PreviousRetained); Assert.Null(result.Upload.HddBackup);
        var call = Assert.Single(harness.Calls); Assert.Equal("Discard", call.Action); harness.AssertSignature(call);
    }

    [Theory]
    [InlineData("size")][InlineData("digest")][InlineData("backup")][InlineData("version")][InlineData("state")]
    public async Task InvalidPreventedFactsNeverBecomePositiveCancellationProof(string variant)
    {
        await using var harness = await Harness.Start(minor: 40, cancellationFence: true);
        harness.ReturnPrevented = true; harness.InvalidFence = variant;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Gateway.PreventContainerUploadAsync(Reference, CancellationToken.None));
        Assert.Equal("CONTAINER_UPLOAD_OUTPUT_INVALID", error.Message);
    }

    [Theory]
    [InlineData("CONTAINER_NOT_RUNNING")][InlineData("CONTAINER_FILES_INSTANCE_CHANGED")]
    [InlineData("CONTAINER_FILES_ROOT_CHANGED")][InlineData("CONTAINER_FILES_TARGET_CHANGED")]
    [InlineData("CONTAINER_UPLOAD_TARGET_CHANGED")][InlineData("CONTAINER_UPLOAD_PUBLISH_PARENT_CHANGED")]
    public async Task DefinitiveOriginalTargetFailuresUseAFixedBusinessMarker(string code)
    {
        await using var harness = await Harness.Start(); harness.InspectFailure = new(StatusCode.FailedPrecondition,code);
        var error = await Assert.ThrowsAsync<App.ContainerFileUploadTargetUnavailableException>(() => harness.Gateway.InspectContainerUploadAsync(Reference,default));
        Assert.Equal("CONTAINER_UPLOAD_TARGET_UNAVAILABLE",error.Message);
    }

    [Theory]
    [InlineData(1U, 38U, true)][InlineData(0U, 39U, true)]
    [InlineData(2U, 39U, true)][InlineData(1U, 39U, false)]
    public async Task OldMinorUnknownMajorOrMissingIndependentCapabilityRefusesBeforeWriteRequest(uint major, uint minor, bool capability)
    {
        await using var harness = await Harness.Start(major, minor, capability);
        var error = await Assert.ThrowsAsync<RpcException>(() => harness.Gateway.InspectContainerUploadTargetAsync(Location, default));
        Assert.Equal(StatusCode.Unimplemented, error.StatusCode);
        Assert.Equal("CONTAINER_UPLOAD_CAPABILITY_UNAVAILABLE", error.Status.Detail);
        Assert.Empty(harness.Calls);
    }

    [Theory]
    [InlineData(39U)][InlineData(40U)]
    public async Task CurrentAndFutureMinorWorkWithOnlyIndependentWriteCapability(uint minor)
    {
        await using var harness = await Harness.Start(minor: minor);
        var target = await harness.Gateway.InspectContainerUploadTargetAsync(Location, default);
        Assert.Equal(Location, target.Location);
        Assert.Equal(Reference.ExpectedTargetProof, target.TargetProof);
        Assert.Equal(new string('E', 64), target.ExpectedParentVersion);
        Assert.Equal(new string('D', 64), target.ExpectedVersion);
        Assert.Single(harness.Calls);
    }

    [Theory]
    [InlineData("InspectTarget")][InlineData("StageUpload")]
    [InlineData("Publish")][InlineData("Inspect")][InlineData("Discard")]
    public async Task FiveMethodsPreserveExactRequestAndUseIndependentBodyHashHmac(string action)
    {
        await using var harness = await Harness.Start(minor: 40, cancellationFence: true);
        var content = RandomNumberGenerator.GetBytes(2 * 32768 + 7);
        var stage = Stage(content);
        App.ContainerFileUploadReceiptDto? receipt = null;
        if (action == "InspectTarget")
            _ = await harness.Gateway.InspectContainerUploadTargetAsync(Location, default);
        else if (action == "StageUpload")
            receipt = await harness.Gateway.StageContainerFileAsync(stage, Bytes(content), default);
        else if (action == "Publish")
            receipt = await harness.Gateway.PublishContainerFileAsync(Reference, default);
        else if (action == "Inspect")
            receipt = await harness.Gateway.InspectContainerUploadAsync(Reference, default);
        else
            receipt = await harness.Gateway.DiscardContainerUploadAsync(Reference, default);

        var call = Assert.Single(harness.Calls);
        Assert.Equal(action, call.Action);
        harness.AssertSignature(call);
        if (action == "InspectTarget") AssertLocation(Assert.IsType<Rpc.ContainerFileLocation>(call.Request));
        else if (action == "StageUpload")
        {
            var header = Assert.IsType<Rpc.ContainerUploadHeader>(call.Request);
            AssertLocation(header.Location);
            Assert.Equal(Operation.ToString("N"), header.OperationId);
            Assert.Equal(stage.Owner, header.Owner);
            Assert.Equal(stage.SizeBytes, header.SizeBytes);
            Assert.Equal(stage.Sha256, header.Sha256);
            Assert.Equal(stage.ExpectedVersion, header.ExpectedVersion);
            Assert.Equal(stage.ExpectedParentVersion, header.ExpectedParentVersion);
            Assert.Equal(stage.ExpectedTargetProof, header.ExpectedTargetProof);
            AssertContext(header.Context);

            Assert.True(harness.UploadReachedEof);
            var frames = harness.Frames.ToArray();
            Assert.NotNull(frames[0].Header);
            Assert.Empty(frames[0].Data);
            Assert.False(frames[0].Commit);
            Assert.Equal(0, frames[0].Offset);
            var dataFrames = frames[1..^1];
            Assert.Equal(ChunkSizes, dataFrames.Select(frame => frame.Data.Length));
            using var body = new MemoryStream();
            foreach (var frame in dataFrames)
            {
                Assert.Null(frame.Header);
                Assert.False(frame.Commit);
                Assert.Equal(body.Length, frame.Offset);
                frame.Data.WriteTo(body);
            }
            Assert.Equal(content, body.ToArray());
            Assert.Equal(stage.Sha256, Convert.ToHexString(SHA256.HashData(body.ToArray())));
            Assert.True(frames[^1].Commit);
            Assert.Null(frames[^1].Header);
            Assert.Empty(frames[^1].Data);
            Assert.Equal(0, frames[^1].Offset);
            Assert.Equal("Staged", Assert.IsType<App.ContainerFileUploadReceiptDto>(receipt).Upload.State);
            Assert.Equal(stage.Sha256, receipt!.Upload.Sha256);
        }
        else
        {
            var request = Assert.IsType<Rpc.ContainerUploadReference>(call.Request);
            Assert.Equal(Reference.ContainerId, request.ContainerId);
            Assert.Equal(Reference.InstanceId, request.InstanceId);
            Assert.Equal(Operation.ToString("N"), request.OperationId);
            Assert.Equal(Reference.Owner, request.Owner);
            Assert.Equal(Reference.ExpectedTargetProof, request.ExpectedTargetProof);
            AssertContext(request.Context);
            Assert.NotNull(receipt);
            Assert.Equal(Reference.ContainerId, receipt.ContainerId);
            Assert.Equal(Reference.InstanceId, receipt.InstanceId);
            Assert.Equal(Operation, receipt.OperationId);
            Assert.Equal(Reference.ExpectedTargetProof, receipt.TargetProof);
            if (action == "Publish")
            {
                Assert.Equal("Published", receipt.Upload.State);
                Assert.True(receipt.Upload.PreviousRetained);
                Assert.Equal("database-platform.backup.hdd", receipt.Upload.HddBackup!.StorageId);
                Assert.Equal(new string('D', 64), receipt.Upload.HddBackup.SourceVersion);
            }
        }
    }

    [Theory]
    [InlineData("short")][InlineData("long")][InlineData("digest")]
    public async Task InvalidLocalBodyNeverSendsCommitOrReportsStaged(string variant)
    {
        await using var harness = await Harness.Start(minor: 40, cancellationFence: true);
        var content = RandomNumberGenerator.GetBytes(32768 + 7);
        var request = Stage(content);
        request = variant switch
        {
            "short" => request with { SizeBytes = request.SizeBytes + 1 },
            "long" => request with { SizeBytes = request.SizeBytes - 1 },
            _ => request with { Sha256 = new('F', 64) }
        };
        var error = await Assert.ThrowsAsync<ArgumentException>(() => harness.Gateway.StageContainerFileAsync(
            request, BytesAfterHeader(harness, content), default));
        Assert.Equal(variant == "long" ? "CONTAINER_UPLOAD_SIZE_MISMATCH" : "CONTAINER_UPLOAD_CHECKSUM_MISMATCH", error.Message);
        await harness.StageFinished.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.DoesNotContain(harness.Frames, frame => frame.Commit);
        Assert.Single(harness.Calls);
    }

    [Theory]
    [InlineData(StatusCode.NotFound, "CONTAINER_UPLOAD_NOT_FOUND", true)]
    [InlineData(StatusCode.NotFound, "CONTAINER_FILES_NOT_FOUND", false)]
    [InlineData(StatusCode.NotFound, "FILES_BACKUP_NOT_FOUND", false)]
    [InlineData(StatusCode.FailedPrecondition, "FILES_BACKUP_INCOMPLETE", false)]
    public async Task OnlyExplicitUploadNotFoundMapsToNull(StatusCode status, string detail, bool missing)
    {
        await using var harness = await Harness.Start(minor: 40, cancellationFence: true);
        harness.InspectFailure = new(status, detail);
        if (missing) Assert.Null(await harness.Gateway.InspectContainerUploadAsync(Reference, default));
        else
        {
            var error = await Assert.ThrowsAsync<RpcException>(() => harness.Gateway.InspectContainerUploadAsync(Reference, default));
            Assert.Equal(status, error.StatusCode);
            Assert.Equal(detail, error.Status.Detail);
        }
    }

    [Fact]
    public async Task PublishedReceiptClaimingPreviousRetentionWithoutHddProofIsInvalidRatherThanMissing()
    {
        await using var harness = await Harness.Start(minor: 40, cancellationFence: true);
        harness.InspectWithoutBackup = true;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Gateway.InspectContainerUploadAsync(Reference, default));
        Assert.Equal("CONTAINER_UPLOAD_OUTPUT_INVALID", error.Message);
    }

    private static void AssertLocation(Rpc.ContainerFileLocation value)
    {
        Assert.Equal(Location.ContainerId, value.ContainerId);
        Assert.Equal(Location.InstanceId, value.InstanceId);
        Assert.Equal(Location.Path, value.Path);
    }
    private static void AssertContext(Rpc.RequestContext value)
    {
        Assert.Equal(Context.ActorSubject, value.ActorSubject);
        Assert.Equal(Context.TraceId, value.TraceId);
        Assert.Equal(Context.JobId.ToString("N"), value.JobId);
        Assert.Equal(Context.IdempotencyKey.ToString("N"), value.IdempotencyKey);
        Assert.Empty(value.Capability);
    }
    private static async IAsyncEnumerable<ReadOnlyMemory<byte>> Bytes(byte[] content)
    {
        await Task.Yield();
        yield return content;
    }
    private static async IAsyncEnumerable<ReadOnlyMemory<byte>> BytesAfterHeader(Harness harness, byte[] content)
    {
        // Ensure the server has accepted the stream before triggering a local
        // SDK failure, so cancellation/no-commit observations are meaningful.
        await harness.HeaderSeen.Task.WaitAsync(TimeSpan.FromSeconds(10));
        yield return content;
    }

    public sealed record Captured(string Action, IMessage Request, string Token, string BodyHash);

    public sealed class Harness : IAsyncDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "wd-up-" + Guid.NewGuid().ToString("N")[..10]);
        private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
        private WebApplication _server = null!;
        public AgentGateway Gateway { get; private set; } = null!;
        public uint Major { get; private init; }
        public uint Minor { get; private init; }
        public bool Capability { get; private init; }
        public bool CancellationFence { get; private init; }
        public bool RecoverySeal { get; private init; }
        public bool Restoration { get; private init; }
        public bool HistorySources { get; private init; }
        public string? InvalidRecovery { get; set; }
        public ConcurrentQueue<Captured> Calls { get; } = new();
        public ConcurrentQueue<Rpc.ContainerUploadFrame> Frames { get; } = new();
        public TaskCompletionSource<bool> HeaderSeen { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> StageFinished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool UploadReachedEof { get; set; }
        public Status? InspectFailure { get; set; }
        public bool InspectWithoutBackup { get; set; }
        public bool ReturnPrevented { get; set; }
        public string? InvalidFence { get; set; }

        public static async Task<Harness> Start(uint major = 1, uint minor = 39, bool capability = true, bool cancellationFence = false, bool recoverySeal = false, bool restoration = false, bool historySources = false)
        {
            var harness = new Harness { Major = major, Minor = minor, Capability = capability, CancellationFence = cancellationFence, RecoverySeal = recoverySeal, Restoration = restoration, HistorySources = historySources };
            if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Private UDS transport fixture requires Linux.");
            Directory.CreateDirectory(harness._directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var socket = Path.Combine(harness._directory, "agent.sock");
            var key = Path.Combine(harness._directory, "fixture.key");
            File.WriteAllBytes(key, harness._key);
            try
            {
                var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
                builder.Logging.ClearProviders();
                builder.WebHost.ConfigureKestrel(options => options.ListenUnixSocket(socket, listen => listen.Protocols = HttpProtocols.Http2));
                builder.Services.AddGrpc();
                builder.Services.AddSingleton(harness);
                harness._server = builder.Build();
                harness._server.MapGrpcService<FakeAgent>();
                harness._server.MapGrpcService<FakeWrites>();
                await harness._server.StartAsync();
                harness.Gateway = new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                    { ["Agent:SocketPath"] = socket, ["Agent:CapabilityKeyFile"] = key }).Build());
                return harness;
            }
            catch
            {
                await harness.DisposeAsync();
                throw;
            }
        }

        public void Capture(string action, IMessage request, ServerCallContext context)
        {
            var token = context.RequestHeaders.GetValue("x-whaledeck-capability") ?? "";
            var bodyHash = context.RequestHeaders.GetValue("x-whaledeck-request-sha256") ?? "";
            var captured = new Captured(action, request, token, bodyHash);
            AssertSignature(captured);
            Calls.Enqueue(captured);
        }

        public void AssertSignature(Captured call)
        {
            // Calculate the documented domain and bytes independently from the
            // production ContainerFileWriteBinding helper.
            using var contentHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            contentHash.AppendData(Encoding.UTF8.GetBytes("whaledeck-container-file-request-v1\n" + call.Request.Descriptor.FullName + "\n"));
            contentHash.AppendData(call.Request.ToByteArray());
            Assert.Equal(Convert.ToHexString(contentHash.GetHashAndReset()), call.BodyHash);
            var pieces = call.Token.Split('.');
            Assert.Equal(3, pieces.Length);
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var expires = long.Parse(pieces[0], CultureInfo.InvariantCulture);
            Assert.InRange(expires - now, 1, 60);
            Assert.True(Guid.TryParseExact(pieces[1], "N", out var nonce));
            Assert.NotEqual(Guid.Empty, nonce);
            string Mac(string method) => Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(
                "whaledeck-container-file-capability-v1\n" + pieces[0] + "." + pieces[1] + "." + method + "." + call.BodyHash)));
            Assert.Equal(Mac(Service + call.Action), pieces[2]);
            Assert.NotEqual(Mac(Service + (call.Action == "Inspect" ? "Discard" : "Inspect")), pieces[2]);
            Assert.NotEqual(Mac("/whaledeck.agent.v1.FileTransferService/" + call.Action), pieces[2]);
        }

        public async ValueTask DisposeAsync()
        {
            Gateway?.Dispose();
            if (_server is not null)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await _server.StopAsync(timeout.Token);
                await _server.DisposeAsync();
            }
            CryptographicOperations.ZeroMemory(_key);
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
    }

    public sealed class FakeAgent(Harness harness) : Rpc.AgentService.AgentServiceBase
    {
        public override Task<Rpc.CapabilitiesResponse> GetCapabilities(Google.Protobuf.WellKnownTypes.Empty request, ServerCallContext context)
        {
            var result = new Rpc.CapabilitiesResponse { ProtocolMajor = harness.Major, ProtocolMinor = harness.Minor };
            if (harness.Capability) result.Capabilities.Add("docker.files.bound-uploads");
            if (harness.CancellationFence) result.Capabilities.Add("docker.files.upload-cancel-fence");
            if (harness.RecoverySeal) result.Capabilities.Add("docker.files.upload-recovery-seal");
            if (harness.Restoration) result.Capabilities.Add("docker.files.restore-attributes");
            if (harness.HistorySources) result.Capabilities.AddRange(["docker.files.history", "docker.files.history-source"]);
            return Task.FromResult(result);
        }
    }

    public sealed class FakeWrites(Harness harness) : Rpc.ContainerFileWriteService.ContainerFileWriteServiceBase
    {
        public override Task<Rpc.ContainerUploadRecoveryObservation> ObserveRecovery(Rpc.ContainerUploadReference request, ServerCallContext context)
        { harness.Capture("ObserveRecovery", request.Clone(), context); return Task.FromResult(Recovery(request, "")); }
        public override Task<Rpc.ContainerUploadRecoveryObservation> SealRecovery(Rpc.ContainerUploadRecoveryRequest request, ServerCallContext context)
        { harness.Capture("SealRecovery", request.Clone(), context); return Task.FromResult(Recovery(request.Original, request.RecoveryId)); }
        private Rpc.ContainerUploadRecoveryObservation Recovery(Rpc.ContainerUploadReference original, string seal)
        {
            var result = new Rpc.ContainerUploadRecoveryObservation { ContainerId = original.ContainerId, InstanceId = original.InstanceId,
                OperationId = original.OperationId, TargetProof = original.ExpectedTargetProof, RootState = "Missing",
                OriginalRecordHash = "B73B451CAC7D3B4D6CD095A4CB10A7F062D17ACA014FD693F2C99DD5F542441A", Sealed = seal.Length != 0, SealId = seal };
            switch (harness.InvalidRecovery)
            {
                case "cid": result.ContainerId = new('b',64); break;
                case "instance": result.InstanceId = result.InstanceId.Replace("777:", "778:", StringComparison.Ordinal); break;
                case "operation": result.OperationId = Guid.NewGuid().ToString("N"); break;
                case "proof": result.TargetProof = new('F',64); break;
                case "current": result.BindingCurrent = true; break;
                case "state": result.RootState = "Completed"; break;
                case "retained": result.RetainedObjects = true; break;
                case "published": result.MayHavePublished = true; break;
                case "seal": result.Sealed = true; break;
                case "record": result.OriginalRecordHash = "bad"; break;
                case "wrong-seal": result.SealId = Guid.NewGuid().ToString("N"); break;
            }
            result.ObservationHash = WhaleDeck.Contracts.ContainerUploadRecoveryBinding.ObservationHash(original, result);
            if (harness.InvalidRecovery == "digest") result.ObservationHash = new('F',64);
            return result;
        }

        public override Task<Rpc.ContainerUploadTarget> InspectTarget(Rpc.ContainerFileLocation request, ServerCallContext context)
        {
            harness.Capture("InspectTarget", request.Clone(), context);
            return Task.FromResult(new Rpc.ContainerUploadTarget { Location = request.Clone(), TargetProof = Reference.ExpectedTargetProof,
                ExpectedParentVersion = new('E', 64), ExpectedVersion = new('D', 64), CurrentSizeBytes = 17 });
        }

        public override async Task<Rpc.ContainerUploadReceipt> StageUpload(IAsyncStreamReader<Rpc.ContainerUploadFrame> requestStream, ServerCallContext context)
        {
            try
            {
                Assert.True(await requestStream.MoveNext(context.CancellationToken));
                var first = requestStream.Current.Clone();
                harness.Frames.Enqueue(first);
                Assert.NotNull(first.Header);
                harness.Capture("StageUpload", first.Header.Clone(), context);
                harness.HeaderSeen.TrySetResult(true);
                while (await requestStream.MoveNext(context.CancellationToken)) harness.Frames.Enqueue(requestStream.Current.Clone());
                harness.UploadReachedEof = true;
                var frames = harness.Frames.ToArray();
                if (!frames[^1].Commit || frames.Skip(1).Count(frame => frame.Commit) != 1)
                    throw new RpcException(new(StatusCode.InvalidArgument, "FIXTURE_COMMIT_REQUIRED"));
                var header = first.Header;
                return Receipt(new() { ContainerId = header.Location.ContainerId, InstanceId = header.Location.InstanceId,
                    OperationId = header.OperationId, Owner = header.Owner, ExpectedTargetProof = header.ExpectedTargetProof,
                    Context = header.Context.Clone() }, "Staged", header.SizeBytes, header.Sha256);
            }
            finally { harness.StageFinished.TrySetResult(true); }
        }

        public override Task<Rpc.ContainerUploadReceipt> Publish(Rpc.ContainerUploadReference request, ServerCallContext context)
        {
            harness.Capture("Publish", request.Clone(), context);
            return Task.FromResult(Receipt(request, "Published"));
        }
        public override Task<Rpc.ContainerUploadReceipt> Inspect(Rpc.ContainerUploadReference request, ServerCallContext context)
        {
            harness.Capture("Inspect", request.Clone(), context);
            if (harness.InspectFailure is { } status) throw new RpcException(status);
            var result = Receipt(request, harness.InspectWithoutBackup ? "Published" : "Staged");
            if (harness.InspectWithoutBackup) result.Backup = null;
            return Task.FromResult(result);
        }
        public override Task<Rpc.ContainerUploadReceipt> Discard(Rpc.ContainerUploadReference request, ServerCallContext context)
        {
            harness.Capture("Discard", request.Clone(), context);
            if (harness.ReturnPrevented)
            {
                var result = Receipt(request, "Prevented", size: 0, digest: Convert.ToHexString(SHA256.HashData([])));
                if (harness.InvalidFence == "size") result.Upload.SizeBytes = 1;
                if (harness.InvalidFence == "digest") result.Upload.Sha256 = new('A', 64);
                if (harness.InvalidFence == "backup") result.Backup = Receipt(request, "Published").Backup;
                if (harness.InvalidFence == "version") result.Upload.Version = new('A', 64);
                if (harness.InvalidFence == "state") result.Upload.State = "Staged";
                return Task.FromResult(result);
            }
            return Task.FromResult(Receipt(request, "Discarded"));
        }

        private static Rpc.ContainerUploadReceipt Receipt(Rpc.ContainerUploadReference request, string state, long size = 17, string? digest = null)
        {
            var published = state == "Published";
            var result = new Rpc.ContainerUploadReceipt { ContainerId = request.ContainerId, InstanceId = request.InstanceId,
                TargetProof = request.ExpectedTargetProof, Upload = new() { OperationId = request.OperationId, State = state,
                    SizeBytes = size, Sha256 = digest ?? new string('C', 64), Version = published ? new('F', 64) : "",
                    BindingCurrent = published, PreviousRetained = published } };
            if (published) result.Backup = new() { ContainerId = request.ContainerId, InstanceId = request.InstanceId,
                TargetProof = request.ExpectedTargetProof, Proof = new() { OperationId = request.OperationId,
                    StorageId = "database-platform.backup.hdd", SourceVersion = new('D', 64), SizeBytes = 11,
                    Sha256 = new('E', 64), CreatedAtUnixMs = 1792602123000, DifferentSourceDevice = true } };
            return result;
        }
    }
}
