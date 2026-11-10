using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Core.Interceptors;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Agent.Services;

public sealed class CapabilityValidator(IConfiguration configuration)
{
    private const long MaximumLifetimeSeconds = 60;
    private readonly string _keyFile = configuration["Agent:CapabilityKeyFile"]
        ?? "/etc/whaledeck/agent-capability.key";
    private readonly ConcurrentDictionary<string, long> _usedNonces = new(StringComparer.Ordinal);

    public bool Validate(string? token, string method)
    {
        if (method is DevelopmentStorageBinding.Method or DevelopmentStorageBinding.AttestationMethod ||
            method.StartsWith(DevelopmentLifecycleBinding.Service, StringComparison.Ordinal) ||
            method.StartsWith(DevelopmentArchiveBinding.Service, StringComparison.Ordinal) ||
            method.StartsWith(DevelopmentRunnerBinding.Service, StringComparison.Ordinal) ||
            method.StartsWith(DevelopmentGitBinding.Service, StringComparison.Ordinal) ||
            method.StartsWith(DevelopmentSourceBinding.Service, StringComparison.Ordinal)) return false;
        if (method.StartsWith(ContainerFileWriteBinding.Service, StringComparison.Ordinal) ||
            method.StartsWith(ContainerHistoryBinding.Service, StringComparison.Ordinal) ||
            method.StartsWith(ContainerDirectoryBinding.Service, StringComparison.Ordinal) ||
            method.StartsWith(ContainerRecursiveAclBinding.Service, StringComparison.Ordinal) ||
            method.StartsWith(ContainerRecursiveAclRestoreBinding.Service, StringComparison.Ordinal) ||
            method.StartsWith(ContainerRecursiveOwnershipBinding.Service, StringComparison.Ordinal) ||
            method.StartsWith(ContainerRecursivePermissionBinding.Service, StringComparison.Ordinal) ||
            method.StartsWith(ContainerPermissionBinding.Service, StringComparison.Ordinal) ||
            method.StartsWith(ContainerRecycleBinding.Service, StringComparison.Ordinal) ||
            method.StartsWith(ContainerRecycleRestoreBinding.Service,StringComparison.Ordinal) ||
            method.StartsWith(ContainerRecyclePurgeBinding.Service,StringComparison.Ordinal) ||
            method.StartsWith(ContainerRecycleArchiveBinding.Service,StringComparison.Ordinal) ||
            method.StartsWith(ContainerRecycleArchiveRetirementBinding.Service,StringComparison.Ordinal) ||
            method.StartsWith(ContainerOwnershipBinding.Service, StringComparison.Ordinal) ||
            method.StartsWith(ContainerAclHistoryBinding.Service, StringComparison.Ordinal) ||
            method.StartsWith(ContainerRecursiveAclHistoryBinding.Service, StringComparison.Ordinal) ||
            method.StartsWith(ContainerAclBinding.Service, StringComparison.Ordinal) ||
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

    public bool ValidateDevelopmentStorage(string? token, string method, WorkspaceAllocationRequest request) =>
        (method is DevelopmentStorageBinding.Method or DevelopmentStorageBinding.AttestationMethod) &&
        ValidateContentBound(token, method, DevelopmentStorageBinding.Hash(request), DevelopmentStorageBinding.Payload);

    public bool ValidateDevelopmentLifecycle(string? token, string method, WorkspaceLifecycleRequest request) =>
        method is DevelopmentLifecycleBinding.Plan or DevelopmentLifecycleBinding.Apply &&
        DevelopmentLifecycleBinding.IsValid(request, method == DevelopmentLifecycleBinding.Apply) &&
        ValidateContentBound(token, method, DevelopmentLifecycleBinding.Hash(request), DevelopmentLifecycleBinding.Payload);

    public bool ValidateDevelopmentArchive(string? token, string method, WorkspaceArchiveRequest request) =>
        DevelopmentArchiveBinding.IsMethod(method) && DevelopmentArchiveBinding.IsValid(request) &&
        ValidateContentBound(token, method, DevelopmentArchiveBinding.Hash(request), DevelopmentArchiveBinding.Payload);

    public bool ValidateDevelopmentRunner(string? token, string method, WorkspaceRunRequest request) =>
        method is DevelopmentRunnerBinding.Start or DevelopmentRunnerBinding.Observe or
            DevelopmentRunnerBinding.Cancel or DevelopmentRunnerBinding.Reclaim &&
        ValidateContentBound(token, method, DevelopmentRunnerBinding.Hash(request), DevelopmentRunnerBinding.Payload);

    public bool ValidateDevelopmentArtifact(string? token, string method, WorkspaceTestArtifactRequest request) =>
        method is DevelopmentRunnerBinding.ReadTestArtifact or DevelopmentRunnerBinding.RetireTestArtifact &&
        ValidateContentBound(token, method, DevelopmentRunnerBinding.Hash(request), DevelopmentRunnerBinding.Payload);

    public bool ValidateDevelopmentSource(string? token, string method, string? hash) =>
        DevelopmentSourceBinding.IsMethod(method) &&
        ValidateContentBound(token, method, hash, DevelopmentSourceBinding.Payload);

    public bool ValidateDevelopmentGit(string? token, string method, WorkspaceGitRequest request) =>
        method == DevelopmentGitBinding.Acquire && DevelopmentGitBinding.IsValid(request) &&
        ValidateContentBound(token, method, DevelopmentGitBinding.Hash(request), DevelopmentGitBinding.Payload);

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

    public bool ValidateContainerRecursivePermission(string? token, string method, string? requestHash) =>
        method is ContainerRecursivePermissionBinding.Service + "Plan" or ContainerRecursivePermissionBinding.Service + "Apply" or
            ContainerRecursivePermissionBinding.Service + "Inspect" or ContainerRecursivePermissionBinding.Service + "Discard" &&
        ValidateContentBound(token, method, requestHash, ContainerRecursivePermissionBinding.Payload);

    public bool ValidateContainerRecursiveOwnership(string? token, string method, string? requestHash) =>
        method is ContainerRecursiveOwnershipBinding.Service + "Plan" or ContainerRecursiveOwnershipBinding.Service + "Apply" or
            ContainerRecursiveOwnershipBinding.Service + "Inspect" or ContainerRecursiveOwnershipBinding.Service + "Discard" &&
        ValidateContentBound(token, method, requestHash, ContainerRecursiveOwnershipBinding.Payload);

    public bool ValidateContainerRecursiveAcl(string? token, string method, string? requestHash) =>
        method is ContainerRecursiveAclBinding.Service + "Plan" or ContainerRecursiveAclBinding.Service + "Apply" or
            ContainerRecursiveAclBinding.Service + "Inspect" or ContainerRecursiveAclBinding.Service + "Discard" &&
        ValidateContentBound(token, method, requestHash, ContainerRecursiveAclBinding.Payload);

    public bool ValidateContainerRecursiveAclRestore(string? token, string method, string? requestHash) =>
        method is ContainerRecursiveAclRestoreBinding.Service + "Plan" or ContainerRecursiveAclRestoreBinding.Service + "Apply" or
            ContainerRecursiveAclRestoreBinding.Service + "Inspect" or ContainerRecursiveAclRestoreBinding.Service + "Discard" &&
        ValidateContentBound(token, method, requestHash, ContainerRecursiveAclRestoreBinding.Payload);

    public bool ValidateContainerRecycleArchive(string? token,string method,string? requestHash) =>
        method is ContainerRecycleArchiveBinding.Service+"Plan" or ContainerRecycleArchiveBinding.Service+"Reserve" or ContainerRecycleArchiveBinding.Service+"Apply" or
            ContainerRecycleArchiveBinding.Service+"Inspect" or ContainerRecycleArchiveBinding.Service+"Discard" &&
        ValidateContentBound(token,method,requestHash,ContainerRecycleArchiveBinding.Payload);

    public bool ValidateContainerRecycleArchiveRetirement(string? token, string method, string? requestHash) =>
        method is ContainerRecycleArchiveRetirementBinding.Service + "Plan" or ContainerRecycleArchiveRetirementBinding.Service + "Reserve" or
            ContainerRecycleArchiveRetirementBinding.Service + "Apply" or ContainerRecycleArchiveRetirementBinding.Service + "Inspect" or
            ContainerRecycleArchiveRetirementBinding.Service + "Discard" &&
        ValidateContentBound(token, method, requestHash, ContainerRecycleArchiveRetirementBinding.Payload);

    public bool ValidateContainerRecyclePurge(string? token,string method,string? requestHash) =>
        method is ContainerRecyclePurgeBinding.Service+"Plan" or ContainerRecyclePurgeBinding.Service+"Reserve" or ContainerRecyclePurgeBinding.Service+"Apply" or
            ContainerRecyclePurgeBinding.Service+"Inspect" or ContainerRecyclePurgeBinding.Service+"Discard" &&
        ValidateContentBound(token,method,requestHash,ContainerRecyclePurgeBinding.Payload);

    public bool ValidateContainerRecycleRestore(string? token,string method,string? requestHash) =>
        method is ContainerRecycleRestoreBinding.Service+"Plan" or ContainerRecycleRestoreBinding.Service+"Apply" or
            ContainerRecycleRestoreBinding.Service+"Inspect" or ContainerRecycleRestoreBinding.Service+"Discard" &&
        ValidateContentBound(token,method,requestHash,ContainerRecycleRestoreBinding.Payload);

    public bool ValidateContainerRecycle(string? token, string method, string? requestHash) =>
        method is ContainerRecycleBinding.Service + "Plan" or ContainerRecycleBinding.Service + "Apply" or
            ContainerRecycleBinding.Service + "Inspect" or ContainerRecycleBinding.Service + "Discard" or ContainerRecycleBinding.Service + "Restore" &&
        ValidateContentBound(token, method, requestHash, ContainerRecycleBinding.Payload);

    public bool ValidateContainerPermission(string? token, string method, string? requestHash) =>
        method is ContainerPermissionBinding.Service + "Plan" or ContainerPermissionBinding.Service + "Apply" or
            ContainerPermissionBinding.Service + "Inspect" or ContainerPermissionBinding.Service + "Discard" &&
        ValidateContentBound(token, method, requestHash, ContainerPermissionBinding.Payload);

    public bool ValidateContainerRecursiveAclHistory(string? token, string method, string? requestHash) =>
        method is ContainerRecursiveAclHistoryBinding.Service + "List" or ContainerRecursiveAclHistoryBinding.Service + "Read" &&
        ValidateContentBound(token, method, requestHash, ContainerRecursiveAclHistoryBinding.Payload);

    public bool ValidateContainerAclHistory(string? token, string method, string? requestHash) =>
        method is ContainerAclHistoryBinding.Service + "List" or ContainerAclHistoryBinding.Service + "Read" &&
        ValidateContentBound(token, method, requestHash, ContainerAclHistoryBinding.Payload);

    public bool ValidateContainerAcl(string? token, string method, string? requestHash) =>
        method is ContainerAclBinding.Service + "Plan" or ContainerAclBinding.Service + "Apply" or
            ContainerAclBinding.Service + "Inspect" or ContainerAclBinding.Service + "Discard" &&
        ValidateContentBound(token, method, requestHash, ContainerAclBinding.Payload);

    public bool ValidateContainerOwnership(string? token, string method, string? requestHash) =>
        method is ContainerOwnershipBinding.Service + "Plan" or ContainerOwnershipBinding.Service + "Apply" or
            ContainerOwnershipBinding.Service + "Inspect" or ContainerOwnershipBinding.Service + "Discard" &&
        ValidateContentBound(token, method, requestHash, ContainerOwnershipBinding.Payload);

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
        if (context.Method is DevelopmentSourceBinding.ReadDiff or DevelopmentSourceBinding.ReadTextDiff)
        {
            var tokens = context.RequestHeaders.GetAll("x-whaledeck-capability").ToArray();
            var hash = (context.Method, request) switch
            {
                (DevelopmentSourceBinding.ReadDiff, WorkspaceSourceDiffRequest diff) when DevelopmentSourceDiffBinding.IsValid(diff) =>
                    DevelopmentSourceDiffBinding.Hash(diff),
                (DevelopmentSourceBinding.ReadTextDiff, WorkspaceSourceTextDiffRequest diff) when DevelopmentSourceDiffBinding.IsValid(diff) =>
                    DevelopmentSourceDiffBinding.Hash(diff),
                _ => null
            };
            if (tokens.Length != 1 || hash is null || !validator.ValidateDevelopmentSource(tokens[0].Value, context.Method, hash))
                throw Denied();
            return continuation(request, context);
        }
        if (context.Method.StartsWith(DevelopmentSourceBinding.Service, StringComparison.Ordinal))
        {
            var tokens = context.RequestHeaders.GetAll("x-whaledeck-capability").ToArray();
            if (DevelopmentSourceBinding.IsStage(context.Method) || tokens.Length != 1 ||
                request is not WorkspaceSourceRequest source || source.CalculateSize() > DevelopmentSourceBinding.MaximumHeaderBytes ||
                !validator.ValidateDevelopmentSource(tokens[0].Value, context.Method, DevelopmentSourceBinding.Hash(source)))
                throw Denied();
            return continuation(request, context);
        }
        if (context.Method.StartsWith(DevelopmentRunnerBinding.Service, StringComparison.Ordinal))
        {
            if (context.Method == DevelopmentRunnerBinding.RetireTestArtifact)
            {
                AuthorizeArtifact(context, request);
                return continuation(request, context);
            }
            var tokens = context.RequestHeaders.GetAll("x-whaledeck-capability").ToArray();
            if (tokens.Length != 1 || request is not WorkspaceRunRequest run || run.CalculateSize() > DevelopmentRunnerBinding.MaximumWireBytes ||
                !validator.ValidateDevelopmentRunner(tokens[0].Value, context.Method, run)) throw Denied();
            return continuation(request, context);
        }
        if (context.Method.StartsWith(DevelopmentLifecycleBinding.Service, StringComparison.Ordinal))
        {
            var tokens = context.RequestHeaders.GetAll("x-whaledeck-capability").ToArray();
            if (tokens.Length != 1 || request is not WorkspaceLifecycleRequest lifecycle ||
                !validator.ValidateDevelopmentLifecycle(tokens[0].Value, context.Method, lifecycle)) throw Denied();
            return continuation(request, context);
        }
        if (context.Method.StartsWith(DevelopmentArchiveBinding.Service, StringComparison.Ordinal))
        {
            var tokens = context.RequestHeaders.GetAll("x-whaledeck-capability").ToArray();
            if (tokens.Length != 1 || request is not WorkspaceArchiveRequest archive ||
                !validator.ValidateDevelopmentArchive(tokens[0].Value, context.Method, archive)) throw Denied();
            return continuation(request, context);
        }
        if (context.Method is DevelopmentStorageBinding.Method or DevelopmentStorageBinding.AttestationMethod)
        {
            var tokens = context.RequestHeaders.GetAll("x-whaledeck-capability").ToArray();
            if (tokens.Length != 1 || request is not WorkspaceAllocationRequest allocation ||
                !validator.ValidateDevelopmentStorage(tokens[0].Value, context.Method, allocation)) throw Denied();
            return continuation(request, context);
        }
        if (IsRetirement(context))
        {
            var plan = context.Method == ContainerRecycleArchiveRetirementBinding.Service + "Plan";
            if (plan ? request is not WhaleDeck.Contracts.Agent.V1.ContainerRecycleArchiveRetirementPlanRequest :
                request is not WhaleDeck.Contracts.Agent.V1.ContainerRecycleArchiveRetirementRequest) throw Denied();
            var message = (IMessage)(object)request!;
            try { ContainerRecycleArchiveRetirementWire.RequireKnown(message); }
            catch (ArgumentException) { throw Denied(); }
            if (!string.Equals(context.RequestHeaders.GetValue(ContainerRecycleArchiveRetirementBinding.Header),
                ContainerRecycleArchiveRetirementBinding.Hash(message), StringComparison.Ordinal)) throw Denied();
        }
        Authorize(context);
        return continuation(request, context);
    }

    public override Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        if (context.Method == DevelopmentSourceBinding.ExportPrevious)
        {
            var tokens = context.RequestHeaders.GetAll("x-whaledeck-capability").ToArray();
            if (tokens.Length != 1 || request is not WorkspaceSourceRequest source ||
                source.CalculateSize() > DevelopmentSourceBinding.MaximumHeaderBytes ||
                !DevelopmentSourceBinding.IsValid(source, context.Method) ||
                !validator.ValidateDevelopmentSource(tokens[0].Value, context.Method, DevelopmentSourceBinding.Hash(source)))
                throw Denied();
            return continuation(request, responseStream, context);
        }
        if (context.Method.StartsWith(DevelopmentGitBinding.Service, StringComparison.Ordinal))
        {
            var tokens = context.RequestHeaders.GetAll("x-whaledeck-capability").ToArray();
            if (tokens.Length != 1 || request is not WorkspaceGitRequest git ||
                !validator.ValidateDevelopmentGit(tokens[0].Value, context.Method, git)) throw Denied();
            return continuation(request, responseStream, context);
        }
        if (context.Method.StartsWith(DevelopmentRunnerBinding.Service, StringComparison.Ordinal))
        {
            if (context.Method != DevelopmentRunnerBinding.ReadTestArtifact) throw Denied();
            AuthorizeArtifact(context, request);
            return continuation(request, responseStream, context);
        }
        if (IsRetirement(context)) throw Denied();
        Authorize(context);
        return continuation(request, responseStream, context);
    }

