using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Configuration;
using WhaleDeck.Contracts;
using WhaleDeck.Infrastructure.Agent;
using App = WhaleDeck.Application.Models;
using Rpc = WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.UnitTests.Agent;

/// <summary>Isolated SDK projection and full signature validation. Not an installed Agent acceptance.</summary>
public sealed class ContainerRecycleGatewayTests
{
    private static App.ContainerRecycleCommandDto Command => new(new(new('a', 64), "777:2026-10-27T01:02:03.123456789Z", "work/中文.bin"), new('9', 64));
    private static App.ContainerRecyclePlanDto Plan => new(1,Command,new('A',64),new(101,202),new('B',64),1000,1001,new('C',64),
        new(3,4,0x81A0,2100,3100,1,1048583,1722222222222222222),1733333333333333333,new('8',64),
        [new("user.note",[0,255,1])]);
    private static App.TerminalActor Actor => new("synthetic-admin", null, [], new('F', 64), "synthetic-trace", null);
    private static App.ContainerRecycleOperationDto Operation => new(Guid.ParseExact("aa0a8865343e49bdb01a72680131bf39", "N"), new('D', 64), Plan,
        new(Actor.Subject, Actor.TraceId, Guid.ParseExact("aa0a8865343e49bdb01a72680131bf39", "N"), Guid.ParseExact("bb0a8865343e49bdb01a72680131bf39", "N")));

