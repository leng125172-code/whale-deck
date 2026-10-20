using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using WhaleDeck.Agent.Services;
using Grpc.Core;

namespace WhaleDeck.UnitTests.Agent;

public sealed class CapabilityValidatorTests : IDisposable
{
    private const string Method = "/whaledeck.agent.v1.HostService/GetHostInfo";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"whaledeck-capability-{Guid.NewGuid():N}");
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
    private readonly CapabilityValidator _validator;

    public CapabilityValidatorTests()
    {
        Directory.CreateDirectory(_directory);
        var keyFile = Path.Combine(_directory, "capability.key");
        File.WriteAllBytes(keyFile, _key);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Agent:CapabilityKeyFile"] = keyFile
        }).Build();
        _validator = new CapabilityValidator(configuration);
    }

    [Fact]
    public void ValidCapabilityCanOnlyBeConsumedOnce()
    {
        var token = Sign(DateTimeOffset.UtcNow.AddSeconds(45), "unique-nonce-0001", Method);

        Assert.True(_validator.Validate(token, Method));
        Assert.False(_validator.Validate(token, Method));
    }

    [Fact]
    public void CapabilityLongerThanSixtySecondsIsRejected()
    {
        var token = Sign(DateTimeOffset.UtcNow.AddSeconds(90), "unique-nonce-0002", Method);

        Assert.False(_validator.Validate(token, Method));
    }

    [Fact]
    public void ExpiredOrWrongMethodCapabilityIsRejected()
    {
        var expired = Sign(DateTimeOffset.UtcNow.AddSeconds(-1), "unique-nonce-0003", Method);
        var wrongMethod = Sign(DateTimeOffset.UtcNow.AddSeconds(45), "unique-nonce-0004", Method);

        Assert.False(_validator.Validate(expired, Method));
        Assert.False(_validator.Validate(wrongMethod, "/whaledeck.agent.v1.DockerService/ListContainers"));
    }

    [Theory]
    [InlineData("Plan")][InlineData("Apply")][InlineData("Inspect")][InlineData("Discard")][InlineData("Download")]
    public void PackingMethodsAreDistinctSingleUseCapabilities(string action)
    {
        var method = "/whaledeck.agent.v1.FileTreePackingService/" + action;
        Assert.False(_validator.Validate(null, method));
        var wrong = Sign(DateTimeOffset.UtcNow.AddSeconds(45), "wrong-packing-nonce", "/whaledeck.agent.v1.FileTreeCopyService/" + action);
        Assert.False(_validator.Validate(wrong, method));
        var token = Sign(DateTimeOffset.UtcNow.AddSeconds(45), "packing-method-nonce", method);
        Assert.True(_validator.Validate(token, method)); Assert.False(_validator.Validate(token, method));
    }

    private string Sign(DateTimeOffset expiresAt, string nonce, string method)
    {
        var expires = expiresAt.ToUnixTimeSeconds();
        using var hmac = new HMACSHA256(_key);
        var signature = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{expires}.{nonce}.{method}")));
        return $"{expires}.{nonce}.{signature}";
    }

    [Theory]
    [InlineData("Plan")][InlineData("Apply")][InlineData("Inspect")][InlineData("Discard")]
    public void DirectoryRecycleCannotReuseOtherFileCapabilitiesOrReplayItsOwn(string action)
    {
        var method = "/whaledeck.agent.v1.FileTreeRecyclingService/" + action;
        Assert.False(_validator.Validate(null, method));
        foreach (var service in new[] { "FileArchiveService", "FileTreeMoveService", "FileTreeUnpackingService" })
            Assert.False(_validator.Validate(Sign(DateTimeOffset.UtcNow.AddSeconds(45), "recycle-wrong-service", "/whaledeck.agent.v1." + service + "/" + action), method));
        var token = Sign(DateTimeOffset.UtcNow.AddSeconds(45), "recycle-correct-method", method);
        Assert.True(_validator.Validate(token, method)); Assert.False(_validator.Validate(token, method));
    }

    [Theory]
    [InlineData("Inspect")]
    [InlineData("Apply")]
    public void PrivateConfigurationMethodsRequireTheirOwnSingleUseCapability(string action)
    {
        var method = "/whaledeck.agent.v1.NotificationConfigurationService/" + action;
        Assert.False(_validator.Validate(null, method));
        Assert.False(_validator.Validate(Sign(DateTimeOffset.UtcNow.AddSeconds(45), "private-wrong-method", Method), method));
        var token = Sign(DateTimeOffset.UtcNow.AddSeconds(45), "private-configuration-nonce", method);
        Assert.True(_validator.Validate(token, method));
        Assert.False(_validator.Validate(token, method));
    }

    [Theory]
    [InlineData("Plan")][InlineData("Apply")][InlineData("Inspect")][InlineData("Discard")]
    public void DirectoryRestoreRequiresItsOwnMethodAndCannotReplayOrUseRecyclingGrant(string action)
    {
        var method = "/whaledeck.agent.v1.FileTreeRestoringService/" + action;
        Assert.False(_validator.Validate(null, method));
        foreach (var service in new[] { "FileArchiveService", "FileTreeUnpackingService", "FileTreeRecyclingService" })
            Assert.False(_validator.Validate(Sign(DateTimeOffset.UtcNow.AddSeconds(45), "restore-wrong-service", "/whaledeck.agent.v1." + service + "/" + action), method));
        var wrongAction = action == "Inspect" ? "Apply" : "Inspect";
        Assert.False(_validator.Validate(Sign(DateTimeOffset.UtcNow.AddSeconds(45), "restore-wrong-action", "/whaledeck.agent.v1.FileTreeRestoringService/" + wrongAction), method));
        var token = Sign(DateTimeOffset.UtcNow.AddSeconds(45), "restore-correct-method", method);
        Assert.True(_validator.Validate(token, method)); Assert.False(_validator.Validate(token, method));
    }

    [Theory]
    [InlineData("List")][InlineData("Plan")][InlineData("Apply")][InlineData("Inspect")][InlineData("Discard")]
    public void ArchiveMethodsRequireTheirOwnNonReplayableCapability(string action)
    {
        var method = "/whaledeck.agent.v1.FileArchiveService/" + action;
        Assert.False(_validator.Validate(null, method));
        Assert.False(_validator.Validate(Sign(DateTimeOffset.UtcNow.AddSeconds(45), "archive-other-method", "/whaledeck.agent.v1.FileOperationService/Apply"), method));
        var token = Sign(DateTimeOffset.UtcNow.AddSeconds(45), "archive-correct-method", method);
        Assert.True(_validator.Validate(token, method)); Assert.False(_validator.Validate(token, method));
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task DuplexTerminalRequiresMethodBoundSingleUseCapabilityBeforeContinuation()
    {
        const string method = "/whaledeck.agent.v1.HostTerminalService/Connect";
        var interceptor = new CapabilityInterceptor(_validator);
        var invoked = false;
        Task Continue(IAsyncStreamReader<string> _, IServerStreamWriter<string> __, ServerCallContext ___) { invoked = true; return Task.CompletedTask; }
        var missing = await Assert.ThrowsAsync<RpcException>(() => interceptor.DuplexStreamingServerHandler<string, string>(null!, null!, new Context(method, null), Continue));
        Assert.Equal(StatusCode.PermissionDenied, missing.StatusCode); Assert.False(invoked);
        var wrong = Sign(DateTimeOffset.UtcNow.AddSeconds(45), "terminal-wrong-nonce", Method);
        await Assert.ThrowsAsync<RpcException>(() => interceptor.DuplexStreamingServerHandler<string, string>(null!, null!, new Context(method, wrong), Continue));
        Assert.False(invoked);
        var token = Sign(DateTimeOffset.UtcNow.AddSeconds(45), "terminal-correct-nonce", method);
        await interceptor.DuplexStreamingServerHandler<string, string>(null!, null!, new Context(method, token), Continue);
        Assert.True(invoked); invoked = false;
        await Assert.ThrowsAsync<RpcException>(() => interceptor.DuplexStreamingServerHandler<string, string>(null!, null!, new Context(method, token), Continue));
        Assert.False(invoked);
    }

    [Theory]
    [InlineData("Plan")][InlineData("Apply")][InlineData("Inspect")][InlineData("Discard")]
    public async Task DirectoryCopyUnaryRejectsMissingWrongMethodAndReplayWithExistingPermissionDeniedContract(string action)
    {
        var method = "/whaledeck.agent.v1.FileTreeCopyService/" + action;
        var interceptor = new CapabilityInterceptor(_validator); var invoked = false;
        Task<string> Continue(string _, ServerCallContext __) { invoked = true; return Task.FromResult("ok"); }
        var missing = await Assert.ThrowsAsync<RpcException>(() => interceptor.UnaryServerHandler("", new Context(method, null), Continue));
        Assert.Equal(StatusCode.PermissionDenied, missing.StatusCode); Assert.False(invoked);
        var wrong = Sign(DateTimeOffset.UtcNow.AddSeconds(45), "tree-wrong-method-nonce", Method);
        var mismatch = await Assert.ThrowsAsync<RpcException>(() => interceptor.UnaryServerHandler("", new Context(method, wrong), Continue));
        Assert.Equal(StatusCode.PermissionDenied, mismatch.StatusCode); Assert.False(invoked);
        var token = Sign(DateTimeOffset.UtcNow.AddSeconds(45), "tree-correct-method-nonce", method);
        Assert.Equal("ok", await interceptor.UnaryServerHandler("", new Context(method, token), Continue));
        Assert.True(invoked); invoked = false;
        var replay = await Assert.ThrowsAsync<RpcException>(() => interceptor.UnaryServerHandler("", new Context(method, token), Continue));
        Assert.Equal(StatusCode.PermissionDenied, replay.StatusCode); Assert.False(invoked);
    }

    [Theory]
    [InlineData("Plan")][InlineData("Apply")][InlineData("Inspect")][InlineData("Discard")]
    public async Task DirectoryMoveCannotReuseDirectoryCopyCapabilityOrNonce(string action)
    {
        var method = "/whaledeck.agent.v1.FileTreeMoveService/" + action;
        var interceptor = new CapabilityInterceptor(_validator); var invoked = false;
        Task<string> Continue(string _, ServerCallContext __) { invoked = true; return Task.FromResult("ok"); }
        var missing = await Assert.ThrowsAsync<RpcException>(() => interceptor.UnaryServerHandler("", new Context(method, null), Continue));
        Assert.Equal(StatusCode.PermissionDenied, missing.StatusCode); Assert.False(invoked);
        var copy = Sign(DateTimeOffset.UtcNow.AddSeconds(45), "move-wrong-method-nonce", "/whaledeck.agent.v1.FileTreeCopyService/" + action);
        var mismatch = await Assert.ThrowsAsync<RpcException>(() => interceptor.UnaryServerHandler("", new Context(method, copy), Continue));
        Assert.Equal(StatusCode.PermissionDenied, mismatch.StatusCode); Assert.False(invoked);
        var token = Sign(DateTimeOffset.UtcNow.AddSeconds(45), "move-correct-method-nonce", method);
        Assert.Equal("ok", await interceptor.UnaryServerHandler("", new Context(method, token), Continue));
        Assert.True(invoked); invoked = false;
        var replay = await Assert.ThrowsAsync<RpcException>(() => interceptor.UnaryServerHandler("", new Context(method, token), Continue));
        Assert.Equal(StatusCode.PermissionDenied, replay.StatusCode); Assert.False(invoked);
    }

    [Theory]
    [InlineData("Plan")][InlineData("Apply")][InlineData("Inspect")][InlineData("Discard")]
    public async Task UnpackingCannotReusePackingCapabilityOrReplayItsOwn(string action)
    {
        var method = "/whaledeck.agent.v1.FileTreeUnpackingService/" + action;
        var interceptor = new CapabilityInterceptor(_validator); var invoked = false;
        Task<string> Continue(string _, ServerCallContext __) { invoked = true; return Task.FromResult("ok"); }
        var missing = await Assert.ThrowsAsync<RpcException>(() => interceptor.UnaryServerHandler("", new Context(method, null), Continue));
        Assert.Equal(StatusCode.PermissionDenied, missing.StatusCode); Assert.False(invoked);
        var wrong = Sign(DateTimeOffset.UtcNow.AddSeconds(45), "unpack-wrong-method-nonce", "/whaledeck.agent.v1.FileTreePackingService/" + action);
        await Assert.ThrowsAsync<RpcException>(() => interceptor.UnaryServerHandler("", new Context(method, wrong), Continue)); Assert.False(invoked);
        var token = Sign(DateTimeOffset.UtcNow.AddSeconds(45), "unpack-correct-method-nonce", method);
        Assert.Equal("ok", await interceptor.UnaryServerHandler("", new Context(method, token), Continue)); Assert.True(invoked); invoked = false;
        await Assert.ThrowsAsync<RpcException>(() => interceptor.UnaryServerHandler("", new Context(method, token), Continue)); Assert.False(invoked);
    }

    private sealed class Context(string method, string? token) : ServerCallContext
    {
        protected override string MethodCore => method;
        protected override string HostCore => "localhost";
        protected override string PeerCore => "unix";
        protected override DateTime DeadlineCore => DateTime.UtcNow.AddSeconds(45);
        protected override Metadata RequestHeadersCore => token is null ? [] : new() { { "x-whaledeck-capability", token } };
        protected override CancellationToken CancellationTokenCore => default;
        protected override Metadata ResponseTrailersCore => [];
        protected override Status StatusCore { get; set; }
        protected override WriteOptions? WriteOptionsCore { get; set; }
        protected override AuthContext AuthContextCore => null!;
        protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options) => throw new NotSupportedException();
        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) => Task.CompletedTask;
    }
}
