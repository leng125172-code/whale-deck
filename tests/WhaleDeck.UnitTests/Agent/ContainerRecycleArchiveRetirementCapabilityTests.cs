using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.Extensions.Configuration;
using WhaleDeck.Agent.Services;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.UnitTests.Agent;

public sealed class ContainerRecycleArchiveRetirementCapabilityTests : IDisposable
{
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
    private readonly string _file = Path.GetTempFileName();
    private readonly CapabilityValidator _validator;
    private const string Service = ContainerRecycleArchiveRetirementBinding.Service;
    public ContainerRecycleArchiveRetirementCapabilityTests()
    {
        File.WriteAllBytes(_file, _key);
        _validator = new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:CapabilityKeyFile"] = _file }).Build());
    }
    private string Sign(string method, string hash, DateTimeOffset? expiry = null, string? nonce = null, bool legacy = false)
    {
        var expires = (expiry ?? DateTimeOffset.UtcNow.AddSeconds(30)).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        nonce ??= Guid.NewGuid().ToString("N");
        var payload = legacy ? $"{expires}.{nonce}.{method}" : ContainerRecycleArchiveRetirementBinding.Payload(expires, nonce, method, hash);
        return expires + "." + nonce + "." + Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(payload)));
    }

    [Theory][InlineData("Plan")][InlineData("Reserve")][InlineData("Apply")][InlineData("Inspect")][InlineData("Discard")]
    public void EachActionRequiresDedicatedShortLivedContentBoundSingleUseAuthority(string action)
    {
        var method = Service + action; var hash = ContainerRecycleArchiveRetirementBinding.Hash(ContainerRecycleArchiveRetirementContractTests.Request()); var token = Sign(method, hash);
        Assert.False(_validator.Validate(token, method)); Assert.False(_validator.ValidateContainerRecycleArchive(token, method, hash));
        Assert.False(_validator.ValidateContainerRecyclePurge(token, method, hash)); Assert.False(_validator.ValidateContainerRecycleRestore(token, method, hash));
        Assert.False(_validator.ValidateContainerRecycleArchiveRetirement(Sign(method, hash, legacy: true), method, hash));
        Assert.False(_validator.ValidateContainerRecycleArchiveRetirement(Sign(method, hash, DateTimeOffset.UtcNow.AddSeconds(-5)), method, hash));
        Assert.False(_validator.ValidateContainerRecycleArchiveRetirement(Sign(method, hash, DateTimeOffset.UtcNow.AddSeconds(120)), method, hash));
        Assert.False(_validator.ValidateContainerRecycleArchiveRetirement(token, method, new('F', 64)));
        Assert.True(_validator.ValidateContainerRecycleArchiveRetirement(token, method, hash));
        Assert.False(_validator.ValidateContainerRecycleArchiveRetirement(token, method, hash));
    }

    [Theory][InlineData("GetHealth")][InlineData("GetCapabilities")][InlineData("WatchHealth")][InlineData("Reset")][InlineData("Apply/Other")][InlineData("apply")]
    public async Task UnknownAndPublicLookingSuffixesAreDeniedWithoutNonceConsumption(string action)
    {
        var hash = new string('A', 64); var nonce = Guid.NewGuid().ToString("N"); var method = Service + action;
        Assert.False(_validator.Validate(Sign(method, hash, nonce: nonce, legacy: true), method));
        Assert.False(_validator.ValidateContainerRecycleArchiveRetirement(Sign(method, hash, nonce: nonce), method, hash));
        Assert.True(_validator.ValidateContainerRecycleArchiveRetirement(Sign(Service + "Apply", hash, nonce: nonce), Service + "Apply", hash));
        var request = ContainerRecycleArchiveRetirementContractTests.Request(); var actual = ContainerRecycleArchiveRetirementBinding.Hash(request); var fresh = Guid.NewGuid().ToString("N"); var invoked = false;
        Task<string> Next(ContainerRecycleArchiveRetirementRequest _, ServerCallContext __) { invoked = true; return Task.FromResult("bad"); }
        var error = await Assert.ThrowsAsync<RpcException>(() => new CapabilityInterceptor(_validator).UnaryServerHandler(request, new Context(method, Sign(method, actual, nonce: fresh), actual), Next));
        Assert.Equal(StatusCode.PermissionDenied, error.StatusCode); Assert.False(invoked);
        Assert.True(_validator.ValidateContainerRecycleArchiveRetirement(Sign(Service + "Apply", actual, nonce: fresh), Service + "Apply", actual));
    }

    [Theory][InlineData(null)][InlineData("")][InlineData("abc")][InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void NoncanonicalHashIsRejected(string? hash) => Assert.False(_validator.ValidateContainerRecycleArchiveRetirement(Sign(Service + "Apply", new('A', 64)), Service + "Apply", hash));

    [Theory][InlineData("")][InlineData("00000000000000000000000000000000")][InlineData("ABCDEFABCDEFABCDEFABCDEFABCDEFABCD")][InlineData("short")]
    public void NoncanonicalNonceIsRejected(string nonce)
    {
        var hash = new string('A', 64); Assert.False(_validator.ValidateContainerRecycleArchiveRetirement(Sign(Service + "Apply", hash, nonce: nonce), Service + "Apply", hash));
    }

    [Fact]
    public void ConcurrentReplayHasExactlyOneWinner()
    {
        var hash = new string('A', 64); var token = Sign(Service + "Apply", hash); var winners = 0;
        Parallel.For(0, 16, _ => { if (_validator.ValidateContainerRecycleArchiveRetirement(token, Service + "Apply", hash)) Interlocked.Increment(ref winners); });
        Assert.Equal(1, winners);
    }

    [Fact]
    public void MissingOrShortKeyFailsClosed()
    {
        var hash = new string('A', 64); var token = Sign(Service + "Apply", hash);
        File.WriteAllBytes(_file, [1, 2]); Assert.False(_validator.ValidateContainerRecycleArchiveRetirement(token, Service + "Apply", hash));
        File.Delete(_file); Assert.False(_validator.ValidateContainerRecycleArchiveRetirement(token, Service + "Apply", hash));
    }

    [Theory][InlineData("Plan")][InlineData("Reserve")][InlineData("Apply")][InlineData("Inspect")][InlineData("Discard")]
    public async Task UnaryInterceptorBindsActualBodyAndRejectsReplay(string action)
    {
        var q = ContainerRecycleArchiveRetirementContractTests.Request();
        IMessage request = action == "Plan" ? new ContainerRecycleArchiveRetirementPlanRequest { Command = q.Plan.Command.Clone(), Context = q.Context.Clone() } : q;
        var method = Service + action; var hash = ContainerRecycleArchiveRetirementBinding.Hash(request); var token = Sign(method, hash);
        var interceptor = new CapabilityInterceptor(_validator); var invoked = false;
        Task<string> Next(IMessage _, ServerCallContext __) { invoked = true; return Task.FromResult("ok"); }
        var error = await Assert.ThrowsAsync<RpcException>(() => interceptor.UnaryServerHandler(request, new Context(method, null, hash), Next)); Assert.Equal(StatusCode.PermissionDenied, error.StatusCode); Assert.False(invoked);
        var changed = action == "Plan" ? (IMessage)((ContainerRecycleArchiveRetirementPlanRequest)request).Clone() : q.Clone();
        if (changed is ContainerRecycleArchiveRetirementPlanRequest plan) plan.Command.RetainDays++; else ((ContainerRecycleArchiveRetirementRequest)changed).Context.ActorSubject = "changed";
        await Assert.ThrowsAsync<RpcException>(() => interceptor.UnaryServerHandler(changed, new Context(method, token, hash), Next)); Assert.False(invoked);
        Assert.Equal("ok", await interceptor.UnaryServerHandler(request, new Context(method, token, hash), Next)); Assert.True(invoked); invoked = false;
        await Assert.ThrowsAsync<RpcException>(() => interceptor.UnaryServerHandler(request, new Context(method, token, hash), Next)); Assert.False(invoked);
    }

    [Fact]
    public async Task DescriptorConfusionAndStreamingNeverInvokeContinuation()
    {
        var q = ContainerRecycleArchiveRetirementContractTests.Request(); var hash = ContainerRecycleArchiveRetirementBinding.Hash(q); var token = Sign(Service + "Apply", hash); var i = new CapabilityInterceptor(_validator); var invoked = false;
        Task<string> Wrong(string _, ServerCallContext __) { invoked = true; return Task.FromResult("bad"); }
        await Assert.ThrowsAsync<RpcException>(() => i.UnaryServerHandler("not protobuf", new Context(Service + "Apply", token, hash), Wrong));
        Task Stream(ContainerRecycleArchiveRetirementRequest _, IServerStreamWriter<string> __, ServerCallContext ___) { invoked = true; return Task.CompletedTask; }
        await Assert.ThrowsAsync<RpcException>(() => i.ServerStreamingServerHandler<ContainerRecycleArchiveRetirementRequest,string>(q, null!, new Context(Service + "Apply", token, hash), Stream));
        Task<string> Client(IAsyncStreamReader<ContainerRecycleArchiveRetirementRequest> _, ServerCallContext __) { invoked = true; return Task.FromResult("bad"); }
        Task Duplex(IAsyncStreamReader<ContainerRecycleArchiveRetirementRequest> _, IServerStreamWriter<string> __, ServerCallContext ___) { invoked = true; return Task.CompletedTask; }
        await Assert.ThrowsAsync<RpcException>(() => i.ClientStreamingServerHandler<ContainerRecycleArchiveRetirementRequest,string>(null!, new Context(Service + "Apply", token, hash), Client));
        await Assert.ThrowsAsync<RpcException>(() => i.DuplexStreamingServerHandler<ContainerRecycleArchiveRetirementRequest,string>(null!, null!, new Context(Service + "Apply", token, hash), Duplex));
        Assert.False(invoked); Assert.True(_validator.ValidateContainerRecycleArchiveRetirement(token, Service + "Apply", hash));
    }

    [Fact]
    public void ArchiveDomainSignatureCannotAuthorizeRetirement()
    {
        var method = Service + "Apply"; var hash = new string('A', 64); var expires = DateTimeOffset.UtcNow.AddSeconds(30).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture); var nonce = Guid.NewGuid().ToString("N");
        var signature = Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(ContainerRecycleArchiveBinding.Payload(expires, nonce, method, hash))));
        Assert.False(_validator.ValidateContainerRecycleArchiveRetirement(expires + "." + nonce + "." + signature, method, hash));
    }

    [Fact]
    public async Task UnknownFieldsAndWrongProtobufDescriptorAreDeniedBeforeNonceConsumption()
    {
        var q = ContainerRecycleArchiveRetirementContractTests.Request();
        var unknown = ContainerRecycleArchiveRetirementRequest.Parser.ParseFrom(q.ToByteArray().Concat(new byte[] { 248, 7, 1 }).ToArray());
        var hash = ContainerRecycleArchiveRetirementBinding.Hash(unknown); var token = Sign(Service + "Apply", hash); var invoked = false;
        Task<string> Next(IMessage _, ServerCallContext __) { invoked = true; return Task.FromResult("bad"); }
        var interceptor = new CapabilityInterceptor(_validator);
        await Assert.ThrowsAsync<RpcException>(() => interceptor.UnaryServerHandler<IMessage,string>(unknown, new Context(Service + "Apply", token, hash), Next));
        var planning = new ContainerRecycleArchiveRetirementPlanRequest { Command = q.Plan.Command.Clone(), Context = q.Context.Clone() };
        await Assert.ThrowsAsync<RpcException>(() => interceptor.UnaryServerHandler<IMessage,string>(planning, new Context(Service + "Apply", token, hash), Next));
        Assert.False(invoked); Assert.True(_validator.ValidateContainerRecycleArchiveRetirement(token, Service + "Apply", hash));
    }

    private sealed class Context(string method, string? token, string? hash) : ServerCallContext
    {
        protected override string MethodCore => method;
        protected override string HostCore => "localhost";
        protected override string PeerCore => "unix";
        protected override DateTime DeadlineCore => DateTime.UtcNow.AddSeconds(45);
        protected override Metadata RequestHeadersCore { get; } = Headers(token, hash);
        private static Metadata Headers(string? token, string? hash) { Metadata v = []; if (token is not null) v.Add("x-whaledeck-capability", token); if (hash is not null) v.Add(ContainerRecycleArchiveRetirementBinding.Header, hash); return v; }
        protected override CancellationToken CancellationTokenCore => default;
        protected override Metadata ResponseTrailersCore => [];
        protected override Status StatusCore { get; set; }
        protected override WriteOptions? WriteOptionsCore { get; set; }
        protected override AuthContext AuthContextCore => null!;
        protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options) => throw new NotSupportedException();
        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) => Task.CompletedTask;
    }
    public void Dispose() { File.Delete(_file); CryptographicOperations.ZeroMemory(_key); }
}
