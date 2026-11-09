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
using WhaleDeck.Application.Services;
using WhaleDeck.Contracts;
using WhaleDeck.Infrastructure.Agent;
using App = WhaleDeck.Application.Models;
using Rpc = WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.UnitTests.Agent;

// Synthetic SDK/HTTP2 frames only; not installed Agent/native acceptance.
public sealed class ContainerRecycleArchiveRetirementGatewayTests
{
    private static App.TerminalActor Actor => new("synthetic-admin", null, [], new('F', 64), "synthetic-trace", null);
    private static Rpc.ContainerRecycleArchiveRetirementRequest Request()
    {
        var q = ContainerRecycleArchiveRetirementContractTests.Request(); q.Context.JobId = q.OperationId;
        q.Plan.PublishedIdentity.Device = q.Plan.Command.Archive.Plan.ParentIdentity.Device;
        q.Plan.Fingerprint = ContainerRecycleArchiveRetirementWire.Fingerprint(q.Plan); return q;
    }
    private static App.FileObjectIdentityDto Identity(Rpc.FileObjectIdentity i) => new(i.Device, i.Inode, i.Mode, i.OwnerUid, i.OwnerGid, i.LinkCount, i.SizeBytes, i.ModifiedAtUnixNs);
    private static App.ContainerRecycleArchiveRetirementPlanDto Plan(Rpc.ContainerRecycleArchiveRetirementPlan p) => new(p.SchemaVersion,
        new(new(Guid.ParseExact(p.Command.Archive.OperationId, "N"), p.Command.Archive.Owner, ContainerRecycleArchiveGatewayTests.Plan(p.Command.Archive.Plan)), p.Command.RetainDays, p.Command.Mode),
        p.ArchiveRecordDigest, p.SourceRecordDigest, p.PurgeRecordDigest, p.PurgeMarkerDigest, p.ClaimDigest, p.HistoryMetadataDigest,
        p.HistoryCreatedAtUnixMs, p.NotBeforeUnixMs, p.RetentionElapsed, p.BindingCurrent, p.RetirementEligible, p.StorageHash, new(p.StoreIdentity.Device, p.StoreIdentity.Inode), Identity(p.ArchiveIdentity), p.ArchiveChangedAtUnixNs,
        p.ArchiveBytes, p.ArchiveSha256, Identity(p.PublishedIdentity), p.PublishedChangedAtUnixNs, p.Fingerprint);
    private static App.ContainerRecycleArchiveRetirementOperationDto Operation()
    {
        var q = Request(); return new(Guid.ParseExact(q.OperationId, "N"), q.Owner, Plan(q.Plan), new(q.Context.ActorSubject, q.Context.TraceId, Guid.ParseExact(q.Context.JobId, "N"), Guid.ParseExact(q.Context.IdempotencyKey, "N")));
    }
    [Theory][InlineData(1U, 59U, true)][InlineData(2U, 60U, true)][InlineData(1U, 60U, false)]
    public async Task IncompleteNegotiationRejectsEveryMethodBeforeSigning(uint major, uint minor, bool available)
    {
        using var h = new Harness(major, minor, available, keyBytes: 0); var op = Operation();
        var errors = new[] {
            await Assert.ThrowsAsync<RpcException>(() => h.Gateway.RequireContainerRecycleArchiveRetirementAsync(default)),
            await Assert.ThrowsAsync<RpcException>(() => h.Gateway.PlanContainerRecycleArchiveRetirementAsync(Actor, op.Plan.Command, default)),
            await Assert.ThrowsAsync<RpcException>(() => h.Gateway.ReserveContainerRecycleArchiveRetirementAsync(op, default)),
            await Assert.ThrowsAsync<RpcException>(() => h.Gateway.ApplyContainerRecycleArchiveRetirementAsync(op, default)),
            await Assert.ThrowsAsync<RpcException>(() => h.Gateway.InspectContainerRecycleArchiveRetirementAsync(op, default)),
            await Assert.ThrowsAsync<RpcException>(() => h.Gateway.DiscardContainerRecycleArchiveRetirementAsync(op, default)) };
        Assert.All(errors, e => { Assert.Equal(StatusCode.Unimplemented, e.StatusCode); Assert.Equal("CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_CAPABILITY_UNAVAILABLE", e.Status.Detail); });
        Assert.Null(h.Request); Assert.Equal(6, h.CapabilityCalls);
    }
    [Theory][InlineData(0)][InlineData(31)]
    public async Task SupportedPeerWithoutSigningMaterialReceivesNoAction(int length)
    {
        using var h = new Harness(keyBytes: length);
        var error = await Assert.ThrowsAsync<RpcException>(() => h.Gateway.ApplyContainerRecycleArchiveRetirementAsync(Operation(), default));
        Assert.Equal(StatusCode.PermissionDenied, error.StatusCode); Assert.Null(h.Request);
    }
    [Fact]
    public async Task PlanningFreezesNestedArraysAndBindsActorAndTrace()
    {
        using var h = new Harness(); var c = Operation().Plan.Command;
        var bytes = c.Archive.Plan.Command.Purge.Plan.Command.Source.Plan.Attributes[0].Value.ToArray();
        h.OnCapabilities = () => c.Archive.Plan.Command.Purge.Plan.Command.Source.Plan.Attributes[0].Value[0] ^= 42;
        var result = await h.Gateway.PlanContainerRecycleArchiveRetirementAsync(Actor, c, default);
        var q = Assert.IsType<Rpc.ContainerRecycleArchiveRetirementPlanRequest>(h.Request);
        Assert.Equal(bytes, q.Command.Archive.Plan.Command.Purge.Plan.Command.Source.Plan.Attributes[0].Value.ToByteArray());
        Assert.Equal(bytes, result.Command.Archive.Plan.Command.Purge.Plan.Command.Source.Plan.Attributes[0].Value);
        Assert.Equal(Actor.Subject, q.Context.ActorSubject); Assert.Equal(Actor.TraceId, q.Context.TraceId); Assert.Empty(q.Context.JobId); h.AssertSignature();
    }
    [Theory][InlineData("Reserve", "Reserved")][InlineData("Apply", "Completed")][InlineData("Inspect", "Completed")][InlineData("Discard", "Aborted")]
    public async Task ActionsSignDistinctAuthorityAndPreserveEveryField(string action, string state)
    {
        using var h = new Harness(); var op = Operation(); var r = action switch {
            "Reserve" => await h.Gateway.ReserveContainerRecycleArchiveRetirementAsync(op, default),
            "Apply" => await h.Gateway.ApplyContainerRecycleArchiveRetirementAsync(op, default),
            "Inspect" => await h.Gateway.InspectContainerRecycleArchiveRetirementAsync(op, default),
            _ => await h.Gateway.DiscardContainerRecycleArchiveRetirementAsync(op, default) };
        Assert.NotNull(r); Assert.Equal(state, r.State); Assert.Equal(op.Plan.NotBeforeUnixMs, r.NotBeforeUnixMs);
        var q = Assert.IsType<Rpc.ContainerRecycleArchiveRetirementRequest>(h.Request);
        Assert.Equal(Request(), q); Assert.Equal(q.OperationId, q.Context.JobId);
        Assert.NotEqual(q.OperationId, q.Plan.Command.Archive.OperationId); h.AssertSignature();
    }
    [Fact]
    public async Task ExecutionAlsoFreezesNestedArraysBeforeNegotiation()
    {
        using var h = new Harness(); var op = Operation(); var bytes = op.Plan.Command.Archive.Plan.Command.Purge.Plan.Command.Source.Plan.Attributes[0].Value.ToArray();
        h.OnCapabilities = () => op.Plan.Command.Archive.Plan.Command.Purge.Plan.Command.Source.Plan.Attributes[0].Value[0] ^= 42;
        await h.Gateway.ApplyContainerRecycleArchiveRetirementAsync(op, default);
        Assert.Equal(bytes, Assert.IsType<Rpc.ContainerRecycleArchiveRetirementRequest>(h.Request).Plan.Command.Archive.Plan.Command.Purge.Plan.Command.Source.Plan.Attributes[0].Value.ToByteArray()); h.AssertSignature();
    }
    [Theory][InlineData(null)][InlineData(false)]
    public async Task UnknownRetainsItsNullableEvidence(bool? applied)
    {
        using var h = new Harness { Unknown = true, UnknownApplied = applied };
        Assert.Equal(applied, (await h.Gateway.ApplyContainerRecycleArchiveRetirementAsync(Operation(), default)).RetirementApplied);
    }
    [Theory]
    [InlineData(StatusCode.NotFound, "CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_NOT_FOUND", true)]
    [InlineData(StatusCode.NotFound, "CONTAINER_RECYCLE_ARCHIVE_NOT_FOUND", false)]
    [InlineData(StatusCode.NotFound, "CONTAINER_RECYCLE_NOT_FOUND", false)]
    [InlineData(StatusCode.Unavailable, "CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_UNAVAILABLE", false)]
    [InlineData(StatusCode.FailedPrecondition, "CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_NOT_FOUND", false)]
    public async Task OnlyExactRetirementAbsenceBecomesNull(StatusCode status, string detail, bool absent)
    {
        using var h = new Harness { Status = status, Detail = detail };
        if (absent) Assert.Null(await h.Gateway.InspectContainerRecycleArchiveRetirementAsync(Operation(), default));
        else await Assert.ThrowsAsync<RpcException>(() => h.Gateway.InspectContainerRecycleArchiveRetirementAsync(Operation(), default));
    }
    [Theory][InlineData("source")][InlineData("archive")][InlineData("id")][InlineData("digest")][InlineData("presence")][InlineData("state")][InlineData("due")][InlineData("owned")][InlineData("unknown")]
    public async Task CorruptReceiptCannotBecomeCompletion(string corrupt)
    {
        using var h = new Harness { Corrupt = corrupt };
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Gateway.ApplyContainerRecycleArchiveRetirementAsync(Operation(), default));
    }
    [Theory][InlineData("plan-device")][InlineData("plan-due")][InlineData("plan-command")][InlineData("plan-missing")][InlineData("plan-unknown")]
    public async Task InvalidPlansAreRejectedEvenWithRecomputedOuterDigest(string corrupt)
    {
        using var h = new Harness { Corrupt = corrupt };
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Gateway.PlanContainerRecycleArchiveRetirementAsync(Actor, Operation().Plan.Command, default));
    }
    [Theory][InlineData("archive")][InlineData("purge")][InlineData("source")][InlineData("job")][InlineData("due")][InlineData("published")]
    public async Task InvalidOperationIsRejectedBeforeAnyNetworkCall(string corrupt)
    {
        using var h = new Harness(); var op = Operation();
        op = corrupt switch {
            "archive" => op with { Id = op.Plan.Command.Archive.OperationId, Context = op.Context with { JobId = op.Plan.Command.Archive.OperationId } },
            "purge" => op with { Id = op.Plan.Command.Archive.Plan.Command.Purge.OperationId, Context = op.Context with { JobId = op.Plan.Command.Archive.Plan.Command.Purge.OperationId } },
            "source" => op with { Id = op.Plan.Command.Archive.Plan.Command.Purge.Plan.Command.Source.OperationId, Context = op.Context with { JobId = op.Plan.Command.Archive.Plan.Command.Purge.Plan.Command.Source.OperationId } },
            "job" => op with { Context = op.Context with { JobId = Guid.NewGuid() } },
            "due" => op with { Plan = op.Plan with { NotBeforeUnixMs = op.Plan.NotBeforeUnixMs + 1 } },
            _ => op with { Plan = op.Plan with { PublishedIdentity = op.Plan.ArchiveIdentity } } };
        await Assert.ThrowsAsync<ArgumentException>(() => h.Gateway.ApplyContainerRecycleArchiveRetirementAsync(op, default)); Assert.Equal(0, h.CapabilityCalls); Assert.Null(h.Request);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task RehashingOuterPlansDoesNotHideInvalidAncestorDigest(bool purge)
    {
        using var h = new Harness(); var q = Request();
        if (purge) { q.Plan.Command.Archive.Plan.Command.Purge.Plan.Fingerprint = new('F', 64); q.Plan.Command.Archive.Plan.Fingerprint = ContainerRecycleArchiveWire.Fingerprint(q.Plan.Command.Archive.Plan); }
        else q.Plan.Command.Archive.Plan.Fingerprint = new('F', 64);
        q.Plan.Fingerprint = ContainerRecycleArchiveRetirementWire.Fingerprint(q.Plan);
        var op = Operation() with { Plan = Plan(q.Plan) };
        await Assert.ThrowsAsync<ArgumentException>(() => h.Gateway.ApplyContainerRecycleArchiveRetirementAsync(op, default)); Assert.Equal(0, h.CapabilityCalls);
    }
    [Fact]
    public void ApplicationReceiptRulesRetainUnknownFalseAndRejectUnknownTrue()
    {
        var op = Operation(); var r = new App.ContainerRecycleArchiveRetirementReceiptDto(op.Id, op.Plan.Command.Archive.Plan.Command.Purge.Plan.Command.Source.OperationId, op.Plan.Command.Archive.OperationId, op.Plan.Fingerprint, "Unknown", true, true, false, op.Plan.NotBeforeUnixMs);
        ContainerRecycleArchiveRetirementValidation.Receipt(op, r);
        Assert.Throws<InvalidOperationException>(() => ContainerRecycleArchiveRetirementValidation.Receipt(op, r with { RetirementApplied = true }));
    }

    private sealed class Harness : HttpMessageHandler
    {
        private readonly byte[] _key;
        private readonly string _file = Path.GetTempFileName();
        private readonly uint _major, _minor;
        private readonly bool _available;
        private string _method = "", _token = "", _hash = "";
        public AgentGateway Gateway { get; }
        public IMessage? Request { get; private set; }
        public int CapabilityCalls { get; private set; }
        public Action? OnCapabilities { get; set; }
        public bool Unknown { get; init; }
        public bool? UnknownApplied { get; init; }
        public string? Corrupt { get; init; }
        public StatusCode Status { get; init; } = StatusCode.OK;
        public string Detail { get; init; } = "";
        public Harness(uint major = 1, uint minor = 60, bool available = true, int keyBytes = 32)
        {
            _major = major; _minor = minor; _available = available; _key = RandomNumberGenerator.GetBytes(keyBytes);
            File.WriteAllBytes(_file, _key);
            if (OperatingSystem.IsLinux()) File.SetUnixFileMode(_file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            Gateway = new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:CapabilityKeyFile"] = _file }).Build());
            var field = typeof(AgentGateway).GetField("_channel", BindingFlags.Instance | BindingFlags.NonPublic)!;
            ((GrpcChannel)field.GetValue(Gateway)!).Dispose();
            var channel = GrpcChannel.ForAddress("http://127.0.0.1", new() { HttpHandler = this, DisposeHttpClient = false }); field.SetValue(Gateway, channel);
            typeof(AgentGateway).GetField("_agent", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Gateway, new Rpc.AgentService.AgentServiceClient(channel));
        }
        public void AssertSignature()
        {
            Assert.NotNull(Request);
            var expectedHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("whaledeck-container-recycle-archive-retirement-request-v1\n" + Request.Descriptor.FullName + "\n").Concat(Request.ToByteArray()).ToArray()));
            Assert.Equal(expectedHash, _hash);
            var parts = _token.Split('.'); Assert.Equal(3, parts.Length); Assert.Equal(Guid.ParseExact(parts[1], "N").ToString("N"), parts[1]);
            Assert.InRange(long.Parse(parts[0], CultureInfo.InvariantCulture) - DateTimeOffset.UtcNow.ToUnixTimeSeconds(), 1, 60);
            string Mac(string domain, string method) => Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(domain + "\n" + parts[0] + "." + parts[1] + "." + method + "." + _hash)));
            Assert.Equal(Mac("whaledeck-container-recycle-archive-retirement-capability-v1", _method), parts[2]);
            Assert.NotEqual(Mac("whaledeck-container-recycle-archive-capability-v1", _method), parts[2]);
            Assert.NotEqual(Mac("whaledeck-container-recycle-archive-retirement-capability-v1", _method + "different"), parts[2]);
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath; var caps = path.EndsWith("/GetCapabilities", StringComparison.Ordinal); IMessage? frame;
            if (caps)
            {
                CapabilityCalls++; Assert.False(request.Headers.Contains("x-whaledeck-capability")); Assert.False(request.Headers.Contains(ContainerRecycleArchiveRetirementBinding.Header));
                OnCapabilities?.Invoke(); var response = new Rpc.CapabilitiesResponse { ProtocolMajor = _major, ProtocolMinor = _minor };
                if (_available) response.Capabilities.Add(ContainerRecycleArchiveRetirementBinding.Capability); frame = response;
            }
            else
            {
                Assert.StartsWith(ContainerRecycleArchiveRetirementBinding.Service, path, StringComparison.Ordinal);
                _method = path; _token = Assert.Single(request.Headers.GetValues("x-whaledeck-capability")); _hash = Assert.Single(request.Headers.GetValues(ContainerRecycleArchiveRetirementBinding.Header));
                var raw = await request.Content!.ReadAsByteArrayAsync(cancellationToken); Assert.Equal(0, raw[0]); Assert.Equal(raw.Length - 5, BinaryPrimitives.ReadInt32BigEndian(raw.AsSpan(1, 4)));
                if (path.EndsWith("/Plan", StringComparison.Ordinal))
                {
                    Request = Rpc.ContainerRecycleArchiveRetirementPlanRequest.Parser.ParseFrom(raw.AsSpan(5)); var p = ContainerRecycleArchiveRetirementGatewayTests.Request().Plan;
                    switch (Corrupt)
                    {
                        case "plan-device": p.PublishedIdentity.Device++; break;
                        case "plan-due": p.NotBeforeUnixMs++; break;
                        case "plan-command": p.Command.RetainDays++; p.NotBeforeUnixMs += 86400000; break;
                        case "plan-missing": p.PublishedIdentity = null; break;
                    }
                    if (p.PublishedIdentity is not null) p.Fingerprint = ContainerRecycleArchiveRetirementWire.Fingerprint(p);
                    frame = Corrupt == "plan-unknown" ? Rpc.ContainerRecycleArchiveRetirementPlan.Parser.ParseFrom(p.ToByteArray().Concat(new byte[] { 248, 7, 1 }).ToArray()) : p;
                }
                else
                {
                    var q = Rpc.ContainerRecycleArchiveRetirementRequest.Parser.ParseFrom(raw.AsSpan(5)); Request = q;
                    var state = Unknown ? "Unknown" : path.EndsWith("/Reserve", StringComparison.Ordinal) ? "Reserved" : path.EndsWith("/Discard", StringComparison.Ordinal) ? "Aborted" : "Completed";
                    var r = new Rpc.ContainerRecycleArchiveRetirementReceipt { OperationId = q.OperationId, SourceOperationId = q.Plan.Command.Archive.Plan.Command.Purge.Plan.Command.Source.OperationId, ArchiveOperationId = q.Plan.Command.Archive.OperationId,
                        Fingerprint = q.Plan.Fingerprint, State = state, SourceFenced = state != "Aborted", OwnedFence = state != "Aborted", NotBeforeUnixMs = q.Plan.NotBeforeUnixMs };
                    if (Unknown) { if (UnknownApplied.HasValue) r.RetirementApplied = UnknownApplied.Value; } else r.RetirementApplied = state == "Completed";
                    switch (Corrupt)
                    {
                        case "source": r.SourceOperationId = q.OperationId; break;
                        case "archive": r.ArchiveOperationId = q.OperationId; break;
                        case "id": r.OperationId = q.Plan.Command.Archive.OperationId; break;
                        case "digest": r.Fingerprint = new('6', 64); break;
                        case "presence": r.ClearRetirementApplied(); break;
                        case "state": r.State = "Removing"; break;
                        case "due": r.NotBeforeUnixMs++; break;
                        case "owned": r.OwnedFence = false; break;
                    }
                    frame = Corrupt == "unknown" ? Rpc.ContainerRecycleArchiveRetirementReceipt.Parser.ParseFrom(r.ToByteArray().Concat(new byte[] { 248, 7, 1 }).ToArray()) : r;
                }
                if (Status != StatusCode.OK) frame = null;
            }
            using var body = new MemoryStream();
            if (frame is not null) { var bytes = frame.ToByteArray(); var prefix = new byte[5]; BinaryPrimitives.WriteInt32BigEndian(prefix.AsSpan(1), bytes.Length); body.Write(prefix); body.Write(bytes); }
            var result = new HttpResponseMessage(HttpStatusCode.OK) { Version = HttpVersion.Version20, Content = new ByteArrayContent(body.ToArray()) };
            result.Content.Headers.ContentType = new MediaTypeHeaderValue("application/grpc"); result.TrailingHeaders.Add("grpc-status", ((int)(caps ? StatusCode.OK : Status)).ToString(CultureInfo.InvariantCulture));
            if (!caps && Detail.Length > 0) result.TrailingHeaders.Add("grpc-message", Uri.EscapeDataString(Detail)); return result;
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
