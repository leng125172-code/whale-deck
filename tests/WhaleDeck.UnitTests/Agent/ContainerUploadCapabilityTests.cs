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

public sealed class ContainerUploadCapabilityTests : IDisposable
{
    private static readonly string[] Actions = ["InspectTarget", "StageUpload", "Publish", "Inspect", "Discard", "ObserveRecovery", "SealRecovery"];
    private readonly string _keyFile = Path.GetTempFileName();
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
    private readonly CapabilityValidator _validator;

    public ContainerUploadCapabilityTests()
    {
        // This key belongs exclusively to this disposable fixture.
        File.WriteAllBytes(_keyFile, _key);
        _validator = new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Agent:CapabilityKeyFile"] = _keyFile }).Build());
    }

    [Fact]
    public void ProtocolExposesSevenDistinctMethodsAndOnlyUploadIsClientStreaming()
    {
        var methods = ContainerFileWriteService.Descriptor.Methods;
        Assert.Equal(Actions.Order(), methods.Select(method => method.Name).Order());
        foreach (var method in methods)
        {
            Assert.Equal(method.Name == "StageUpload", method.IsClientStreaming);
            Assert.False(method.IsServerStreaming);
        }
    }

    [Theory]
    [InlineData("InspectTarget")][InlineData("StageUpload")]
    [InlineData("Publish")][InlineData("Inspect")][InlineData("Discard")][InlineData("ObserveRecovery")][InlineData("SealRecovery")]
    public void EachMethodRequiresItsOwnContentBoundSingleUseSignature(string action)
    {
        var method = ContainerFileWriteBinding.Service + action;
        var hash = ContainerFileWriteBinding.Hash(Request(action));
        var token = Sign(method, hash);
        Assert.False(_validator.ValidateContainerWrite(null, method, hash));
        Assert.False(_validator.Validate(token, method));
        foreach (var other in Actions.Where(candidate => candidate != action))
            Assert.False(_validator.ValidateContainerWrite(token, ContainerFileWriteBinding.Service + other, hash));
        Assert.True(_validator.ValidateContainerWrite(token, method, hash));
        Assert.False(_validator.ValidateContainerWrite(token, method, hash));
    }

    [Theory]
    [InlineData("InspectTarget")][InlineData("StageUpload")]
    [InlineData("Publish")][InlineData("Inspect")][InlineData("Discard")][InlineData("ObserveRecovery")][InlineData("SealRecovery")]
    public void ExpiredOverlongAndLegacyMethodOnlySignaturesCannotAuthorizeWrites(string action)
    {
        var method = ContainerFileWriteBinding.Service + action;
        var hash = ContainerFileWriteBinding.Hash(Request(action));
        Assert.False(_validator.ValidateContainerWrite(Sign(method, hash, DateTimeOffset.UtcNow.AddMinutes(-1)), method, hash));
        Assert.False(_validator.ValidateContainerWrite(Sign(method, hash, DateTimeOffset.UtcNow.AddMinutes(2)), method, hash));
        var legacy = LegacySign(method);
        Assert.False(_validator.ValidateContainerWrite(legacy, method, hash));
        Assert.False(_validator.Validate(legacy, method));
    }

    [Theory]
    [InlineData("/whaledeck.agent.v1.ContainerFileService/Inspect")]
    [InlineData("/whaledeck.agent.v1.FileTransferService/StageUpload")]
    [InlineData("/whaledeck.agent.v1.FileTreeRestoringService/Apply")]
    [InlineData("/whaledeck.agent.v1.FileTreePurgingService/Apply")]
    public void ContentSignatureCannotAuthorizeAnyLegacyServiceEvenWhenSignedForItsExactMethod(string method)
    {
        var hash = ContainerFileWriteBinding.Hash(Header());
        var token = Sign(method, hash);
        Assert.False(_validator.ValidateContainerWrite(token, method, hash));
        Assert.False(_validator.Validate(token, method));
        Assert.True(_validator.Validate(LegacySign(method), method));
    }

