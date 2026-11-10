using System.Globalization;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WhaleDeck.Agent.Services;
using WhaleDeck.Application.Development;
using WhaleDeck.Application.Models;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;
using WhaleDeck.Infrastructure.Agent;

namespace WhaleDeck.UnitTests.Agent;

public sealed class DevelopmentRetirementRpcTests
{
    // Python canonical vectors are synthetic wire fixtures, not disk evidence.
    private static JsonObject Fixture(int index = 0) => JsonNode.Parse(File.ReadAllText(Path.Combine(
        AppContext.BaseDirectory, "Agent/Fixtures/development-retirement-protocol.json")))![index]!.AsObject();
    private static JsonObject Envelope(int index = 0) => Fixture(index)["receipt"]!.AsObject();
    private static WorkspaceRetirementRequest Request(int index = 0)
    {
        var value = Fixture(index)["request"]!; var prerequisite = value["prerequisite"]!;
        return new()
        {
            Storage = new() { WorkspaceId = prerequisite["storage"]!["workspaceId"]!.GetValue<string>(),
                StorageRootId = "nvme-main", EnvironmentSha256 = new string('a', 64), DiskMib = 256, ActorSubject = "owner", TraceId = "trace" },
            TaskId = value["taskId"]!.GetValue<string>(), RetirementId = value["retirementId"]!.GetValue<string>(),
            ArchiveId = prerequisite["archiveId"]!.GetValue<string>(), Phase = value["phase"]!.GetValue<string>(), ExpectedEpoch = 2,
            ArchivePlanSha256 = prerequisite["archivePlanSha256"]!.GetValue<string>(),
            CaptureReceiptSha256 = prerequisite["captureReceiptSha256"]!.GetValue<string>(),
            RetirementPlanSha256 = value["retirementPlanSha256"]!.GetValue<string>()
        };
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void IndependentPythonPlanAndExecutionHashesAgree(int index)
    {
        var request = Request(index);
        Assert.True(DevelopmentRetirementBinding.IsValid(request));
        Assert.Equal(Fixture(index)["canonicalRequest"]!.GetValue<string>(), DevelopmentRetirementBinding.HostInput(request));
        Assert.Equal(Fixture(index)["engineSha256"]!.GetValue<string>(), DevelopmentRetirementBinding.EngineHash(request));
        var receipt = DevelopmentRetirementGrpcService.Parse(request, Envelope(index).ToJsonString());
        Assert.True(DevelopmentRetirementBinding.Matches(request, receipt));
        Assert.Equal("Succeeded", receipt.Status);
        var changed = request.Clone(); changed.TaskId = Guid.NewGuid().ToString("D");
        Assert.Equal(DevelopmentRetirementBinding.EngineHash(request), DevelopmentRetirementBinding.EngineHash(changed));
        Assert.False(DevelopmentRetirementBinding.Matches(changed, receipt));
        changed = request.Clone(); changed.Storage.ActorSubject = "new-current-administrator";
        Assert.Equal(DevelopmentRetirementBinding.HostInput(request), DevelopmentRetirementBinding.HostInput(changed));
        Assert.NotEqual(DevelopmentRetirementBinding.Hash(request), DevelopmentRetirementBinding.Hash(changed));
        var invalid = receipt.Clone(); invalid.ResultJson = ByteString.CopyFromUtf8(" " + receipt.ResultJson.ToStringUtf8());
        Assert.False(DevelopmentRetirementBinding.Matches(request, invalid));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void MalformedReceiptsCannotChangeIdentityOrAuthorizeBudgetRelease(int index)
    {
        var request = Request(index);
        void Reject(Action<JsonObject> change)
        {
            var value = Envelope(index); change(value);
            var error = Assert.Throws<RpcException>(() => DevelopmentRetirementGrpcService.Parse(request, value.ToJsonString()));
            Assert.Equal(StatusCode.DataLoss, error.StatusCode);
        }
        Reject(v => v["taskId"] = Guid.NewGuid().ToString("D"));
        Reject(v => v["requestSha256"] = new string('0', 64));
        Reject(v => v["schemaVersion"] = true); Reject(v => v["code"] = "");
        Reject(v => v["result"] = null); Reject(v => v["status"] = "Stopped"); Reject(v => v["extra"] = 1);
        foreach (var name in Envelope(index)["result"]!.AsObject().Select(p => p.Key))
            Reject(v => v["result"]![name] = null);
        foreach (var name in new[] { "retirementPlanSha256", "captureReceiptSha256" })
            Reject(v => v["result"]![name] = new string('0', 64));
        foreach (var name in new[] { "storageProofSha256", "sourceManifestSha256" })
            Reject(v => v["result"]![name] = new string('A', 64));
        if (index == 1)
        {
            Reject(v => v["result"]!["releasesApplicationBudget"] = true);
            Reject(v => v["result"]!["releasesApplicationBudget"] = 0);
            Reject(v => v["result"]!["projectId"] = true);
            Reject(v => v["result"]!["projectId"] = 0);
            Reject(v => v["result"]!["projectId"] = 0x80000000UL);
            foreach (var name in new[] { "sourceRemoved", "quotaReleased", "claimReleased", "retainsHdd" })
            {
                Reject(v => v["result"]![name] = false); Reject(v => v["result"]![name] = 1);
            }
        }
        else
        {
            Reject(v => v["result"]!["deletionAuthorized"] = true);
            Reject(v => v["result"]!["reservesBudget"] = false);
            Reject(v => v["result"]!["storage"]!["diskMiB"] = 512);
            Reject(v => v["result"]!["sourceVersion"] = new string('c', 64));
        }
        var raw = Envelope(index).ToJsonString();
        Assert.Throws<RpcException>(() => DevelopmentRetirementGrpcService.Parse(request, raw.Insert(1, """{"status":"Succeeded"}"""[1..^1] + ",")));
        Assert.Throws<RpcException>(() => DevelopmentRetirementGrpcService.Parse(request, new string(' ', 16385)));
    }

    [Fact]
    public void WideInodeRoundtripsWithoutFloatingPointAndPlanFlagsRemainTyped()
    {
        var value = Envelope(); var plan = value["result"]!.AsObject();
        plan["archiveInode"] = 9007199254740993UL;
        using var document = JsonDocument.Parse(plan.ToJsonString());
        plan["retirementPlanSha256"] = Convert.ToHexStringLower(SHA256.HashData(DevelopmentRetirementPlanBinding.Canonical(document.RootElement, omitDigest: true)));
        var receipt = DevelopmentRetirementGrpcService.Parse(Request(), value.ToJsonString());
        using var parsed = JsonDocument.Parse(receipt.ResultJson.Memory);
        Assert.Equal(9007199254740993UL, parsed.RootElement.GetProperty("archiveInode").GetUInt64());
        plan["retainsSource"] = 1;
        Assert.Throws<RpcException>(() => DevelopmentRetirementGrpcService.Parse(Request(), value.ToJsonString()));
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("Running")]
    [InlineData("Finishing")]
    [InlineData("Stopped")]
    [InlineData("Interrupted")]
    [InlineData("Rejected")]
    public void NonSuccessNeverCarriesContentAndErrorsCannotExposePaths(string status)
    {
        var value = Envelope(); value["status"] = status; value["result"] = null;
        value["code"] = status == "Rejected" ? "WORKSPACE_STORAGE_RETIREMENT_ACCESS_DENIED" : null;
        var receipt = DevelopmentRetirementGrpcService.Parse(Request(), value.ToJsonString());
        Assert.Equal(status, receipt.Status); Assert.True(receipt.ResultJson.IsEmpty);
        foreach (var code in new[] { "private path /secret", "WORKSPACE_ARCHIVE_IO_UNCONFIRMED", "" })
        {
            value["code"] = code;
            Assert.Throws<RpcException>(() => DevelopmentRetirementGrpcService.Parse(Request(), value.ToJsonString()));
        }
    }

    [Fact]
    public void RequestsCannotReuseArchivePhasesOrCollapseDistinctTaskAndStorageIdentities()
    {
        foreach (var change in new Action<WorkspaceRetirementRequest>[]
        {
            r => r.Phase = "capture", r => r.Phase = "verify", r => r.Phase = "delete", r => r.Phase = "execute",
            r => r.ExpectedEpoch = 3, r => r.ExpectedEpoch = 0, r => r.ExpectedEpoch = ulong.MaxValue,
            r => r.TaskId = r.RetirementId, r => r.TaskId = r.ArchiveId, r => r.TaskId = r.Storage.WorkspaceId,
            r => r.RetirementId = r.ArchiveId, r => r.RetirementId = r.Storage.WorkspaceId, r => r.ArchiveId = r.Storage.WorkspaceId,
            r => r.RetirementId = Guid.Empty.ToString("D"), r => r.ArchivePlanSha256 = "", r => r.CaptureReceiptSha256 = "",
            r => r.RetirementPlanSha256 = new string('a', 64), r => r.Storage.ActorSubject = new string('a', 5000)
        })
        { var request = Request(); change(request); Assert.False(DevelopmentRetirementBinding.IsValid(request)); }
        var execute = Request(1); execute.RetirementPlanSha256 = new string('A', 64);
        Assert.False(DevelopmentRetirementBinding.IsValid(execute));
    }

    [Fact]
    public async Task UnixSocketChecksCapabilitiesExactBodySingleUseNonceAndAllControlMethods()
    {
        var directory = Directory.CreateTempSubdirectory("wd-retirement-rpc-");
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var key = RandomNumberGenerator.GetBytes(32);
            var keyFile = Path.Combine(directory.FullName, "key"); await File.WriteAllBytesAsync(keyFile, key, timeout.Token);
            var socket = Path.Combine(directory.FullName, "agent.sock");
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.Logging.ClearProviders(); builder.Configuration["Agent:CapabilityKeyFile"] = keyFile;
            builder.WebHost.ConfigureKestrel(o => o.ListenUnixSocket(socket, l => l.Protocols = HttpProtocols.Http2));
            builder.Services.AddGrpc(o => o.Interceptors.Add<CapabilityInterceptor>());
            builder.Services.AddSingleton<CapabilityValidator>(); builder.Services.AddSingleton<Capabilities>();
            builder.Services.AddSingleton<Receiver>();
            await using var server = builder.Build(); server.MapGrpcService<Capabilities>(); server.MapGrpcService<Receiver>();
            await server.StartAsync(timeout.Token);
            using var gateway = new AgentGateway(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                { ["Agent:CapabilityKeyFile"] = keyFile, ["Agent:SocketPath"] = socket }).Build());
            var actor = new TerminalActor("administrator", null, [], new string('a', 64), "trace", null);
            var original = Request();
            var workspace = new WorkspaceDefinition(Guid.Parse(original.Storage.WorkspaceId), "qa", "old-owner", "nvme-main",
                WorkspaceStorageAllocation.Allocated, 3, new(100, 128, 256), new(1, []), new string('a', 64), DateTimeOffset.UtcNow)
                { LifecycleState = "Stopped", LifecycleVersion = 2 };
            var task = new WorkspaceRetirementTask(Guid.Parse(original.TaskId), Guid.Parse(original.RetirementId), Guid.Parse(original.ArchiveId), "plan", 2, original.ArchivePlanSha256, original.CaptureReceiptSha256, "");
            var receiver = server.Services.GetRequiredService<Receiver>(); var caps = server.Services.GetRequiredService<Capabilities>();
            foreach (var (major, minor, enabled) in new[] { (1u, 77u, true), (1u, 78u, false), (2u, 78u, true) })
            {
                caps.Major = major; caps.Minor = minor; caps.Enabled = enabled;
                var rejected = await Assert.ThrowsAsync<WorkspaceRetirementException>(() => gateway.StartRetirementAsync(actor, workspace, task, timeout.Token));
                Assert.Equal(WorkspaceRetirementFailure.Unsupported, rejected.Failure); Assert.Equal(0, receiver.Calls);
                Assert.True(rejected.StartWasNotDispatched);
            }
            caps.Major = 1; caps.Enabled = true;
            caps.Fail = true;
            var preflight = await Assert.ThrowsAsync<WorkspaceRetirementException>(() => gateway.StartRetirementAsync(actor, workspace, task, timeout.Token));
            Assert.True(preflight.StartWasNotDispatched); Assert.Equal(0, receiver.Calls);
            caps.Fail = false;
            using (var missingKey = new AgentGateway(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                { ["Agent:SocketPath"] = socket }).Build()))
            {
                var rejected = await Assert.ThrowsAsync<WorkspaceRetirementException>(() => missingKey.StartRetirementAsync(actor, workspace, task, timeout.Token));
                Assert.True(rejected.StartWasNotDispatched); Assert.Equal(WorkspaceRetirementFailure.Unconfirmed, rejected.Failure);
                Assert.Equal(0, receiver.Calls);
            }
            var pending = await gateway.StartRetirementAsync(actor, workspace, task, timeout.Token); Assert.Equal("Pending", pending.Status);
            var request = receiver.LastRequest!; var consumed = receiver.LastToken!;
            Assert.Equal(actor.Subject, request.Storage.ActorSubject); Assert.NotEqual(workspace.OwnerSubject, request.Storage.ActorSubject);
            receiver.Status = "Running";
            Assert.Equal("Running", (await gateway.ObserveRetirementAsync(actor, workspace, task, timeout.Token)).Status);
            Assert.Equal(request, receiver.LastRequest); Assert.NotEqual(consumed, receiver.LastToken);
            receiver.Status = "Stopped";
            Assert.Equal("Stopped", (await gateway.CancelRetirementAsync(actor, workspace, task, timeout.Token)).Status);
            Assert.Equal(request, receiver.LastRequest);
            var count = receiver.Calls;
            await Assert.ThrowsAsync<ArgumentException>(() => gateway.StartRetirementAsync(actor, workspace with { LifecycleState = "Active" }, task, timeout.Token));
            await Assert.ThrowsAsync<ArgumentException>(() => gateway.StartRetirementAsync(actor, workspace, task with { ExpectedEpoch = 4 }, timeout.Token));
            Assert.Equal(count, receiver.Calls);
            using var channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions { HttpHandler = new SocketsHttpHandler
            {
                UseProxy = false, ConnectCallback = async (_, token) =>
                {
                    var connection = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                    await connection.ConnectAsync(new UnixDomainSocketEndPoint(socket), token); return new NetworkStream(connection, ownsSocket: true);
                }
            } });
            var client = new DevelopmentRetirementService.DevelopmentRetirementServiceClient(channel);
            Metadata Sign(string method, bool legacy = false, long seconds = 30, bool archiveDomain = false)
            {
                var expiry = DateTimeOffset.UtcNow.AddSeconds(seconds).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
                var nonce = Guid.NewGuid().ToString("N");
                var payload = legacy ? $"{expiry}.{nonce}.{method}" : archiveDomain
                    ? DevelopmentArchiveBinding.Payload(expiry, nonce, method, DevelopmentRetirementBinding.Hash(request))
                    : DevelopmentRetirementBinding.Payload(expiry, nonce, method, DevelopmentRetirementBinding.Hash(request));
                return new() { { "x-whaledeck-capability", $"{expiry}.{nonce}.{Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload)))}" } };
            }
            var denials = new List<(WorkspaceRetirementRequest, Metadata)>
            {
                (request, new()), (request, new() { { "x-whaledeck-capability", consumed } }),
                (request, Sign(DevelopmentRetirementBinding.Start, legacy: true)),
                (request, Sign(DevelopmentRetirementBinding.Start, archiveDomain: true)),
                (request, Sign(DevelopmentRetirementBinding.Observe)), (request, Sign(DevelopmentRetirementBinding.Cancel)),
                (request, Sign(DevelopmentRetirementBinding.Start, seconds: -10)), (request, Sign(DevelopmentRetirementBinding.Start, seconds: 120))
            };
            var duplicate = Sign(DevelopmentRetirementBinding.Start); duplicate.Add("x-whaledeck-capability", duplicate[0].Value);
            denials.Add((request, duplicate));
            foreach (var mutate in new Action<WorkspaceRetirementRequest>[]
            {
                r => r.TaskId = Guid.NewGuid().ToString("D"), r => r.ArchiveId = Guid.NewGuid().ToString("D"),
                r => r.RetirementId = Guid.NewGuid().ToString("D"), r => r.RetirementPlanSha256 = new string('c', 64),
                r => r.ExpectedEpoch = 4, r => r.Phase = "execute", r => r.ArchivePlanSha256 = new string('a', 64),
                r => r.CaptureReceiptSha256 = new string('b', 64), r => r.Storage.WorkspaceId = Guid.NewGuid().ToString("D"),
                r => r.Storage.EnvironmentSha256 = new string('b', 64), r => r.Storage.StorageRootId = "nvme-other",
                r => r.Storage.DiskMib++, r => r.Storage.ActorSubject = "other", r => r.Storage.TraceId = "other",
                r => r.Storage.ActorSubject = new string('a', 5000)
            }) { var changed = request.Clone(); mutate(changed); denials.Add((changed, Sign(DevelopmentRetirementBinding.Start))); }
            foreach (var (body, headers) in denials)
            {
                var denied = await Assert.ThrowsAsync<RpcException>(async () => await client.StartAsync(body, headers, cancellationToken: timeout.Token));
                Assert.Equal(StatusCode.PermissionDenied, denied.StatusCode);
            }
            Assert.Equal(count, receiver.Calls);
            receiver.Status = "Finishing";
            Assert.Equal("Finishing", (await gateway.ObserveRetirementAsync(actor, workspace, task, timeout.Token)).Status);
            receiver.Status = "Succeeded";
            var finished = await gateway.ObserveRetirementAsync(actor, workspace, task, timeout.Token);
            Assert.Equal("Succeeded", finished.Status); Assert.NotNull(finished.ResultJson);
            Assert.Equal(finished, await gateway.CancelRetirementAsync(actor, workspace, task, timeout.Token));
            // A fresh authorized observer signs its own identity while the
            // immutable host task/hash remains the same.
            Assert.Equal(finished, await gateway.ObserveRetirementAsync(actor with { Subject = "current-administrator" }, workspace, task, timeout.Token));
            receiver.FixtureIndex = 1;
            var execution = Request(1);
            var executionTask = task with { TaskId = Guid.Parse(execution.TaskId), Phase = "execute", RetirementPlanSha256 = execution.RetirementPlanSha256 };
            var executed = await gateway.StartRetirementAsync(actor, workspace, executionTask, timeout.Token);
            Assert.Equal("Succeeded", executed.Status);
            using (var result = JsonDocument.Parse(executed.ResultJson!))
                Assert.False(result.RootElement.GetProperty("releasesApplicationBudget").GetBoolean());
            Assert.Equal(executed, await gateway.ObserveRetirementAsync(actor, workspace, executionTask, timeout.Token));
            Assert.Equal(executed, await gateway.CancelRetirementAsync(actor, workspace, executionTask, timeout.Token));
            receiver.FixtureIndex = 0;
            receiver.Corrupt = true;
            var corrupt = await Assert.ThrowsAsync<WorkspaceRetirementException>(() => gateway.ObserveRetirementAsync(actor, workspace, task, timeout.Token));
            Assert.Equal(WorkspaceRetirementFailure.Integrity, corrupt.Failure);
            receiver.Corrupt = false; receiver.Fail = true; count = receiver.Calls;
            var lost = await Assert.ThrowsAsync<WorkspaceRetirementException>(() => gateway.StartRetirementAsync(actor, workspace, task, timeout.Token));
            Assert.Equal(WorkspaceRetirementFailure.Retryable, lost.Failure); Assert.Equal(count + 1, receiver.Calls);
            Assert.False(lost.StartWasNotDispatched);
            Assert.Equal(request, receiver.LastRequest); // No automatic retry/new task.
            Assert.False(server.Services.GetRequiredService<CapabilityValidator>().Validate(consumed, DevelopmentRetirementBinding.Start));
        }
        finally { directory.Delete(recursive: true); }
    }

    public sealed class Capabilities : AgentService.AgentServiceBase
    {
        public uint Major { get; set; } = 1; public uint Minor { get; set; } = 78; public bool Enabled { get; set; } = true;
        public bool Fail { get; set; }
        public override Task<CapabilitiesResponse> GetCapabilities(Google.Protobuf.WellKnownTypes.Empty request, ServerCallContext context)
        {
            if (Fail) throw new RpcException(new Status(StatusCode.Unavailable, "WORKSPACE_RETIREMENT_TEST_PREFLIGHT_FAILED"));
            var reply = new CapabilitiesResponse { ProtocolMajor = Major, ProtocolMinor = Minor };
            if (Enabled) reply.Capabilities.Add(DevelopmentRetirementBinding.Capability); return Task.FromResult(reply);
        }
    }
    // Explicit helper double; actual signature interceptor and Unix transport.
    public sealed class Receiver : DevelopmentRetirementService.DevelopmentRetirementServiceBase
    {
        public int Calls { get; private set; } public string Status { get; set; } = "Pending";
        public int FixtureIndex { get; set; }
        public bool Corrupt { get; set; } public bool Fail { get; set; }
        public WorkspaceRetirementRequest? LastRequest { get; private set; } public string? LastToken { get; private set; }
        public override Task<WorkspaceRetirementReceipt> Start(WorkspaceRetirementRequest request, ServerCallContext context) => Run(request, context);
        public override Task<WorkspaceRetirementReceipt> Observe(WorkspaceRetirementRequest request, ServerCallContext context) => Run(request, context);
        public override Task<WorkspaceRetirementReceipt> Cancel(WorkspaceRetirementRequest request, ServerCallContext context) => Run(request, context);
        private Task<WorkspaceRetirementReceipt> Run(WorkspaceRetirementRequest request, ServerCallContext context)
        {
            Calls++; LastRequest = request; LastToken = context.RequestHeaders.GetValue("x-whaledeck-capability");
            if (Fail) throw new RpcException(new Status(StatusCode.Unavailable, "WORKSPACE_RETIREMENT_TEST_LOST_REPLY"));
            var value = Envelope(FixtureIndex); value["status"] = Status; if (Status != "Succeeded") value["result"] = null;
            var reply = DevelopmentRetirementBinding.Parse(request, value.ToJsonString());
            if (Corrupt) reply.RequestSha256 = new string('0', 64); return Task.FromResult(reply);
        }
    }
}
