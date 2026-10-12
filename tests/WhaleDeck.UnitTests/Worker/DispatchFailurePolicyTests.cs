using Grpc.Core;
using WhaleDeck.Worker;

namespace WhaleDeck.UnitTests.Worker;

public sealed class DispatchFailurePolicyTests
{
    [Theory]
    [InlineData(StatusCode.InvalidArgument, "AGENT_INVALID_ARGUMENT")]
    [InlineData(StatusCode.NotFound, "AGENT_RESOURCE_NOT_FOUND")]
    [InlineData(StatusCode.PermissionDenied, "AGENT_OPERATION_FORBIDDEN")]
    [InlineData(StatusCode.FailedPrecondition, "AGENT_PRECONDITION_FAILED")]
    [InlineData(StatusCode.Unimplemented, "AGENT_CAPABILITY_UNSUPPORTED")]
    public void RejectedOperationsFailWithoutRetrying(StatusCode status, string errorCode)
    {
        var result = DispatchFailurePolicy.Classify(new RpcException(new Status(status, "private information")), 1);
        Assert.True(result.Terminal);
        Assert.Equal(errorCode, result.ErrorCode);
        Assert.Equal("AgentRejected", result.Phase);
    }

    [Theory]
    [InlineData(StatusCode.Unavailable)]
    [InlineData(StatusCode.DeadlineExceeded)]
    [InlineData(StatusCode.ResourceExhausted)]
    [InlineData(StatusCode.Unknown)]
    public void TransientFailuresKeepBoundedRetryBudget(StatusCode status)
    {
        var error = new RpcException(new Status(status, "transient"));
        Assert.False(DispatchFailurePolicy.Classify(error, 1).Terminal);
        Assert.False(DispatchFailurePolicy.Classify(error, 7).Terminal);
        Assert.True(DispatchFailurePolicy.Classify(error, 8).Terminal);
    }

    [Fact]
    public void ExpiredInputIsNeverAutomaticallyRetried()
    {
        var result = DispatchFailurePolicy.Classify(new InvalidOperationException("The staged secret expired before dispatch."), 1);
        Assert.True(result.Terminal);
        Assert.Equal("ONE_TIME_SECRET_EXPIRED", result.ErrorCode);
    }
}
