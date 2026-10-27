using System.Text.Json;
using Grpc.Core;
using WhaleDeck.Agent.Services;
using WhaleDeck.Application.Services;
using WhaleDeck.Contracts;
using Rpc = WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.UnitTests.Agent;

public sealed partial class ContainerUploadGatewayTests
{
    private static WhaleDeck.Application.Models.ContainerFileRestoreSourceDto RestorationSource =>
        new("dd0a8865343e49bdb01a72680131bf39", new('C', 64), "历史/原始.bin");

    [Fact]
    public async Task ActualAgentAdvertisesIndependentAttributeAndExistingHistoryCapabilities()
    {
        var capabilities = await new AgentGrpcService(null!).GetCapabilities(new(), null!);
        Assert.Equal(1U, capabilities.ProtocolMajor); Assert.Equal(43U, capabilities.ProtocolMinor);
        Assert.Contains("docker.files.restore-attributes", capabilities.Capabilities);
        Assert.Contains("docker.files.history-source", capabilities.Capabilities);
        Assert.Contains("docker.files.upload-recovery-seal", capabilities.Capabilities);
    }

    [Theory]
    [InlineData(1U, 42U, true, true)]
    [InlineData(1U, 43U, false, true)]
    [InlineData(1U, 43U, true, false)]
    [InlineData(2U, 43U, true, true)]
    public async Task AttributesRequireIndependentCapabilityAndMinorBeforeBodyEnumeration(uint major, uint minor, bool capability, bool history)
    {
        await using var harness = await Harness.Start(major, minor, cancellationFence: true, restoration: capability, historySources: history);
        var read = false;
        async IAsyncEnumerable<ReadOnlyMemory<byte>> Source() { read = true; await Task.Yield(); yield return new byte[] { 42 }; }
        var error = await Assert.ThrowsAsync<RpcException>(() => harness.Gateway.StageContainerFileAsync(
            Stage([42]) with { RestoreSource = RestorationSource }, Source(), default));
        Assert.Equal(StatusCode.Unimplemented, error.StatusCode); Assert.False(read); Assert.Empty(harness.Calls); Assert.Empty(harness.Frames);
    }

    [Fact]
    public async Task RestorationHeaderBindsExactSourceWhileKeepingIndependentDestinationAndBody()
    {
        await using var harness = await Harness.Start(minor: 43, cancellationFence: true, restoration: true, historySources: true);
        byte[] bytes = [1, 2, 3];
        var result = await harness.Gateway.StageContainerFileAsync(Stage(bytes) with { RestoreSource = RestorationSource }, Bytes(bytes), default);
        Assert.Equal("Staged", result.Upload.State);
        var call = Assert.Single(harness.Calls); harness.AssertSignature(call);
        var header = Assert.IsType<Rpc.ContainerUploadHeader>(call.Request);
        Assert.Equal(Location.Path, header.Location.Path); Assert.Equal(RestorationSource.Path, header.RestoreSource.Path);
        Assert.Equal(RestorationSource.CaptureId, header.RestoreSource.CaptureId); Assert.Equal(RestorationSource.CaptureHash, header.RestoreSource.CaptureHash);
        foreach (var field in new[] { "capture", "hash", "path" })
        {
            var changed = header.Clone();
            if (field == "capture") changed.RestoreSource.CaptureId = Guid.NewGuid().ToString("N");
            if (field == "hash") changed.RestoreSource.CaptureHash = new('F', 64);
            if (field == "path") changed.RestoreSource.Path = "elsewhere.bin";
            Assert.NotEqual(call.BodyHash, ContainerFileWriteBinding.Hash(changed));
        }
        using var input = JsonDocument.Parse(ContainerUploadPolicy.Open(new() { Header = header }));
        var source = input.RootElement.GetProperty("restoreSource");
        Assert.Equal(3, source.EnumerateObject().Count()); Assert.Equal(RestorationSource.Path, source.GetProperty("path").GetString());
        Assert.False(source.TryGetProperty("uid", out _)); Assert.False(source.TryGetProperty("mode", out _));
    }

    [Theory]
    [InlineData("path")][InlineData("hash")][InlineData("capture")]
    public async Task InvalidRestorationSourceCannotReachAgent(string variant)
    {
        await using var harness = await Harness.Start(minor: 43, cancellationFence: true, restoration: true, historySources: true);
        var source = variant switch
        {
            "path" => RestorationSource with { Path = "../escape" },
            "hash" => RestorationSource with { CaptureHash = new('a', 64) },
            _ => RestorationSource with { CaptureId = Guid.Empty.ToString("N") }
        };
        await Assert.ThrowsAsync<ArgumentException>(() => harness.Gateway.StageContainerFileAsync(Stage([42]) with { RestoreSource = source }, Bytes([42]), default));
        Assert.Empty(harness.Calls); Assert.Empty(harness.Frames);
        var stage = Stage([42]) with { RestoreSource = source };
        Assert.Throws<ArgumentException>(() => ContainerFileUploadValidation.Stage(stage));
    }

    [Theory]
    [InlineData("path")][InlineData("hash")][InlineData("capture")]
    public void AgentAlsoRejectsMalformedSourceBeforeFixedHelper(string variant)
    {
        var header = new Rpc.ContainerUploadHeader { Location = new() { ContainerId = Location.ContainerId, InstanceId = Location.InstanceId, Path = Location.Path },
            OperationId = Operation.ToString("N"), Owner = Reference.Owner, SizeBytes = 1, Sha256 = new('A', 64),
            ExpectedVersion = "ABSENT", ExpectedParentVersion = new('B', 64), ExpectedTargetProof = Reference.ExpectedTargetProof,
            Context = new() { ActorSubject = Context.ActorSubject, TraceId = Context.TraceId, JobId = Context.JobId.ToString("N"), IdempotencyKey = Context.IdempotencyKey.ToString("N") },
            RestoreSource = new() { CaptureId = RestorationSource.CaptureId, CaptureHash = RestorationSource.CaptureHash, Path = RestorationSource.Path } };
        if (variant == "path") header.RestoreSource.Path = "/host/path";
        if (variant == "hash") header.RestoreSource.CaptureHash = "bad";
        if (variant == "capture") header.RestoreSource.CaptureId = Guid.Empty.ToString("N");
        Assert.Throws<ArgumentException>(() => ContainerUploadPolicy.Open(new() { Header = header }));
    }

    [Theory]
    [InlineData("CONTAINER_UPLOAD_RESTORE_OWNER_UNMAPPED", StatusCode.PermissionDenied)]
    [InlineData("CONTAINER_HISTORY_CAPTURE_CHANGED", StatusCode.Aborted)]
    [InlineData("CONTAINER_UPLOAD_RESTORE_MODE_UNSUPPORTED", StatusCode.FailedPrecondition)]
    [InlineData("CONTAINER_UPLOAD_RESTORE_ATTRIBUTES_CHANGED", StatusCode.FailedPrecondition)]
    public void RestorationFixedFailuresKeepTypedSemantics(string code, StatusCode status) =>
        Assert.Equal(status, ContainerUploadPolicy.Failure(code).StatusCode);
}
