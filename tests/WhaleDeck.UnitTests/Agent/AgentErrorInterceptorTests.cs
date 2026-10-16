using Grpc.Core;
using WhaleDeck.Agent.Services;

namespace WhaleDeck.UnitTests.Agent;

public sealed class AgentErrorInterceptorTests
{
    [Theory]
    [InlineData("argument", StatusCode.InvalidArgument)]
    [InlineData("missing", StatusCode.NotFound)]
    [InlineData("forbidden", StatusCode.PermissionDenied)]
    [InlineData("precondition", StatusCode.FailedPrecondition)]
    public async Task RejectedOperationsProduceStableStatusWithoutExceptionText(string kind, StatusCode status)
    {
        Exception failure = kind switch
        {
            "argument" => new ArgumentException("sensitive-input"),
            "missing" => new KeyNotFoundException("private-path"),
            "forbidden" => new UnauthorizedAccessException("secret-value"),
            _ => new InvalidOperationException("private-resource")
        };
        var interceptor = new AgentErrorInterceptor();
        var error = await Assert.ThrowsAsync<RpcException>(() => interceptor.UnaryServerHandler<string, string>(
            "request", null!, (_, _) => Task.FromException<string>(failure)));
        Assert.Equal(status, error.StatusCode);
        Assert.DoesNotContain(failure.Message, error.Status.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TransportFailureRemainsTransient()
    {
        var failure = new RpcException(new Status(StatusCode.Unavailable, "Agent unavailable"));
        var error = await Assert.ThrowsAsync<RpcException>(() => new AgentErrorInterceptor().UnaryServerHandler<string, string>(
            "request", null!, (_, _) => Task.FromException<string>(failure)));
        Assert.Same(failure, error);
    }

    [Fact]
    public async Task DuplexRejectedInputNeverExposesTerminalContentInStatus()
    {
        var error = await Assert.ThrowsAsync<RpcException>(() => new AgentErrorInterceptor().DuplexStreamingServerHandler<string, string>(
            null!, null!, null!, (_, _, _) => Task.FromException(new ArgumentException("private terminal input"))));
        Assert.Equal(StatusCode.InvalidArgument, error.StatusCode);
        Assert.DoesNotContain("private terminal input", error.Status.Detail, StringComparison.Ordinal);
    }
}