    private void AuthorizeArtifact<TRequest>(ServerCallContext context, TRequest request)
    {
        var tokens = context.RequestHeaders.GetAll("x-whaledeck-capability").ToArray();
        if (tokens.Length != 1 || request is not WorkspaceTestArtifactRequest artifact ||
            artifact.CalculateSize() > DevelopmentRunnerBinding.MaximumWireBytes ||
            !validator.ValidateDevelopmentArtifact(tokens[0].Value, context.Method, artifact)) throw Denied();
    }

    public override Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(IAsyncStreamReader<TRequest> requestStream,
        ServerCallContext context, ClientStreamingServerMethod<TRequest, TResponse> continuation)
    {
        if (DevelopmentSourceBinding.IsStage(context.Method))
        {
            var tokens = context.RequestHeaders.GetAll("x-whaledeck-capability").ToArray();
            var hashes = context.RequestHeaders.GetAll(DevelopmentSourceBinding.Header).ToArray();
            if (tokens.Length != 1 || hashes.Length != 1 ||
                !validator.ValidateDevelopmentSource(tokens[0].Value, context.Method, hashes[0].Value))
                throw Denied();
            return continuation(requestStream, context);
        }
        if (IsRetirement(context)) throw Denied();
        Authorize(context);
        return continuation(requestStream, context);
    }

