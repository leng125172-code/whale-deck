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
using WhaleDeck.Agent.Services;
using WhaleDeck.Application.Services;
using WhaleDeck.Contracts;
using WhaleDeck.Infrastructure.Agent;
using App = WhaleDeck.Application.Models;
using Rpc = WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.UnitTests.Agent;

/// <summary>Actual SDK serialization against an isolated HTTP/2 handler, not installed UDS evidence.</summary>
public sealed class ContainerCopyGatewayTests
{
    private static App.TerminalActor Actor => new("synthetic-admin", null, [], new('F', 64), "synthetic-trace", null);
    private static App.FileObjectIdentityDto Identity(Rpc.FileObjectIdentity v) => new(v.Device, v.Inode, v.Mode, v.OwnerUid, v.OwnerGid, v.LinkCount, v.SizeBytes, v.ModifiedAtUnixNs);
    private static App.FileParentIdentity Parent(Rpc.FileDirectoryIdentity v) => new(v.Device, v.Inode);
    internal static App.ContainerCopyPlanDto Plan(bool directory = false)
    {
        var p = ContainerCopyPolicyTests.Plan(directory); var c = p.Command; App.FileMutationPlan? file = null; App.FileTreeCopyPlan? tree = null;
        if (directory)
        {
            var t = p.TreePlan;
            tree = new(new(new(t.Command.Source.RootId, t.Command.Source.Path), t.Command.ExpectedVersion, new(t.Command.Target.RootId, t.Command.Target.Path)),
                t.ManifestSha256, (int)t.EntryCount, t.TotalBytes, Parent(t.TargetParent), t.SourcePolicyHash, t.TargetPolicyHash, t.Fingerprint);
        }
        else
        {
            var f = p.FilePlan;
            file = new(new("copy", new(f.Command.Source.RootId, f.Command.Source.Path), new(f.Command.Target.RootId, f.Command.Target.Path), f.Command.ExpectedVersion, ""),
                Identity(f.SourceIdentity), Parent(f.SourceParent), Parent(f.TargetParent), f.SizeBytes, f.Sha256, f.Fingerprint);
        }
        return new(p.SchemaVersion, new(new(c.Source.ContainerId, c.Source.InstanceId, c.Source.Path), c.TargetPath, c.ExpectedVersion), p.Kind, p.TargetProof,
            Identity(p.SourceIdentity), p.SourceChangedAtUnixNs, Parent(p.SourceParent), Parent(p.TargetParent), p.TargetParentVersion, p.OwnerUid, p.OwnerGid, p.Fingerprint, file, tree);
    }
    private static App.ContainerCopyOperationDto Operation(bool directory = false)
    {
        var id = Guid.ParseExact("aa0a8865343e49bdb01a72680131bf39", "N");
        return new(id, new('D', 64), Plan(directory), new(Actor.Subject, Actor.TraceId, id, Guid.ParseExact("bb0a8865343e49bdb01a72680131bf39", "N")));
    }
    [Theory][InlineData(1U, 44U, true)][InlineData(2U, 45U, true)][InlineData(1U, 45U, false)]
    public async Task OldProtocolWrongMajorAndMissingFeatureStopBeforePlan(uint major, uint minor, bool capability)
    {
        using var h = new Harness(major, minor, capability);
        var error = await Assert.ThrowsAsync<RpcException>(() => h.Gateway.PlanContainerCopyAsync(Actor, Plan().Command, default));
        Assert.Equal(StatusCode.Unimplemented, error.StatusCode); Assert.Null(h.Request);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task PlanningPreservesTypedInnerPlanCompleteSourceAndOriginalActor(bool directory)
    {
        using var h = new Harness(directory: directory); var expected = Plan(directory);
        var result = await h.Gateway.PlanContainerCopyAsync(Actor, expected.Command, default);
        Assert.Equal(expected, result); Assert.Equal(ContainerCopyPolicy.HostVersion(ContainerCopyPolicyTests.Plan(directory)), ContainerCopyValidation.HostVersion(result));
        var request = Assert.IsType<Rpc.ContainerCopyPlanRequest>(h.Request);
        Assert.Equal(Actor.Subject, request.Context.ActorSubject); Assert.Empty(request.Context.JobId); h.AssertSignature();
    }
    [Theory][InlineData("Apply", false)][InlineData("Inspect", false)][InlineData("Discard", false)]
    [InlineData("Apply", true)][InlineData("Inspect", true)][InlineData("Discard", true)]
    public async Task EachActionBindsWholePlanJobKeyAndOwnerBeforeValidatingReceipt(string action, bool directory)
    {
        using var h = new Harness(directory: directory); var operation = Operation(directory);
        var result = action switch { "Apply" => await h.Gateway.ApplyContainerCopyAsync(operation, default),
            "Inspect" => await h.Gateway.InspectContainerCopyAsync(operation, default), _ => await h.Gateway.DiscardContainerCopyAsync(operation, default) };
        Assert.NotNull(result); Assert.Equal("Completed", result.State); Assert.Equal(operation.Plan.Kind, result.Kind);
        var request = Assert.IsType<Rpc.ContainerCopyRequest>(h.Request);
        Assert.Equal(ContainerCopyPolicyTests.Plan(directory), request.Plan); Assert.Equal(request.OperationId, request.Context.JobId);
        Assert.Equal(operation.Owner, request.Owner); Assert.Equal(operation.Context.IdempotencyKey.ToString("N"), request.Context.IdempotencyKey); h.AssertSignature();
    }
    [Theory][InlineData(StatusCode.NotFound, "CONTAINER_COPY_NOT_FOUND", true)]
    [InlineData(StatusCode.NotFound, "CONTAINER_FILES_TARGET_MISSING", false)][InlineData(StatusCode.FailedPrecondition, "CONTAINER_FILES_TARGET_MISSING", false)]
    [InlineData(StatusCode.Unavailable, "CONTAINER_COPY_UNAVAILABLE", false)]
    public async Task OnlyExactPrivateReceiptAbsenceMapsToNull(StatusCode status, string detail, bool absent)
    {
        using var h = new Harness { Status = status, Detail = detail };
        if (absent) Assert.Null(await h.Gateway.InspectContainerCopyAsync(Operation(), default));
        else await Assert.ThrowsAsync<RpcException>(() => h.Gateway.InspectContainerCopyAsync(Operation(), default));
    }
    [Theory][InlineData("target")][InlineData("proof")][InlineData("kind")][InlineData("state")][InlineData("binding")][InlineData("private")][InlineData("version")]
    public async Task InvalidReceiptsDoNotBecomeAcknowledgedSuccess(string change)
    {
        using var h = new Harness { Corrupt = change };
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Gateway.ApplyContainerCopyAsync(Operation(), default));
    }
    private sealed class Harness : HttpMessageHandler
    {
        private readonly string _file = Path.GetTempFileName(); private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
        private readonly uint _major, _minor; private readonly bool _capability, _directory;
        public AgentGateway Gateway { get; }
        public IMessage? Request { get; private set; }
        public StatusCode Status { get; init; } = StatusCode.OK; public string Detail { get; init; } = ""; public string? Corrupt { get; init; }
        private string _method = "", _token = "", _hash = "";
        public Harness(uint major = 1, uint minor = 45, bool capability = true, bool directory = false)
        {
            _major = major; _minor = minor; _capability = capability; _directory = directory; File.WriteAllBytes(_file, _key);
            Gateway = new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:CapabilityKeyFile"] = _file }).Build());
            var channel = GrpcChannel.ForAddress("http://127.0.0.1", new() { HttpHandler = this, DisposeHttpClient = false });
            var field = typeof(AgentGateway).GetField("_channel", BindingFlags.Instance | BindingFlags.NonPublic)!;
            ((GrpcChannel)field.GetValue(Gateway)!).Dispose(); field.SetValue(Gateway, channel);
            typeof(AgentGateway).GetField("_agent", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Gateway, new Rpc.AgentService.AgentServiceClient(channel));
        }
        public void AssertSignature()
        {
            Assert.NotNull(Request); Assert.Equal(ContainerCopyBinding.Hash(Request), _hash); var parts = _token.Split('.'); Assert.Equal(3, parts.Length);
            Assert.InRange(long.Parse(parts[0], CultureInfo.InvariantCulture) - DateTimeOffset.UtcNow.ToUnixTimeSeconds(), 1, 60);
            var mac = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(ContainerCopyBinding.Payload(parts[0], parts[1], _method, _hash)));
            Assert.Equal(Convert.ToHexString(mac), parts[2]);
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath; var capability = path.EndsWith("/GetCapabilities", StringComparison.Ordinal); IMessage? frame;
            if (capability)
            {
                var caps = new Rpc.CapabilitiesResponse { ProtocolMajor = _major, ProtocolMinor = _minor };
                if (_capability) caps.Capabilities.Add("docker.files.copy-intents"); frame = caps;
            }
            else
            {
                _method = path; _token = Assert.Single(request.Headers.GetValues("x-whaledeck-capability")); _hash = Assert.Single(request.Headers.GetValues(ContainerCopyBinding.Header));
                var raw = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
                Assert.Equal(0, raw[0]); Assert.Equal(raw.Length - 5, BinaryPrimitives.ReadInt32BigEndian(raw.AsSpan(1, 4)));
                if (path.EndsWith("/Plan", StringComparison.Ordinal)) { Request = Rpc.ContainerCopyPlanRequest.Parser.ParseFrom(raw.AsSpan(5)); frame = ContainerCopyPolicyTests.Plan(_directory); }
                else
                {
                    Request = Rpc.ContainerCopyRequest.Parser.ParseFrom(raw.AsSpan(5)); var op = (Rpc.ContainerCopyRequest)Request;
                    var receipt = new Rpc.ContainerCopyReceipt { OperationId = op.OperationId, ContainerId = op.Plan.Command.Source.ContainerId, InstanceId = op.Plan.Command.Source.InstanceId,
                        Kind = op.Plan.Kind, Fingerprint = op.Plan.Fingerprint, TargetProof = op.Plan.TargetProof, State = "Completed", Version = new('E', 64), BindingCurrent = true, Replayed = true };
                    switch (Corrupt)
                    {
                        case "target": receipt.ContainerId = new('b', 64); break;
                        case "proof": receipt.TargetProof = new('F', 64); break;
                        case "kind": receipt.Kind = "Move"; break;
                        case "state": receipt.State = "Running"; break;
                        case "binding": receipt.State = "Unknown"; receipt.Version = ""; receipt.PrivateMayExist = true; break;
                        case "private": receipt.PrivateMayExist = true; break;
                        case "version": receipt.Version = "bad"; break;
                    }
                    frame = receipt;
                }
                if (Status != StatusCode.OK) frame = null;
            }
            using var body = new MemoryStream();
            if (frame is not null) { var bytes = frame.ToByteArray(); var prefix = new byte[5]; BinaryPrimitives.WriteInt32BigEndian(prefix.AsSpan(1), bytes.Length); body.Write(prefix); body.Write(bytes); }
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Version = HttpVersion.Version20, Content = new ByteArrayContent(body.ToArray()) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/grpc"); response.TrailingHeaders.Add("grpc-status", ((int)(capability ? StatusCode.OK : Status)).ToString(CultureInfo.InvariantCulture));
            if (!capability && Detail.Length > 0) response.TrailingHeaders.Add("grpc-message", Uri.EscapeDataString(Detail)); return response;
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Gateway.Dispose(); if (typeof(AgentGateway).GetField("_capabilityKey", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Gateway) is byte[] key) CryptographicOperations.ZeroMemory(key);
                CryptographicOperations.ZeroMemory(_key); File.Delete(_file);
            }
            base.Dispose(disposing);
        }
    }
}
