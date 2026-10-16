using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace WhaleDeck.Agent.Services;

public sealed class CapabilityValidator(IConfiguration configuration)
{
    private const long MaximumLifetimeSeconds = 60;
    private readonly string _keyFile = configuration["Agent:CapabilityKeyFile"]
        ?? "/etc/whaledeck/agent-capability.key";
    private readonly ConcurrentDictionary<string, long> _usedNonces = new(StringComparer.Ordinal);

    public bool Validate(string? token, string method)
    {
        if (method.EndsWith("/GetCapabilities", StringComparison.Ordinal) ||
            method.EndsWith("/GetHealth", StringComparison.Ordinal) ||
            method.EndsWith("/WatchHealth", StringComparison.Ordinal))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(token) || !File.Exists(_keyFile))
        {
            return false;
        }

        var parts = token.Split('.', 3);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (parts.Length != 3 || !long.TryParse(parts[0], out var expiresAt) ||
            expiresAt < now || expiresAt - now > MaximumLifetimeSeconds ||
            parts[1].Length is < 16 or > 128)
        {
            return false;
        }

        var payload = $"{parts[0]}.{parts[1]}.{method}";
        using var hmac = new HMACSHA256(File.ReadAllBytes(_keyFile));
        var expected = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
        var valid = CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected),
            Encoding.ASCII.GetBytes(parts[2].ToUpperInvariant()));
        if (!valid)
        {
            return false;
        }

        foreach (var used in _usedNonces)
        {
            if (used.Value < now)
            {
                _usedNonces.TryRemove(used.Key, out _);
            }
        }

        return _usedNonces.TryAdd(parts[1], expiresAt);
    }
}

public sealed class CapabilityInterceptor(CapabilityValidator validator) : Interceptor
{
    public override Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        Authorize(context);
        return continuation(request, context);
    }

    public override Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        Authorize(context);
        return continuation(request, responseStream, context);
    }

    public override Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(IAsyncStreamReader<TRequest> requestStream,
        ServerCallContext context, ClientStreamingServerMethod<TRequest, TResponse> continuation)
    {
        Authorize(context);
        return continuation(requestStream, context);
    }

    public override Task DuplexStreamingServerHandler<TRequest, TResponse>(IAsyncStreamReader<TRequest> requestStream,
        IServerStreamWriter<TResponse> responseStream, ServerCallContext context, DuplexStreamingServerMethod<TRequest, TResponse> continuation)
    {
        Authorize(context);
        return continuation(requestStream, responseStream, context);
    }

    private void Authorize(ServerCallContext context)
    {
        var token = context.RequestHeaders.GetValue("x-whaledeck-capability");
        if (!validator.Validate(token, context.Method))
        {
            throw new RpcException(new Status(StatusCode.PermissionDenied, "Agent capability is missing or invalid."));
        }
    }
}
