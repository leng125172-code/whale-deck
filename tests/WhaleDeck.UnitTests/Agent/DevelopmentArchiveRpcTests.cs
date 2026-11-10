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

public sealed class DevelopmentArchiveRpcTests
{
    // Synthetic Python json.dumps(sort_keys=True, separators=(',', ':'))
    // golden vectors. They test wire agreement, not real storage acceptance.
    private static JsonObject Fixture(int index = 0) => JsonNode.Parse(File.ReadAllText(Path.Combine(
        AppContext.BaseDirectory, "Agent/Fixtures/development-archive-protocol.json")))![index]!.AsObject();
    private static WorkspaceArchiveRequest Request(int index = 0)
    {
        var value = Fixture(index)["request"]!;
        return new()
        {
            Storage = new() { WorkspaceId = value["storage"]!["workspaceId"]!.GetValue<string>(),
                StorageRootId = "nvme-main", EnvironmentSha256 = new string('a', 64), DiskMib = 256, ActorSubject = "owner", TraceId = "trace" },
            TaskId = value["taskId"]!.GetValue<string>(), ArchiveId = value["archiveId"]!.GetValue<string>(),
            Phase = value["phase"]!.GetValue<string>(), ExpectedEpoch = 2,
            PlanSha256 = value["planSha256"]!.GetValue<string>(), ReceiptSha256 = value["receiptSha256"]!.GetValue<string>()
        };
    }
    private static JsonObject Envelope(int index = 0) => Fixture(index)["receipt"]!.AsObject();

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void PythonCanonicalPlanCaptureAndIndependentVerifyAgree(int index)
    {
        var request = Request(index);
        Assert.True(DevelopmentArchiveBinding.IsValid(request));
        Assert.Equal(Fixture(index)["canonicalRequest"]!.GetValue<string>(), DevelopmentArchiveBinding.HostInput(request));
        var receipt = DevelopmentArchiveGrpcService.Parse(request, Envelope(index).ToJsonString());
        Assert.Equal("Succeeded", receipt.Status);
        Assert.True(DevelopmentArchiveBinding.Matches(request, receipt));
        var changed = request.Clone(); changed.ArchiveId = Guid.NewGuid().ToString("D");
        Assert.False(DevelopmentArchiveBinding.Matches(changed, receipt));
        var invalid = receipt.Clone(); invalid.ResultJson = ByteString.CopyFromUtf8(" " + receipt.ResultJson.ToStringUtf8());
        Assert.False(DevelopmentArchiveBinding.Matches(request, invalid));
    }