    [Theory]
    [InlineData(null)][InlineData("")][InlineData("ABC")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void MissingOrNoncanonicalRequestHashCannotValidateCapability(string? hash)
    {
        var method = ContainerFileWriteBinding.Service + "Inspect";
        var correct = ContainerFileWriteBinding.Hash(Reference());
        Assert.False(_validator.ValidateContainerWrite(Sign(method, correct), method, hash));
    }

    [Fact]
    public void HashBindsMessageTypeEvenWhenSerializedBytesAreIdentical()
    {
        var context = new RequestContext();
        var empty = new Google.Protobuf.WellKnownTypes.Empty();
        Assert.Equal(empty.ToByteArray(), context.ToByteArray());
        Assert.NotEqual(ContainerFileWriteBinding.Hash(empty), ContainerFileWriteBinding.Hash(context));
        var header = Header();
        Assert.Equal(ContainerFileWriteBinding.Hash(header), ContainerFileWriteBinding.Hash(header.Clone()));
    }

    [Theory]
    [InlineData("container")][InlineData("instance")][InlineData("path")]
    [InlineData("operation")][InlineData("owner")][InlineData("size")][InlineData("digest")]
    [InlineData("version")][InlineData("parentVersion")][InlineData("targetProof")]
    [InlineData("actor")][InlineData("trace")][InlineData("job")][InlineData("idempotency")]
    public async Task ChangedUploadHeaderCannotBorrowTheSignedOriginalRequest(string field)
    {
        var original = Header();
        var changed = original.Clone();
        Change(changed, field);
        var originalHash = ContainerFileWriteBinding.Hash(original);
        var changedHash = ContainerFileWriteBinding.Hash(changed);
        Assert.NotEqual(originalHash, changedHash);
        var method = ContainerFileWriteBinding.Service + "StageUpload";
        var token = Sign(method, originalHash);
        Assert.False(_validator.ValidateContainerWrite(token, method, changedHash));

        // Metadata is still authentic, so the service must independently compare
        // the actual first header before any privileged runner is reached.
        var context = new Context(method, token, originalHash);
        var interceptor = new CapabilityInterceptor(_validator);
        var reader = new Reader(new() { Header = changed });
        var service = new ContainerFileWriteGrpcService(null!);
        var error = await Assert.ThrowsAsync<RpcException>(() => interceptor.ClientStreamingServerHandler(
            reader, context, service.StageUpload));
        Assert.Equal(StatusCode.PermissionDenied, error.StatusCode);
        Assert.Equal("CONTAINER_UPLOAD_BINDING_INVALID", error.Status.Detail);
        Assert.Equal(1, reader.MoveCount);
    }

    [Theory]
    [InlineData("InspectTarget")][InlineData("Publish")][InlineData("Inspect")][InlineData("Discard")][InlineData("ObserveRecovery")][InlineData("SealRecovery")]
    public async Task UnaryServiceRejectsChangedRequestBeforePrivilegedRunner(string action)
    {
        var original = Request(action);
        IMessage changed;
        if (original is ContainerFileLocation location)
        {
            var copy = location.Clone();
            copy.Path = "changed.bin";
            changed = copy;
        }
        else if (original is ContainerUploadRecoveryRequest recovery)
        {
            var copy = recovery.Clone(); copy.Context.ActorSubject = "another-current-admin"; changed = copy;
        }
        else
        {
            var copy = ((ContainerUploadReference)original).Clone();
            copy.Owner = new('F', 64);
            changed = copy;
        }
        var method = ContainerFileWriteBinding.Service + action;
        var hash = ContainerFileWriteBinding.Hash(original);
        var context = new Context(method, Sign(method, hash), hash);
        var interceptor = new CapabilityInterceptor(_validator);
        var error = await Assert.ThrowsAsync<RpcException>(() => interceptor.UnaryServerHandler<IMessage, IMessage>(
            changed, context, (request, call) => Invoke(action, request, call)));
        Assert.Equal(StatusCode.PermissionDenied, error.StatusCode);
        Assert.Equal("CONTAINER_UPLOAD_BINDING_INVALID", error.Status.Detail);
    }

    [Theory]
    [InlineData("InspectTarget")][InlineData("StageUpload")]
    [InlineData("Publish")][InlineData("Inspect")][InlineData("Discard")][InlineData("ObserveRecovery")][InlineData("SealRecovery")]
    public async Task InterceptorAcceptsNewSignatureOnceAndRejectsLegacySignatureBeforeContinuation(string action)
    {
        var method = ContainerFileWriteBinding.Service + action;
        var hash = ContainerFileWriteBinding.Hash(Request(action));
        var interceptor = new CapabilityInterceptor(_validator);
        var invoked = false;
        var reader = new Reader(new() { Header = Header() });
        Task<string> Unary(string _, ServerCallContext __) { invoked = true; return Task.FromResult("ok"); }
        Task<string> Streaming(IAsyncStreamReader<ContainerUploadFrame> _, ServerCallContext __)
        { invoked = true; return Task.FromResult("ok"); }
        Task<string> Call(string? token, string? requestHash) => action == "StageUpload"
            ? interceptor.ClientStreamingServerHandler(reader, new Context(method, token, requestHash), Streaming)
            : interceptor.UnaryServerHandler("request", new Context(method, token, requestHash), Unary);

        foreach (var token in new string?[] { null, LegacySign(method) })
        {
            var denied = await Assert.ThrowsAsync<RpcException>(() => Call(token, hash));
            Assert.Equal(StatusCode.PermissionDenied, denied.StatusCode);
            Assert.False(invoked);
        }
        var valid = Sign(method, hash);
        await Assert.ThrowsAsync<RpcException>(() => Call(valid, null));
        Assert.False(invoked);
        Assert.Equal("ok", await Call(valid, hash));
        Assert.True(invoked);
        invoked = false;
        await Assert.ThrowsAsync<RpcException>(() => Call(valid, hash));
        Assert.False(invoked);
        Assert.Equal(0, reader.MoveCount);
    }

    private static async Task<IMessage> Invoke(string action, IMessage request, ServerCallContext context)
    {
        var service = new ContainerFileWriteGrpcService(null!);
        return action switch
        {
            "InspectTarget" => await service.InspectTarget((ContainerFileLocation)request, context),
            "Publish" => await service.Publish((ContainerUploadReference)request, context),
            "Inspect" => await service.Inspect((ContainerUploadReference)request, context),
            "Discard" => await service.Discard((ContainerUploadReference)request, context),
            "ObserveRecovery" => await service.ObserveRecovery((ContainerUploadReference)request, context),
            "SealRecovery" => await service.SealRecovery((ContainerUploadRecoveryRequest)request, context),
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };
    }

    private string Sign(string method, string hash, DateTimeOffset? expiration = null)
    {
        var expires = (expiration ?? DateTimeOffset.UtcNow.AddSeconds(45)).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var nonce = Guid.NewGuid().ToString("N");
        var mac = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(ContainerFileWriteBinding.Payload(expires, nonce, method, hash)));
        return expires + "." + nonce + "." + Convert.ToHexString(mac);
    }

