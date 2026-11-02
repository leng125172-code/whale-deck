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
public sealed class ContainerRecursivePermissionGatewayTests
{
    private static App.ContainerRecursivePermissionCommandDto Command => new(new(new('a', 64), "777:2026-10-27T01:02:03.123456789Z", "work/中文目录"), "0600", "0750", new('9', 64));
    private static App.ContainerRecursivePermissionPlanDto Plan => new(1, Command, new('A', 64), new(101, 202), new('B', 64), 1000, 1001, new('C', 64),
        new(3, 4, 0x41E8, 2100, 3100, 2, 4096, 1722222222222222222), 1733333333333333333, new('8', 64), 4, 2, 2, 2);
    private static App.TerminalActor Actor => new("synthetic-admin", null, [], new('F', 64), "synthetic-trace", null);
    private static App.ContainerRecursivePermissionOperationDto Operation => new(Guid.ParseExact("aa0a8865343e49bdb01a72680131bf39", "N"), new('D', 64), Plan,
        new(Actor.Subject, Actor.TraceId, Guid.ParseExact("aa0a8865343e49bdb01a72680131bf39", "N"), Guid.ParseExact("bb0a8865343e49bdb01a72680131bf39", "N")));

    [Theory]
    [InlineData(1U, 48U, true)][InlineData(2U, 49U, true)][InlineData(1U, 49U, false)]
    public async Task CapabilityNegotiationFailsBeforeSendingAPlan(uint major, uint minor, bool capability)
    {
        using var h = new Harness(major, minor, capability);
        var error = await Assert.ThrowsAsync<RpcException>(() => h.Gateway.PlanContainerRecursivePermissionAsync(Actor, Command, default));
        Assert.Equal(StatusCode.Unimplemented, error.StatusCode); Assert.Null(h.Request);
    }
    [Fact]
    public async Task PlanningBindsTargetModeActorTraceAndMapsDerivedKernelOwnership()
    {
        using var h = new Harness(); var result = await h.Gateway.PlanContainerRecursivePermissionAsync(Actor, Command, default);
        Assert.Equal(Plan, result); var request = Assert.IsType<Rpc.ContainerRecursivePermissionPlanRequest>(h.Request);
        Assert.Equal(Command.Location.Path, request.Command.Location.Path); Assert.Equal(Command.FileMode, request.Command.FileMode); Assert.Equal(Command.DirectoryMode, request.Command.DirectoryMode);
        Assert.Equal(Actor.Subject, request.Context.ActorSubject); Assert.Equal(Actor.TraceId, request.Context.TraceId);
        Assert.Empty(request.Context.JobId); Assert.Empty(request.Context.IdempotencyKey); h.AssertSignature();
    }
    [Theory]
    [InlineData("Apply")][InlineData("Inspect")][InlineData("Discard")]
    public async Task ActionBindsWholePlanAndOriginalJobThenValidatesTerminalReceipt(string action)
    {
        using var h = new Harness(); var operation = Operation;
        var receipt = action switch
        {
            "Apply" => await h.Gateway.ApplyContainerRecursivePermissionAsync(operation, default),
            "Inspect" => await h.Gateway.InspectContainerRecursivePermissionAsync(operation, default),
            _ => await h.Gateway.DiscardContainerRecursivePermissionAsync(operation, default)
        };
        Assert.NotNull(receipt); Assert.Equal(operation.Id, receipt.Id); Assert.Equal("Completed", receipt.State); Assert.True(receipt.BindingCurrent);
        var request = Assert.IsType<Rpc.ContainerRecursivePermissionRequest>(h.Request);
        Assert.Equal(ContainerRecursivePermissionPolicyTests.Plan(), request.Plan); Assert.Equal(request.OperationId, request.Context.JobId);
        Assert.Equal(operation.Owner, request.Owner); Assert.Equal(operation.Context.IdempotencyKey.ToString("N"), request.Context.IdempotencyKey);
        h.AssertSignature();
    }
    [Theory]
    [InlineData(StatusCode.NotFound, "CONTAINER_RECURSIVE_PERMISSION_NOT_FOUND", true)]
    [InlineData(StatusCode.NotFound, "CONTAINER_FILES_TARGET_MISSING", false)]
    [InlineData(StatusCode.Unavailable, "CONTAINER_RECURSIVE_PERMISSION_UNAVAILABLE", false)]
    [InlineData(StatusCode.FailedPrecondition, "CONTAINER_FILES_TARGET_MISSING", false)]
    public async Task OnlyExactPrivateReceiptAbsenceMapsToNull(StatusCode status, string detail, bool absent)
    {
        using var h = new Harness { Status = status, Detail = detail };
        if (absent) Assert.Null(await h.Gateway.InspectContainerRecursivePermissionAsync(Operation, default));
        else
        { var error = await Assert.ThrowsAsync<RpcException>(() => h.Gateway.InspectContainerRecursivePermissionAsync(Operation, default)); Assert.Equal(status, error.StatusCode); }
    }
    [Theory]
    [InlineData("target")][InlineData("proof")][InlineData("owner")][InlineData("state")][InlineData("binding")][InlineData("completedCount")][InlineData("changedCount")][InlineData("unknownCount")][InlineData("historyAvailable")][InlineData("history")][InlineData("historyDigest")][InlineData("outcomeDigest")]
    public async Task CorruptResponsesDoNotBecomeAcknowledgedSuccess(string changed)
    {
        using var h = new Harness { Corrupt = changed };
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Gateway.ApplyContainerRecursivePermissionAsync(Operation, default));
    }

    [Theory]
    [InlineData(StatusCode.Aborted, "CONTAINER_RECURSIVE_PERMISSION_TREE_CHANGED", "CONTAINER_RECURSIVE_PERMISSION_VERSION_CONFLICT")]
    [InlineData(StatusCode.FailedPrecondition, "CONTAINER_RECURSIVE_PERMISSION_EXTENDED_METADATA_REQUIRED", "CONTAINER_RECURSIVE_PERMISSION_EXTENDED_METADATA_REQUIRED")]
    [InlineData(StatusCode.ResourceExhausted, "CONTAINER_RECURSIVE_PERMISSION_TREE_LIMIT", "CONTAINER_RECURSIVE_PERMISSION_TREE_LIMIT")]
    [InlineData(StatusCode.ResourceExhausted, "CONTAINER_RECURSIVE_PERMISSION_MANIFEST_LIMIT", "CONTAINER_RECURSIVE_PERMISSION_MANIFEST_LIMIT")]
    [InlineData(StatusCode.PermissionDenied, "CONTAINER_UPLOAD_PUBLISH_READ_ONLY", "CONTAINER_RECURSIVE_PERMISSION_PROTECTED")]
    [InlineData(StatusCode.FailedPrecondition, "CONTAINER_FILES_INSTANCE_CHANGED", "CONTAINER_RECURSIVE_PERMISSION_TARGET_CHANGED")]
    public async Task OnlyKnownPlanningRefusalsBecomeBoundedWorkflowErrors(StatusCode status, string detail, string code)
    {
        using var h = new Harness { Status = status, Detail = detail };
        var error = await Assert.ThrowsAsync<App.ContainerRecursivePermissionPreflightRejectedException>(() => h.Gateway.PlanContainerRecursivePermissionAsync(Actor, Command, default));
        Assert.Equal(code, error.Code);
    }
    [Theory]
    [InlineData(StatusCode.Unavailable, "CONTAINER_RECURSIVE_PERMISSION_UNAVAILABLE")]
    [InlineData(StatusCode.ResourceExhausted, "unknown private text")]
    public async Task TransportFailureCannotBeMisreportedAsSafePreflightRefusal(StatusCode status, string detail)
    {
        using var h = new Harness { Status = status, Detail = detail };
        var error = await Assert.ThrowsAsync<RpcException>(() => h.Gateway.PlanContainerRecursivePermissionAsync(Actor, Command, default));
        Assert.Equal(status, error.StatusCode);
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
        private string _method = "", _token = "", _hash = "";
        public Harness(uint major = 1, uint minor = 49, bool capability = true)
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
            Assert.NotNull(Request); Assert.Equal(ContainerRecursivePermissionBinding.Hash(Request), _hash);
            var parts = _token.Split('.'); Assert.Equal(3, parts.Length);
            Assert.InRange(long.Parse(parts[0], CultureInfo.InvariantCulture) - DateTimeOffset.UtcNow.ToUnixTimeSeconds(), 1, 60);
            var mac = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(ContainerRecursivePermissionBinding.Payload(parts[0], parts[1], _method, _hash)));
            Assert.Equal(Convert.ToHexString(mac), parts[2]);
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath; var capability = path.EndsWith("/GetCapabilities", StringComparison.Ordinal); IMessage? frame;
            if (capability)
            {
                var caps = new Rpc.CapabilitiesResponse { ProtocolMajor = _major, ProtocolMinor = _minor };
                if (_capability) caps.Capabilities.Add("docker.files.recursive-permission-intents"); frame = caps;
            }
            else
            {
                _method = path; _token = Assert.Single(request.Headers.GetValues("x-whaledeck-capability")); _hash = Assert.Single(request.Headers.GetValues(ContainerRecursivePermissionBinding.Header));
                var raw = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
                Assert.Equal(0, raw[0]); Assert.Equal(raw.Length - 5, BinaryPrimitives.ReadInt32BigEndian(raw.AsSpan(1, 4)));
                if (path.EndsWith("/Plan", StringComparison.Ordinal))
                { Request = Rpc.ContainerRecursivePermissionPlanRequest.Parser.ParseFrom(raw.AsSpan(5)); frame = ContainerRecursivePermissionPolicyTests.Plan(); }
                else
                {
                    Request = Rpc.ContainerRecursivePermissionRequest.Parser.ParseFrom(raw.AsSpan(5)); var op = (Rpc.ContainerRecursivePermissionRequest)Request;
                    var receipt = new Rpc.ContainerRecursivePermissionReceipt { OperationId = op.OperationId, ContainerId = op.Plan.Command.Location.ContainerId, InstanceId = op.Plan.Command.Location.InstanceId,
                        TargetProof = op.Plan.TargetProof, Fingerprint = op.Plan.Fingerprint, State = "Completed", BindingCurrent = true, Replayed = true,
                        CompletedCount = 4, ChangedCount = 2, NotStartedCount = 0, UnknownCount = 0,
                        HistoryMayExist = true, HistoryVerified = true, HistoryAvailable = true, HistoryDigest = new('7', 64), OutcomeDigest = new('6', 64) };
                    switch (Corrupt)
                    {
                        case "target": receipt.ContainerId = new('b', 64); break;
                        case "proof": receipt.TargetProof = new('F', 64); break;
                        case "owner": receipt.Fingerprint = new('F', 64); break;
                        case "state": receipt.State = "Applying"; break;
                        case "binding": receipt.State = "Partial"; receipt.CompletedCount = 1; receipt.ChangedCount = 1; receipt.NotStartedCount = 3; break;
                        case "completedCount": receipt.CompletedCount = 3; break;
                        case "changedCount": receipt.ChangedCount = 0; break;
                        case "unknownCount": receipt.UnknownCount = 1; break;
                        case "historyAvailable": receipt.HistoryAvailable = false; break;
                        case "history": receipt.HistoryMayExist = false; break;
                        case "historyDigest": receipt.HistoryDigest = new('z', 64); break;
                        case "outcomeDigest": receipt.OutcomeDigest = "bad"; break;
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
