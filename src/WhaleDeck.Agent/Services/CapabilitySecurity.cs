using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Grpc.Core;
using Grpc.Core.Interceptors;
using WhaleDeck.Contracts;

namespace WhaleDeck.Agent.Services;

public sealed class CapabilityValidator(IConfiguration configuration)
{
    private const long MaximumLifetimeSeconds = 60;
    private readonly string _keyFile = configuration["Agent:CapabilityKeyFile"]
        ?? "/etc/whaledeck/agent-capability.key";
    private readonly ConcurrentDictionary<string, long> _usedNonces = new(StringComparer.Ordinal);

    public bool Validate(string? token, string method)
    {
        if (method.StartsWith(ContainerFileWriteBinding.Service, StringComparison.Ordinal) ||
            method.StartsWith(ContainerHistoryBinding.Service, StringComparison.Ordinal) ||
            method.StartsWith(ContainerDirectoryBinding.Service, StringComparison.Ordinal) ||
            method.StartsWith(ContainerPermissionBinding.Service, StringComparison.Ordinal) ||
            method.StartsWith(ContainerCopyBinding.Service, StringComparison.Ordinal) ||
            method.StartsWith(ContainerMoveBinding.Service, StringComparison.Ordinal)) return false;
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

    public bool ValidateContainerWrite(string? token, string method, string? requestHash)
    {
        return method.StartsWith(ContainerFileWriteBinding.Service, StringComparison.Ordinal) &&
            ValidateContentBound(token, method, requestHash, ContainerFileWriteBinding.Payload);
    }

    public bool ValidateContainerHistory(string? token, string method, string? requestHash)
    {
        return method is ContainerHistoryBinding.Service + "List" or ContainerHistoryBinding.Service + "ReadSource" &&
            ValidateContentBound(token, method, requestHash, ContainerHistoryBinding.Payload);
    }

    public bool ValidateContainerDirectory(string? token, string method, string? requestHash) =>
        method is ContainerDirectoryBinding.Service + "Plan" or ContainerDirectoryBinding.Service + "Apply" or
            ContainerDirectoryBinding.Service + "Inspect" or ContainerDirectoryBinding.Service + "Discard" &&
        ValidateContentBound(token, method, requestHash, ContainerDirectoryBinding.Payload);

    public bool ValidateContainerPermission(string? token, string method, string? requestHash) =>
        method is ContainerPermissionBinding.Service + "Plan" or ContainerPermissionBinding.Service + "Apply" or
            ContainerPermissionBinding.Service + "Inspect" or ContainerPermissionBinding.Service + "Discard" &&
        ValidateContentBound(token, method, requestHash, ContainerPermissionBinding.Payload);

    public bool ValidateContainerCopy(string? token, string method, string? requestHash) =>
        method is ContainerCopyBinding.Service + "Plan" or ContainerCopyBinding.Service + "Apply" or
            ContainerCopyBinding.Service + "Inspect" or ContainerCopyBinding.Service + "Discard" &&
        ValidateContentBound(token, method, requestHash, ContainerCopyBinding.Payload);

    public bool ValidateContainerMove(string? token, string method, string? requestHash) =>
        method is ContainerMoveBinding.Service + "Plan" or ContainerMoveBinding.Service + "Apply" or
            ContainerMoveBinding.Service + "Inspect" or ContainerMoveBinding.Service + "Discard" &&
        ValidateContentBound(token, method, requestHash, ContainerMoveBinding.Payload);

    private bool ValidateContentBound(string? token, string method, string? requestHash,
        Func<string, string, string, string, string> payload)
    {
        if (requestHash is not { Length: 64 } || requestHash.Any(c => !(char.IsAsciiDigit(c) || c is >= 'A' and <= 'F')) ||
            string.IsNullOrEmpty(token) || token.Length > 256) return false;
        var parts = token.Split('.', 3);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (parts.Length != 3 || !long.TryParse(parts[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var expires) ||
            expires < now || expires - now > MaximumLifetimeSeconds ||
            !Guid.TryParseExact(parts[1], "N", out var nonce) || nonce == Guid.Empty || nonce.ToString("N") != parts[1] ||
            parts[2].Length != 64 || parts[2].Any(c => !(char.IsAsciiDigit(c) || c is >= 'A' and <= 'F'))) return false;
        byte[] key;
        try { key = File.ReadAllBytes(_keyFile); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
        try
        {
            if (key.Length < 32) return false;
            var expected = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload(parts[0], parts[1], method, requestHash)));
            if (!CryptographicOperations.FixedTimeEquals(expected, Convert.FromHexString(parts[2]))) return false;
        }
        finally { CryptographicOperations.ZeroMemory(key); }
        foreach (var used in _usedNonces) if (used.Value < now) _usedNonces.TryRemove(used.Key, out _);
        return _usedNonces.TryAdd(parts[1], expires);
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
        var valid = context.Method.StartsWith(ContainerPermissionBinding.Service, StringComparison.Ordinal)
            ? validator.ValidateContainerPermission(token, context.Method, context.RequestHeaders.GetValue(ContainerPermissionBinding.Header))
            : context.Method.StartsWith(ContainerMoveBinding.Service, StringComparison.Ordinal)
            ? validator.ValidateContainerMove(token, context.Method, context.RequestHeaders.GetValue(ContainerMoveBinding.Header))
            : context.Method.StartsWith(ContainerCopyBinding.Service, StringComparison.Ordinal)
            ? validator.ValidateContainerCopy(token, context.Method, context.RequestHeaders.GetValue(ContainerCopyBinding.Header))
            : context.Method.StartsWith(ContainerDirectoryBinding.Service, StringComparison.Ordinal)
            ? validator.ValidateContainerDirectory(token, context.Method, context.RequestHeaders.GetValue(ContainerDirectoryBinding.Header))
            : context.Method.StartsWith(ContainerFileWriteBinding.Service, StringComparison.Ordinal)
            ? validator.ValidateContainerWrite(token, context.Method, context.RequestHeaders.GetValue(ContainerFileWriteBinding.Header))
            : context.Method.StartsWith(ContainerHistoryBinding.Service, StringComparison.Ordinal)
                ? validator.ValidateContainerHistory(token, context.Method, context.RequestHeaders.GetValue(ContainerHistoryBinding.Header))
                : validator.Validate(token, context.Method);
        if (!valid)
        {
            throw new RpcException(new Status(StatusCode.PermissionDenied, "Agent capability is missing or invalid."));
        }
    }
}
