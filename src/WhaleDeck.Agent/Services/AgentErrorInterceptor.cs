using System.Net;
using Docker.DotNet;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace WhaleDeck.Agent.Services;

/// <summary>Separates rejected operations from transient RPC failures without exposing exception text.</summary>
public sealed class AgentErrorInterceptor : Interceptor
{
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(TRequest request, ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        try { return await continuation(request, context); }
        catch (Exception exception) when (Map(exception) is not null)
        {
            throw Map(exception)!;
        }
    }

    public override async Task ServerStreamingServerHandler<TRequest, TResponse>(TRequest request,
        IServerStreamWriter<TResponse> responseStream, ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        try { await continuation(request, responseStream, context); }
        catch (Exception exception) when (Map(exception) is not null)
        {
            throw Map(exception)!;
        }
    }

    public override async Task DuplexStreamingServerHandler<TRequest, TResponse>(IAsyncStreamReader<TRequest> requestStream,
        IServerStreamWriter<TResponse> responseStream, ServerCallContext context, DuplexStreamingServerMethod<TRequest, TResponse> continuation)
    {
        try { await continuation(requestStream, responseStream, context); }
        catch (Exception exception) when (Map(exception) is not null) { throw Map(exception)!; }
    }

    public override async Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(IAsyncStreamReader<TRequest> requestStream,
        ServerCallContext context, ClientStreamingServerMethod<TRequest, TResponse> continuation)
    {
        try { return await continuation(requestStream, context); }
        catch (Exception exception) when (Map(exception) is not null) { throw Map(exception)!; }
    }

    private static RpcException? Map(Exception exception)
    {
        var code = exception switch
        {
            ArgumentException => StatusCode.InvalidArgument,
            KeyNotFoundException => StatusCode.NotFound,
            UnauthorizedAccessException => StatusCode.PermissionDenied,
            InvalidOperationException => StatusCode.FailedPrecondition,
            DockerApiException docker when docker.StatusCode == HttpStatusCode.NotFound => StatusCode.NotFound,
            DockerApiException docker when docker.StatusCode == HttpStatusCode.BadRequest => StatusCode.InvalidArgument,
            DockerApiException docker when docker.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => StatusCode.PermissionDenied,
            DockerApiException docker when docker.StatusCode == HttpStatusCode.Conflict => StatusCode.FailedPrecondition,
            _ => (StatusCode?)null
        };
        return code is { } status ? new RpcException(new Status(status, "The Agent rejected the operation. Inspect the resource and operation prerequisites.")) : null;
    }
}
