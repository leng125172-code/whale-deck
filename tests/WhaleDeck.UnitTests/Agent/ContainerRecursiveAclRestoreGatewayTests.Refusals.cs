using Grpc.Core;
using App = WhaleDeck.Application.Models;

namespace WhaleDeck.UnitTests.Agent;

public sealed partial class ContainerRecursiveAclRestoreGatewayTests
{
    [Theory]
    [InlineData(StatusCode.Aborted, "CONTAINER_RECURSIVE_ACL_RESTORE_SOURCE_CHANGED")]
    [InlineData(StatusCode.Aborted, "CONTAINER_RECURSIVE_ACL_RESTORE_SOURCE_TREE_MISMATCH")]
    [InlineData(StatusCode.FailedPrecondition, "CONTAINER_RECURSIVE_ACL_RESTORE_SOURCE_NOT_FOUND")]
    [InlineData(StatusCode.FailedPrecondition, "CONTAINER_RECURSIVE_ACL_RESTORE_SOURCE_INVALID")]
    public async Task KnownSourceFenceErrorsStayDistinctAndNeverClaimZeroWrites(StatusCode status, string code)
    {
        using var h=new Harness {Status=status,Detail=code};
        var plan=await Assert.ThrowsAsync<App.ContainerRecursiveAclRestorePreflightRejectedException>(()=>h.Gateway.PlanContainerRecursiveAclRestoreAsync(Actor,Command,default));
        Assert.Equal(code,plan.Code);Assert.True(plan.IsSourceRejection);
        var apply=await Assert.ThrowsAsync<App.ContainerRecursiveAclRestorePreflightRejectedException>(()=>h.Gateway.ApplyContainerRecursiveAclRestoreAsync(Operation,default));
        Assert.Equal(code,apply.Code);Assert.True(apply.IsSourceRejection);
    }

    [Theory]
    [InlineData(StatusCode.Aborted,"CONTAINER_RECURSIVE_ACL_RESTORE_TREE_CHANGED","CONTAINER_RECURSIVE_ACL_RESTORE_VERSION_CONFLICT")]
    [InlineData(StatusCode.InvalidArgument,"CONTAINER_RECURSIVE_ACL_RESTORE_PLAN_INVALID","CONTAINER_RECURSIVE_ACL_RESTORE_PLAN_INVALID")]
    [InlineData(StatusCode.PermissionDenied,"CONTAINER_UPLOAD_PUBLISH_READ_ONLY","CONTAINER_RECURSIVE_ACL_RESTORE_PROTECTED")]
    [InlineData(StatusCode.FailedPrecondition,"CONTAINER_FILES_INSTANCE_CHANGED","CONTAINER_RECURSIVE_ACL_RESTORE_TARGET_CHANGED")]
    [InlineData(StatusCode.ResourceExhausted,"CONTAINER_RECURSIVE_ACL_RESTORE_TREE_LIMIT","CONTAINER_RECURSIVE_ACL_RESTORE_TREE_LIMIT")]
    [InlineData(StatusCode.ResourceExhausted,"CONTAINER_RECURSIVE_ACL_RESTORE_MANIFEST_LIMIT","CONTAINER_RECURSIVE_ACL_RESTORE_MANIFEST_LIMIT")]
    public async Task OnlyExactPlanningRefusalsBecomeFiniteBusinessCodes(StatusCode status,string detail,string code)
    {
        using var h=new Harness {Status=status,Detail=detail};
        var error=await Assert.ThrowsAsync<App.ContainerRecursiveAclRestorePreflightRejectedException>(()=>h.Gateway.PlanContainerRecursiveAclRestoreAsync(Actor,Command,default));
        Assert.Equal(code,error.Code);Assert.False(error.IsSourceRejection);
        // A write/transport failure cannot be mislabeled as a safe preflight.
        await Assert.ThrowsAsync<RpcException>(()=>h.Gateway.ApplyContainerRecursiveAclRestoreAsync(Operation,default));
    }

    [Theory]
    [InlineData(StatusCode.Unavailable,"CONTAINER_RECURSIVE_ACL_RESTORE_UNAVAILABLE")]
    [InlineData(StatusCode.ResourceExhausted,"unknown private text")]
    [InlineData(StatusCode.FailedPrecondition,"CONTAINER_RECURSIVE_ACL_RESTORE_SOURCE_NOT_FOUND private")]
    [InlineData(StatusCode.NotFound,"CONTAINER_RECURSIVE_ACL_RESTORE_SOURCE_NOT_FOUND")]
    public async Task UnknownTextOrWrongStatusCannotBecomeADeterministicSourceRefusal(StatusCode status,string detail)
    {
        using var h=new Harness {Status=status,Detail=detail};
        await Assert.ThrowsAsync<RpcException>(()=>h.Gateway.PlanContainerRecursiveAclRestoreAsync(Actor,Command,default));
        await Assert.ThrowsAsync<RpcException>(()=>h.Gateway.ApplyContainerRecursiveAclRestoreAsync(Operation,default));
    }

    [Theory][InlineData(false)][InlineData(true)]
    public async Task ObservationAndFenceDoNotDependOnRevalidatingHistoricalSource(bool discard)
    {
        using var h=new Harness {Status=StatusCode.FailedPrecondition,Detail="CONTAINER_RECURSIVE_ACL_RESTORE_SOURCE_NOT_FOUND"};
        if(discard)await Assert.ThrowsAsync<RpcException>(()=>h.Gateway.DiscardContainerRecursiveAclRestoreAsync(Operation,default));
        else await Assert.ThrowsAsync<RpcException>(()=>h.Gateway.InspectContainerRecursiveAclRestoreAsync(Operation,default));
    }
}
