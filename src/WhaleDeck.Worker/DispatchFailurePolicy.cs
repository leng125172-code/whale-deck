using Grpc.Core;

namespace WhaleDeck.Worker;

public sealed record DispatchFailure(bool Terminal, string ErrorCode, string Phase);

public static class DispatchFailurePolicy
{
    public static DispatchFailure Classify(Exception exception, int attempts)
    {
        if (exception is InvalidOperationException && exception.Message.Contains("staged secret expired", StringComparison.Ordinal))
            return new(true, "ONE_TIME_SECRET_EXPIRED", "SecretExpired");
        if (exception is HttpRequestException { StatusCode: { } status } && (int)status is >= 400 and < 500 and not 408 and not 429)
            return new(true, "IDENTITY_PROVIDER_REJECTED", "ProviderRejected");
        if (exception is RpcException rpc && rpc.StatusCode is StatusCode.InvalidArgument or StatusCode.NotFound or
            StatusCode.PermissionDenied or StatusCode.FailedPrecondition or StatusCode.Unimplemented)
            return new(true, rpc.StatusCode switch
            {
                StatusCode.NotFound => "AGENT_RESOURCE_NOT_FOUND",
                StatusCode.PermissionDenied => "AGENT_OPERATION_FORBIDDEN",
                StatusCode.InvalidArgument => "AGENT_INVALID_ARGUMENT",
                StatusCode.Unimplemented => "AGENT_CAPABILITY_UNSUPPORTED",
                _ => "AGENT_PRECONDITION_FAILED"
            }, "AgentRejected");
        return new(attempts >= 8, "OUTBOX_DISPATCH_FAILED", "DispatchRetriesExhausted");
    }
}