    public override Task DuplexStreamingServerHandler<TRequest, TResponse>(IAsyncStreamReader<TRequest> requestStream,
        IServerStreamWriter<TResponse> responseStream, ServerCallContext context, DuplexStreamingServerMethod<TRequest, TResponse> continuation)
    {
        if (IsRetirement(context)) throw Denied();
        Authorize(context);
        return continuation(requestStream, responseStream, context);
    }

    private static bool IsRetirement(ServerCallContext context) => context.Method.StartsWith(ContainerRecycleArchiveRetirementBinding.Service, StringComparison.Ordinal);
    private static RpcException Denied() => new(new Status(StatusCode.PermissionDenied, "Agent capability is missing or invalid."));

    private void Authorize(ServerCallContext context)
    {
        var token = context.RequestHeaders.GetValue("x-whaledeck-capability");
        var valid = IsRetirement(context)
            ? validator.ValidateContainerRecycleArchiveRetirement(token, context.Method, context.RequestHeaders.GetValue(ContainerRecycleArchiveRetirementBinding.Header))
            : context.Method.StartsWith(ContainerRecycleArchiveBinding.Service,StringComparison.Ordinal)
            ? validator.ValidateContainerRecycleArchive(token,context.Method,context.RequestHeaders.GetValue(ContainerRecycleArchiveBinding.Header))
            : context.Method.StartsWith(ContainerRecyclePurgeBinding.Service,StringComparison.Ordinal)
            ? validator.ValidateContainerRecyclePurge(token,context.Method,context.RequestHeaders.GetValue(ContainerRecyclePurgeBinding.Header))
            : context.Method.StartsWith(ContainerRecycleRestoreBinding.Service,StringComparison.Ordinal)
            ? validator.ValidateContainerRecycleRestore(token,context.Method,context.RequestHeaders.GetValue(ContainerRecycleRestoreBinding.Header))
            : context.Method.StartsWith(ContainerRecycleBinding.Service, StringComparison.Ordinal)
            ? validator.ValidateContainerRecycle(token, context.Method, context.RequestHeaders.GetValue(ContainerRecycleBinding.Header))
            : context.Method.StartsWith(ContainerRecursiveAclRestoreBinding.Service, StringComparison.Ordinal)
            ? validator.ValidateContainerRecursiveAclRestore(token, context.Method, context.RequestHeaders.GetValue(ContainerRecursiveAclRestoreBinding.Header))
            : context.Method.StartsWith(ContainerRecursiveAclHistoryBinding.Service, StringComparison.Ordinal)
            ? validator.ValidateContainerRecursiveAclHistory(token, context.Method, context.RequestHeaders.GetValue(ContainerRecursiveAclHistoryBinding.Header))
            : context.Method.StartsWith(ContainerRecursiveAclBinding.Service, StringComparison.Ordinal)
            ? validator.ValidateContainerRecursiveAcl(token, context.Method, context.RequestHeaders.GetValue(ContainerRecursiveAclBinding.Header))
            : context.Method.StartsWith(ContainerAclHistoryBinding.Service, StringComparison.Ordinal)
            ? validator.ValidateContainerAclHistory(token, context.Method, context.RequestHeaders.GetValue(ContainerAclHistoryBinding.Header))
            : context.Method.StartsWith(ContainerAclBinding.Service, StringComparison.Ordinal)
            ? validator.ValidateContainerAcl(token, context.Method, context.RequestHeaders.GetValue(ContainerAclBinding.Header))
            : context.Method.StartsWith(ContainerRecursiveOwnershipBinding.Service, StringComparison.Ordinal)
            ? validator.ValidateContainerRecursiveOwnership(token, context.Method, context.RequestHeaders.GetValue(ContainerRecursiveOwnershipBinding.Header))
            : context.Method.StartsWith(ContainerRecursivePermissionBinding.Service, StringComparison.Ordinal)
            ? validator.ValidateContainerRecursivePermission(token, context.Method, context.RequestHeaders.GetValue(ContainerRecursivePermissionBinding.Header))
            : context.Method.StartsWith(ContainerOwnershipBinding.Service, StringComparison.Ordinal)
            ? validator.ValidateContainerOwnership(token, context.Method, context.RequestHeaders.GetValue(ContainerOwnershipBinding.Header))
            : context.Method.StartsWith(ContainerPermissionBinding.Service, StringComparison.Ordinal)
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
