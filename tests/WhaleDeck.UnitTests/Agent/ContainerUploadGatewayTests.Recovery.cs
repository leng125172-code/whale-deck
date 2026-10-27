using Grpc.Core;
using WhaleDeck.Application.Models;
using Rpc = WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.UnitTests.Agent;

public sealed partial class ContainerUploadGatewayTests
{
    [Theory]
    [InlineData(1U,40U,true,true,true)]
    [InlineData(2U,41U,true,true,true)]
    [InlineData(1U,41U,false,true,true)]
    [InlineData(1U,41U,true,false,true)]
    [InlineData(1U,41U,true,true,false)]
    public async Task RecoveryRequiresItsOwnVersionAndAllIndependentCapabilities(uint major, uint minor, bool upload, bool fence, bool recovery)
    {
        await using var harness = await Harness.Start(major, minor, upload, fence, recovery);
        var error = await Assert.ThrowsAsync<RpcException>(() => harness.Gateway.ObserveContainerUploadRecoveryAsync(Reference, default));
        Assert.Equal(StatusCode.Unimplemented, error.StatusCode); Assert.Empty(harness.Calls);
    }
    [Fact]
    public async Task ReadOnlyRecoveryCarriesOriginalBodySignatureAndNoNewTargetQuery()
    {
        await using var harness = await Harness.Start(minor:41,cancellationFence:true,recoverySeal:true);
        var result = await harness.Gateway.ObserveContainerUploadRecoveryAsync(Reference, default);
        Assert.Equal("Missing", result.RootState); Assert.False(result.Sealed); Assert.False(result.MayHavePublished);
        var call = Assert.Single(harness.Calls); Assert.Equal("ObserveRecovery", call.Action); harness.AssertSignature(call);
        var original = Assert.IsType<Rpc.ContainerUploadReference>(call.Request);
        Assert.Equal(Reference.Owner, original.Owner); AssertContext(original.Context);
    }
    [Fact]
    public async Task SealingBindsNewAdministratorSeparatelyWithoutRewritingTheOriginalContext()
    {
        await using var harness = await Harness.Start(minor:41,cancellationFence:true,recoverySeal:true);
        var id = Guid.NewGuid(); var current = new TerminalActor("new-recovery-admin",null,[],new('C',64),"new-recovery-trace",null);
        var result = await harness.Gateway.SealContainerUploadRecoveryAsync(Reference, id, current, new('D',64), default);
        Assert.True(result.Sealed); Assert.Equal(id,result.SealId); Assert.Equal("Missing",result.RootState);
        var call = Assert.Single(harness.Calls); Assert.Equal("SealRecovery",call.Action); harness.AssertSignature(call);
        var request = Assert.IsType<Rpc.ContainerUploadRecoveryRequest>(call.Request);
        AssertContext(request.Original.Context); Assert.Equal(Reference.Owner,request.Original.Owner);
        Assert.Equal(current.Subject,request.Context.ActorSubject); Assert.Equal(current.TraceId,request.Context.TraceId);
        Assert.Equal(id.ToString("N"),request.Context.JobId); Assert.Equal(request.RecoveryId,request.Context.IdempotencyKey);
        Assert.Equal(new string('D',64),request.ExpectedObservationHash); Assert.DoesNotContain(current.LoginBinding,request.ToString(),StringComparison.Ordinal);
    }
    [Theory]
    [InlineData("cid")][InlineData("instance")][InlineData("operation")][InlineData("proof")]
    [InlineData("current")][InlineData("state")][InlineData("retained")][InlineData("published")]
    [InlineData("seal")][InlineData("record")][InlineData("digest")]
    public async Task UntrustedHistoricalOutputCannotBecomeAnAcceptedObservation(string variant)
    {
        await using var harness = await Harness.Start(minor:41,cancellationFence:true,recoverySeal:true); harness.InvalidRecovery = variant;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Gateway.ObserveContainerUploadRecoveryAsync(Reference,default));
        Assert.Equal("CONTAINER_UPLOAD_RECOVERY_OUTPUT_INVALID",error.Message);
    }
    [Fact]
    public async Task AnotherRecoverySealCannotAcknowledgeThisAdministratorsAction()
    {
        await using var harness = await Harness.Start(minor:41,cancellationFence:true,recoverySeal:true); harness.InvalidRecovery = "wrong-seal";
        var actor = new TerminalActor("current-admin",null,[],new('C',64),"trace",null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Gateway.SealContainerUploadRecoveryAsync(Reference,Guid.NewGuid(),actor,new('D',64),default));
    }
    [Theory]
    [InlineData("binding")][InlineData("subject")][InlineData("trace")][InlineData("identity")][InlineData("digest")]
    public async Task InvalidCurrentContextCannotReachTheRecoveryRpc(string variant)
    {
        await using var harness = await Harness.Start(minor:41,cancellationFence:true,recoverySeal:true);
        var actor = new TerminalActor("current-admin",null,[],new('C',64),"trace",null);
        if (variant == "binding") actor = actor with { LoginBinding = "" };
        if (variant == "subject") actor = actor with { Subject = "\ud800" };
        if (variant == "trace") actor = actor with { TraceId = "\ud800" };
        await Assert.ThrowsAsync<ArgumentException>(() => harness.Gateway.SealContainerUploadRecoveryAsync(Reference,
            variant == "identity" ? Reference.OperationId : Guid.NewGuid(),actor,variant == "digest" ? "bad" : new('D',64),default));
        Assert.Empty(harness.Calls);
    }
}