    [Theory]
    [InlineData(1U, 55U, true)][InlineData(2U, 56U, true)][InlineData(1U, 56U, false)]
    public async Task CapabilityNegotiationFailsBeforeSendingAPlan(uint major, uint minor, bool capability)
    {
        using var h = new Harness(major, minor, capability);
        var error = await Assert.ThrowsAsync<RpcException>(() => h.Gateway.PlanContainerRecycleAsync(Actor, Command, default));
        Assert.Equal(StatusCode.Unimplemented, error.StatusCode); Assert.Null(h.Request);
    }
    [Fact]
    public async Task PlanningBindsTargetBytesAttributesActorTraceAndMapsDerivedKernelOwnership()
    {
        using var h = new Harness(); var result = await h.Gateway.PlanContainerRecycleAsync(Actor, Command, default);
        Assert.Equal(Plan.Command,result.Command); Assert.Equal(Plan.SourceIdentity,result.SourceIdentity); Assert.Equal(new byte[]{0,255,1},Assert.Single(result.Attributes).Value); var request = Assert.IsType<Rpc.ContainerRecyclePlanRequest>(h.Request);
        Assert.Equal(Command.Location.Path, request.Command.Location.Path); Assert.Equal(Command.ExpectedVersion, request.Command.ExpectedVersion);
        Assert.Equal(Actor.Subject, request.Context.ActorSubject); Assert.Equal(Actor.TraceId, request.Context.TraceId);
        Assert.Empty(request.Context.JobId); Assert.Empty(request.Context.IdempotencyKey); h.AssertSignature();
    }
    [Theory]
    [InlineData("Apply")][InlineData("Inspect")][InlineData("Discard")][InlineData("Restore")]
    public async Task ActionBindsWholePlanAndOriginalJobThenValidatesTerminalReceipt(string action)
    {
        using var h = new Harness(); var operation = Operation;
        if(action=="Restore") operation=operation with {Context=operation.Context with {JobId=Guid.NewGuid()}};
        var receipt = action switch
        {
            "Apply" => await h.Gateway.ApplyContainerRecycleAsync(operation, default),
            "Inspect" => await h.Gateway.InspectContainerRecycleAsync(operation, default),
            "Restore" => await h.Gateway.RestoreContainerRecycleAsync(operation, default),
            _ => await h.Gateway.DiscardContainerRecycleAsync(operation, default)
        };
        Assert.NotNull(receipt); Assert.Equal(operation.Id, receipt.Id); Assert.Equal(action=="Restore"?"Restored":"Trashed", receipt.State); Assert.True(receipt.BindingCurrent);
        var request = Assert.IsType<Rpc.ContainerRecycleRequest>(h.Request);
        Assert.Equal(ContainerRecyclePolicyTests.Plan(), request.Plan); Assert.Equal(operation.Context.JobId.ToString("N"),request.Context.JobId);
        Assert.Equal(operation.Owner, request.Owner); Assert.Equal(operation.Context.IdempotencyKey.ToString("N"), request.Context.IdempotencyKey);
        h.AssertSignature();
    }
    [Theory]
    [InlineData(StatusCode.NotFound, "CONTAINER_RECYCLE_NOT_FOUND", true)]
    [InlineData(StatusCode.NotFound, "CONTAINER_FILES_TARGET_MISSING", false)]
    [InlineData(StatusCode.Unavailable, "CONTAINER_RECYCLE_UNAVAILABLE", false)]
    [InlineData(StatusCode.FailedPrecondition, "CONTAINER_FILES_TARGET_MISSING", false)]
    public async Task OnlyExactPrivateReceiptAbsenceMapsToNull(StatusCode status, string detail, bool absent)
    {
        using var h = new Harness { Status = status, Detail = detail };
        if (absent) Assert.Null(await h.Gateway.InspectContainerRecycleAsync(Operation, default));
        else
        { var error = await Assert.ThrowsAsync<RpcException>(() => h.Gateway.InspectContainerRecycleAsync(Operation, default)); Assert.Equal(status, error.StatusCode); }
    }
    [Theory]
    [InlineData("target")][InlineData("proof")][InlineData("owner")][InlineData("state")][InlineData("binding")][InlineData("history")][InlineData("historyDigest")][InlineData("presence")]
    public async Task CorruptResponsesDoNotBecomeAcknowledgedSuccess(string changed)
    {
        using var h = new Harness { Corrupt = changed };
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Gateway.ApplyContainerRecycleAsync(Operation, default));
    }
    [Fact]
    public async Task UnknownPreservesAbsentFactsAndRestoreRequiresANewJobBeforeAnyCall()
    {
        using var h=new Harness {Unknown=true};var r=await h.Gateway.ApplyContainerRecycleAsync(Operation,default);
        Assert.Equal("Unknown",r.State);Assert.Null(r.SourceRetained);Assert.Null(r.SourceRestored);h.AssertSignature();
        using var bad=new Harness();await Assert.ThrowsAsync<ArgumentException>(()=>bad.Gateway.RestoreContainerRecycleAsync(Operation,default));Assert.Null(bad.Request);
    }
    private sealed class Harness : HttpMessageHandler
    {
        private readonly string _file = Path.GetTempFileName();
        private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
        private readonly uint _major, _minor; private readonly bool _capability;
        public AgentGateway Gateway { get; }
        public IMessage? Request { get; private set; }
        public StatusCode Status { get; init; } = StatusCode.OK;
        public string Detail { get; init; } = "";
        public string? Corrupt { get; init; }
        public bool Unknown {get;init;}
        private string _method = "", _token = "", _hash = "";
        public Harness(uint major = 1, uint minor = 56, bool capability = true)
        {
            _major = major; _minor = minor; _capability = capability; File.WriteAllBytes(_file, _key);
            Gateway = new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:CapabilityKeyFile"] = _file }).Build());
            var channel = GrpcChannel.ForAddress("http://127.0.0.1", new() { HttpHandler = this, DisposeHttpClient = false });
            var field = typeof(AgentGateway).GetField("_channel", BindingFlags.Instance | BindingFlags.NonPublic)!;
            ((GrpcChannel)field.GetValue(Gateway)!).Dispose(); field.SetValue(Gateway, channel);
            typeof(AgentGateway).GetField("_agent", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Gateway, new Rpc.AgentService.AgentServiceClient(channel));
        }
        public void AssertSignature()
        {
            Assert.NotNull(Request); Assert.Equal(ContainerRecycleBinding.Hash(Request), _hash);
            var parts = _token.Split('.'); Assert.Equal(3, parts.Length);
            Assert.InRange(long.Parse(parts[0], CultureInfo.InvariantCulture) - DateTimeOffset.UtcNow.ToUnixTimeSeconds(), 1, 60);
            var mac = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(ContainerRecycleBinding.Payload(parts[0], parts[1], _method, _hash)));
            Assert.Equal(Convert.ToHexString(mac), parts[2]);
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath; var capability = path.EndsWith("/GetCapabilities", StringComparison.Ordinal); IMessage? frame;
            if (capability)
            {
                var caps = new Rpc.CapabilitiesResponse { ProtocolMajor = _major, ProtocolMinor = _minor };
                if (_capability) caps.Capabilities.Add("docker.files.recycle"); frame = caps;
            }
            else
            {
                _method = path; _token = Assert.Single(request.Headers.GetValues("x-whaledeck-capability")); _hash = Assert.Single(request.Headers.GetValues(ContainerRecycleBinding.Header));
                var raw = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
                Assert.Equal(0, raw[0]); Assert.Equal(raw.Length - 5, BinaryPrimitives.ReadInt32BigEndian(raw.AsSpan(1, 4)));
                if (path.EndsWith("/Plan", StringComparison.Ordinal))
                { Request = Rpc.ContainerRecyclePlanRequest.Parser.ParseFrom(raw.AsSpan(5)); frame = ContainerRecyclePolicyTests.Plan(); }
                else
                {
                    Request = Rpc.ContainerRecycleRequest.Parser.ParseFrom(raw.AsSpan(5)); var op = (Rpc.ContainerRecycleRequest)Request;
                    var state=Unknown?"Unknown":path.EndsWith("/Restore",StringComparison.Ordinal)?"Restored":"Trashed";
                    var receipt = new Rpc.ContainerRecycleReceipt { OperationId=op.OperationId,ContainerId=op.Plan.Command.Location.ContainerId,InstanceId=op.Plan.Command.Location.InstanceId,
                        TargetProof=op.Plan.TargetProof,Fingerprint=op.Plan.Fingerprint,State=state,BindingCurrent=!Unknown,Replayed=true,HistoryMayExist=true,HistoryVerified=true,HistoryAvailable=true,HistoryDigest=new('7',64) };
                    if(!Unknown){receipt.SourceRetained=state=="Trashed";receipt.SourceRestored=state=="Restored";}
                    switch (Corrupt)
                    {
                        case "target": receipt.ContainerId = new('b', 64); break;
                        case "proof": receipt.TargetProof = new('F', 64); break;
                        case "owner": receipt.Fingerprint = new('F', 64); break;
                        case "state": receipt.State = "Applying"; break;
                        case "binding": receipt.State = "Unknown"; break;
                        case "presence": receipt.ClearSourceRetained(); break;
                        case "history": receipt.HistoryMayExist = false; break;
                        case "historyDigest": receipt.HistoryDigest = new('z', 64); break;
                    }
                    frame = receipt;
                }
                if (Status != StatusCode.OK) frame = null;
            }
            using var body = new MemoryStream();
            if (frame is not null)
            { var bytes = frame.ToByteArray(); var prefix = new byte[5]; BinaryPrimitives.WriteInt32BigEndian(prefix.AsSpan(1), bytes.Length); body.Write(prefix); body.Write(bytes); }
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Version = HttpVersion.Version20, Content = new ByteArrayContent(body.ToArray()) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/grpc");
            response.TrailingHeaders.Add("grpc-status", ((int)(capability ? StatusCode.OK : Status)).ToString(CultureInfo.InvariantCulture));
            if (!capability && Detail.Length > 0) response.TrailingHeaders.Add("grpc-message", Uri.EscapeDataString(Detail));
            return response;
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Gateway.Dispose();
                if (typeof(AgentGateway).GetField("_capabilityKey", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Gateway) is byte[] key) CryptographicOperations.ZeroMemory(key);
                CryptographicOperations.ZeroMemory(_key); File.Delete(_file);
            }
            base.Dispose(disposing);
        }
    }
}