    private string LegacySign(string method)
    {
        var prefix = DateTimeOffset.UtcNow.AddSeconds(45).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)
            + "." + Guid.NewGuid().ToString("N");
        return prefix + "." + Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(prefix + "." + method)));
    }

    private static ContainerFileLocation Location() => new()
    {
        ContainerId = new('a', 64), InstanceId = "777:2026-10-22T01:02:03.123456789Z", Path = "目录/中文.bin"
    };

    private static RequestContext ActorContext() => new()
    {
        ActorSubject = "fixture-administrator", TraceId = "fixture-trace", JobId = "cc0a8865343e49bdb01a72680131bf39",
        IdempotencyKey = "bb0a8865343e49bdb01a72680131bf39"
    };

    private static ContainerUploadHeader Header() => new()
    {
        Location = Location(), OperationId = "aa0a8865343e49bdb01a72680131bf39", Owner = new('B', 64),
        SizeBytes = 2097159, Sha256 = new('C', 64), ExpectedVersion = new('D', 64),
        ExpectedParentVersion = new('E', 64), ExpectedTargetProof = new('A', 64), Context = ActorContext()
    };

    private static ContainerUploadReference Reference() => new()
    {
        ContainerId = Location().ContainerId, InstanceId = Location().InstanceId,
        OperationId = Header().OperationId, Owner = Header().Owner, Context = ActorContext(), ExpectedTargetProof = new('A', 64)
    };

    private static ContainerUploadRecoveryRequest Recovery()
    {
        var id = "bb0a8865343e49bdb01a72680131bf39";
        return new() { Original = Reference(),RecoveryId = id,ExpectedObservationHash = new('D',64),
            Context = new() { ActorSubject = "current-recovery-admin",TraceId = "current-trace",JobId = id,IdempotencyKey = id } };
    }

    [Theory]
    [InlineData("actor")][InlineData("trace")][InlineData("recovery")][InlineData("observation")]
    [InlineData("owner")][InlineData("instance")][InlineData("container")][InlineData("proof")][InlineData("original-actor")]
    public async Task RecoveryBodyCannotBorrowCapabilityForAnotherActorPlanOrOriginal(string field)
    {
        var original = Recovery(); var changed = original.Clone();
        switch (field)
        {
            case "actor": changed.Context.ActorSubject = "another-current-admin"; break;
            case "trace": changed.Context.TraceId = "another-current-trace"; break;
            case "recovery": changed.RecoveryId = Guid.NewGuid().ToString("N"); changed.Context.JobId = changed.RecoveryId; changed.Context.IdempotencyKey = changed.RecoveryId; break;
            case "observation": changed.ExpectedObservationHash = new('F',64); break;
            case "owner": changed.Original.Owner = new('F',64); break;
            case "instance": changed.Original.InstanceId = changed.Original.InstanceId.Replace("777:","778:",StringComparison.Ordinal); break;
            case "container": changed.Original.ContainerId = new('b',64); break;
            case "proof": changed.Original.ExpectedTargetProof = new('F',64); break;
            case "original-actor": changed.Original.Context.ActorSubject = "another-original-admin"; break;
        }
        var method = ContainerFileWriteBinding.Service+"SealRecovery"; var hash = ContainerFileWriteBinding.Hash(original);
        var context = new Context(method,Sign(method,hash),hash);
        var error = await Assert.ThrowsAsync<RpcException>(() => new CapabilityInterceptor(_validator).UnaryServerHandler<IMessage,IMessage>(
            changed,context,(request,call) => Invoke("SealRecovery",request,call)));
        Assert.Equal(StatusCode.PermissionDenied,error.StatusCode); Assert.Equal("CONTAINER_UPLOAD_BINDING_INVALID",error.Status.Detail);
    }

    private static IMessage Request(string action) => action switch
    {
        "InspectTarget" => Location(), "StageUpload" => Header(), "SealRecovery" => Recovery(), _ => Reference()
    };

    private static void Change(ContainerUploadHeader header, string field)
    {
        switch (field)
        {
            case "container": header.Location.ContainerId = new('b', 64); break;
            case "instance": header.Location.InstanceId = "778:2026-10-22T01:02:03.123456789Z"; break;
            case "path": header.Location.Path = "目录/changed.bin"; break;
            case "operation": header.OperationId = "dd0a8865343e49bdb01a72680131bf39"; break;
            case "owner": header.Owner = new('F', 64); break;
            case "size": header.SizeBytes++; break;
            case "digest": header.Sha256 = new('F', 64); break;
            case "version": header.ExpectedVersion = "ABSENT"; break;
            case "parentVersion": header.ExpectedParentVersion = new('F', 64); break;
            case "targetProof": header.ExpectedTargetProof = new('F', 64); break;
            case "actor": header.Context.ActorSubject = "other-administrator"; break;
            case "trace": header.Context.TraceId = "other-trace"; break;
            case "job": header.Context.JobId = "dd0a8865343e49bdb01a72680131bf39"; break;
            case "idempotency": header.Context.IdempotencyKey = "ee0a8865343e49bdb01a72680131bf39"; break;
            default: throw new ArgumentOutOfRangeException(nameof(field));
        }
    }

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(_key);
        File.Delete(_keyFile);
    }

    private sealed class Reader(ContainerUploadFrame frame) : IAsyncStreamReader<ContainerUploadFrame>
    {
        public int MoveCount { get; private set; }
        public ContainerUploadFrame Current => frame;
        public Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(++MoveCount == 1);
        }
    }

    private sealed class Context : ServerCallContext
    {
        private readonly string _method;
        private readonly Metadata _headers = [];
        public Context(string method, string? token, string? hash)
        {
            _method = method;
            if (token is not null) _headers.Add("x-whaledeck-capability", token);
            if (hash is not null) _headers.Add(ContainerFileWriteBinding.Header, hash);
        }
        protected override string MethodCore => _method;
        protected override string HostCore => "localhost";
        protected override string PeerCore => "unix";
        protected override DateTime DeadlineCore => DateTime.UtcNow.AddSeconds(45);
        protected override Metadata RequestHeadersCore => _headers;
        protected override CancellationToken CancellationTokenCore => default;
        protected override Metadata ResponseTrailersCore => [];
        protected override Status StatusCore { get; set; }
        protected override WriteOptions? WriteOptionsCore { get; set; }
        protected override AuthContext AuthContextCore => null!;
        protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options) => throw new NotSupportedException();
        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) => Task.CompletedTask;
    }
}