    [Fact]
    public void MalformedReceiptsCannotMasqueradeAsSuccessOrHideMutatedFacts()
    {
        var request = Request(1);
        void Reject(Action<JsonObject> mutate)
        {
            var value = Envelope(1); mutate(value);
            var error = Assert.Throws<RpcException>(() => DevelopmentArchiveGrpcService.Parse(request, value.ToJsonString()));
            Assert.Equal(StatusCode.DataLoss, error.StatusCode);
        }
        Reject(v => v["taskId"] = Guid.NewGuid().ToString("D"));
        Reject(v => v["requestSha256"] = new string('0', 64));
        Reject(v => v["schemaVersion"] = true);
        Reject(v => v["code"] = "");
        Reject(v => v["result"] = null);
        Reject(v => v["status"] = "Stopped");
        Reject(v => v["extra"] = 1);
        foreach (var name in new[] { "schemaVersion", "storage", "expectedEpoch", "storageProofSha256", "sourceVersion",
            "sourceManifestSha256", "entryCount", "totalBytes", "archiveFilesystemUuid", "hddProofSha256", "planSha256",
            "operationId", "archiveFormat", "archiveBytes", "archiveSha256", "archiveIndexSha256", "archiveInode", "receiptSha256" })
            Reject(v => v["result"]![name] = null);
        Reject(v => v["result"]!["storage"]!["diskMiB"] = 512);
        Reject(v => v["result"]!["sourceVersion"] = new string('c', 64));
        Reject(v => v["result"]!["archiveBytes"] = 17UL * 1024 * 1024 * 1024 + 1);
        Reject(v => v["result"]!["entryCount"] = 10001);
        Reject(v => v["result"]!["totalBytes"] = 16UL * 1024 * 1024 * 1024 + 1);
        Reject(v => v["result"]!["archiveInode"] = 0);
        Reject(v => v["result"]!["entryCount"] = true);
        Reject(v => v["result"]!["archiveSha256"] = new string('0', 64));
        var raw = Envelope(1).ToJsonString();
        Assert.Throws<RpcException>(() => DevelopmentArchiveGrpcService.Parse(request, raw.Insert(1, "\"status\":\"Succeeded\",")));
        Assert.Throws<RpcException>(() => DevelopmentArchiveGrpcService.Parse(request, new string(' ', 16385)));
        Assert.Throws<RpcException>(() => DevelopmentArchiveGrpcService.Parse(request, raw.Replace("\"entryCount\":2", "\"entryCount\":2.0", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("Running")]
    [InlineData("Finishing")]
    [InlineData("Stopped")]
    [InlineData("Interrupted")]
    [InlineData("Rejected")]
    public void StateReceiptsHaveNoContentAndRejectedCodeIsBounded(string state)
    {
        var value = Envelope(); value["status"] = state; value["result"] = null;
        value["code"] = state == "Rejected" ? "WORKSPACE_STORAGE_ARCHIVE_TASK_INTERRUPTED" : null;
        var receipt = DevelopmentArchiveGrpcService.Parse(Request(), value.ToJsonString());
        Assert.Equal(state, receipt.Status); Assert.True(receipt.ResultJson.IsEmpty);
        value["code"] = "secret or arbitrary helper stderr";
        Assert.Throws<RpcException>(() => DevelopmentArchiveGrpcService.Parse(Request(), value.ToJsonString()));
    }

    [Fact]
    public void RequestsRejectUnsupportedPhaseEpochAndIncompleteDigestChain()
    {
        foreach (var mutate in new Action<WorkspaceArchiveRequest>[]
        {
            r => r.Phase = "delete", r => r.ExpectedEpoch = 1, r => r.ExpectedEpoch = ulong.MaxValue,
            r => r.TaskId = Guid.Empty.ToString("D"), r => r.ArchiveId = "other", r => r.Storage.DiskMib = 255,
            r => r.Phase = "capture", r => r.Phase = "verify", r => r.PlanSha256 = new string('a', 64),
            r => r.ReceiptSha256 = new string('b', 64), r => r.Storage.ActorSubject = new string('a', 5000)
        })
        {
            var request = Request(); mutate(request); Assert.False(DevelopmentArchiveBinding.IsValid(request));
        }
        var capture = Request(1); capture.ReceiptSha256 = new string('b', 64);
        Assert.False(DevelopmentArchiveBinding.IsValid(capture));
        var verify = Request(2); verify.ReceiptSha256 = new string('B', 64);
        Assert.False(DevelopmentArchiveBinding.IsValid(verify));
    }

    [Fact]
    public async Task UnixSocketChecksCapabilitiesExactBodySingleUseNonceAndAllControlMethods()
    {
        var directory = Directory.CreateTempSubdirectory("wd-archive-rpc-");
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
            var task = new WorkspaceArchiveTask(Guid.Parse(original.TaskId), Guid.Parse(original.ArchiveId), "plan", 2, "", "");
            var receiver = server.Services.GetRequiredService<Receiver>(); var caps = server.Services.GetRequiredService<Capabilities>();
            foreach (var (major, minor, enabled) in new[] { (1u, 76u, true), (1u, 77u, false), (2u, 77u, true) })
            {
                caps.Major = major; caps.Minor = minor; caps.Enabled = enabled;
                var rejected = await Assert.ThrowsAsync<WorkspaceArchiveException>(() => gateway.StartArchiveAsync(actor, workspace, task, timeout.Token));
                Assert.Equal(WorkspaceArchiveFailure.Unsupported, rejected.Failure); Assert.Equal(0, receiver.Calls);
            }
            caps.Major = 1; caps.Enabled = true;
            var pending = await gateway.StartArchiveAsync(actor, workspace, task, timeout.Token); Assert.Equal("Pending", pending.Status);
            var request = receiver.LastRequest!; var consumed = receiver.LastToken!;
            Assert.Equal(actor.Subject, request.Storage.ActorSubject); Assert.NotEqual(workspace.OwnerSubject, request.Storage.ActorSubject);
            receiver.Status = "Running";
            Assert.Equal("Running", (await gateway.ObserveArchiveAsync(actor, workspace, task, timeout.Token)).Status);
            Assert.Equal(request, receiver.LastRequest); Assert.NotEqual(consumed, receiver.LastToken);
            receiver.Status = "Stopped";
            Assert.Equal("Stopped", (await gateway.CancelArchiveAsync(actor, workspace, task, timeout.Token)).Status);
            Assert.Equal(request, receiver.LastRequest);
            var count = receiver.Calls;
            await Assert.ThrowsAsync<ArgumentException>(() => gateway.StartArchiveAsync(actor, workspace with { LifecycleState = "Active" }, task, timeout.Token));
            await Assert.ThrowsAsync<ArgumentException>(() => gateway.StartArchiveAsync(actor, workspace, task with { ExpectedEpoch = 4 }, timeout.Token));
            Assert.Equal(count, receiver.Calls);
            using var channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions { HttpHandler = new SocketsHttpHandler
            {
                UseProxy = false, ConnectCallback = async (_, token) =>
                {
                    var connection = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                    await connection.ConnectAsync(new UnixDomainSocketEndPoint(socket), token); return new NetworkStream(connection, ownsSocket: true);
                }
            } });
            var client = new DevelopmentArchiveService.DevelopmentArchiveServiceClient(channel);
            Metadata Sign(string method, bool legacy = false, long seconds = 30)
            {
                var expiry = DateTimeOffset.UtcNow.AddSeconds(seconds).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
                var nonce = Guid.NewGuid().ToString("N");
                var payload = legacy ? $"{expiry}.{nonce}.{method}" : DevelopmentArchiveBinding.Payload(expiry, nonce, method, DevelopmentArchiveBinding.Hash(request));
                return new() { { "x-whaledeck-capability", $"{expiry}.{nonce}.{Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload)))}" } };
            }
            var denials = new List<(WorkspaceArchiveRequest, Metadata)>
            {
                (request, new()), (request, new() { { "x-whaledeck-capability", consumed } }),
                (request, Sign(DevelopmentArchiveBinding.Start, legacy: true)),
                (request, Sign(DevelopmentArchiveBinding.Observe)), (request, Sign(DevelopmentArchiveBinding.Cancel)),
                (request, Sign(DevelopmentArchiveBinding.Start, seconds: -10)), (request, Sign(DevelopmentArchiveBinding.Start, seconds: 120))
            };
            var duplicate = Sign(DevelopmentArchiveBinding.Start); duplicate.Add("x-whaledeck-capability", duplicate[0].Value);
            denials.Add((request, duplicate));
            foreach (var mutate in new Action<WorkspaceArchiveRequest>[]
            {
                r => r.TaskId = Guid.NewGuid().ToString("D"), r => r.ArchiveId = Guid.NewGuid().ToString("D"),
                r => r.ExpectedEpoch = 4, r => r.Phase = "capture", r => r.PlanSha256 = new string('a', 64),
                r => r.ReceiptSha256 = new string('b', 64), r => r.Storage.WorkspaceId = Guid.NewGuid().ToString("D"),
                r => r.Storage.EnvironmentSha256 = new string('b', 64), r => r.Storage.StorageRootId = "nvme-other",
                r => r.Storage.DiskMib++, r => r.Storage.ActorSubject = "other", r => r.Storage.TraceId = "other",
                r => r.Storage.ActorSubject = new string('a', 5000)
            }) { var changed = request.Clone(); mutate(changed); denials.Add((changed, Sign(DevelopmentArchiveBinding.Start))); }
            foreach (var (body, headers) in denials)
            {
                var denied = await Assert.ThrowsAsync<RpcException>(async () => await client.StartAsync(body, headers, cancellationToken: timeout.Token));
                Assert.Equal(StatusCode.PermissionDenied, denied.StatusCode);
            }
            Assert.Equal(count, receiver.Calls);
            receiver.Status = "Finishing";
            Assert.Equal("Finishing", (await gateway.ObserveArchiveAsync(actor, workspace, task, timeout.Token)).Status);
            receiver.Status = "Succeeded";
            var finished = await gateway.ObserveArchiveAsync(actor, workspace, task, timeout.Token);
            Assert.Equal("Succeeded", finished.Status); Assert.NotNull(finished.ResultJson);
            Assert.Equal(finished, await gateway.CancelArchiveAsync(actor, workspace, task, timeout.Token));
            receiver.Corrupt = true;
            var corrupt = await Assert.ThrowsAsync<WorkspaceArchiveException>(() => gateway.ObserveArchiveAsync(actor, workspace, task, timeout.Token));
            Assert.Equal(WorkspaceArchiveFailure.Integrity, corrupt.Failure);
            receiver.Corrupt = false; receiver.Fail = true; count = receiver.Calls;
            var lost = await Assert.ThrowsAsync<WorkspaceArchiveException>(() => gateway.StartArchiveAsync(actor, workspace, task, timeout.Token));
            Assert.Equal(WorkspaceArchiveFailure.Retryable, lost.Failure); Assert.Equal(count + 1, receiver.Calls);
            Assert.Equal(request, receiver.LastRequest); // No automatic retry/new task.
            Assert.False(server.Services.GetRequiredService<CapabilityValidator>().Validate(consumed, DevelopmentArchiveBinding.Start));
        }
        finally { directory.Delete(recursive: true); }
    }

    public sealed class Capabilities : AgentService.AgentServiceBase
    {
        public uint Major { get; set; } = 1; public uint Minor { get; set; } = 77; public bool Enabled { get; set; } = true;
        public override Task<CapabilitiesResponse> GetCapabilities(Google.Protobuf.WellKnownTypes.Empty request, ServerCallContext context)
        {
            var reply = new CapabilitiesResponse { ProtocolMajor = Major, ProtocolMinor = Minor };
            if (Enabled) reply.Capabilities.Add(DevelopmentArchiveBinding.Capability); return Task.FromResult(reply);
        }
    }
    // Explicit helper double; actual signature interceptor and Unix transport.
    public sealed class Receiver : DevelopmentArchiveService.DevelopmentArchiveServiceBase
    {
        public int Calls { get; private set; } public string Status { get; set; } = "Pending";
        public bool Corrupt { get; set; } public bool Fail { get; set; }
        public WorkspaceArchiveRequest? LastRequest { get; private set; } public string? LastToken { get; private set; }
        public override Task<WorkspaceArchiveReceipt> Start(WorkspaceArchiveRequest request, ServerCallContext context) => Run(request, context);
        public override Task<WorkspaceArchiveReceipt> Observe(WorkspaceArchiveRequest request, ServerCallContext context) => Run(request, context);
        public override Task<WorkspaceArchiveReceipt> Cancel(WorkspaceArchiveRequest request, ServerCallContext context) => Run(request, context);
        private Task<WorkspaceArchiveReceipt> Run(WorkspaceArchiveRequest request, ServerCallContext context)
        {
            Calls++; LastRequest = request; LastToken = context.RequestHeaders.GetValue("x-whaledeck-capability");
            if (Fail) throw new RpcException(new Status(StatusCode.Unavailable, "WORKSPACE_ARCHIVE_TEST_LOST_REPLY"));
            var value = Envelope(); value["status"] = Status; if (Status != "Succeeded") value["result"] = null;
            var reply = DevelopmentArchiveBinding.Parse(request, value.ToJsonString());
            if (Corrupt) reply.RequestSha256 = new string('0', 64); return Task.FromResult(reply);
        }
    }
}
