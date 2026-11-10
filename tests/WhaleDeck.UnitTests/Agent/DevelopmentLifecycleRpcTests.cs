using System.Globalization;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
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

public sealed class DevelopmentLifecycleRpcTests
{
    [Fact]
    public async Task UnixSocketRequiresCapabilityAndExactSignedBodyAndBindsAdministratorToOriginalWorkspace()
    {
        var directory = Directory.CreateTempSubdirectory("wd-life-rpc-");
        try
        {
            var key = RandomNumberGenerator.GetBytes(32);
            var keyFile = Path.Combine(directory.FullName, "key");
            await File.WriteAllBytesAsync(keyFile, key);
            var socket = Path.Combine(directory.FullName, "agent.sock");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.Logging.ClearProviders();
            builder.Configuration["Agent:CapabilityKeyFile"] = keyFile;
            builder.WebHost.ConfigureKestrel(options => options.ListenUnixSocket(socket, listen => listen.Protocols = HttpProtocols.Http2));
            builder.Services.AddGrpc(options => options.Interceptors.Add<CapabilityInterceptor>());
            builder.Services.AddSingleton<CapabilityValidator>();
            builder.Services.AddSingleton<Capabilities>();
            builder.Services.AddSingleton<Receiver>();
            await using var server = builder.Build();
            server.MapGrpcService<Capabilities>();
            server.MapGrpcService<Receiver>();
            await server.StartAsync(timeout.Token);
            using var gateway = new AgentGateway(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Agent:CapabilityKeyFile"] = keyFile, ["Agent:SocketPath"] = socket }).Build());
            var actor = new TerminalActor("administrator", null, [], new string('a', 64), "trace", null);
            var workspace = new WorkspaceDefinition(Guid.NewGuid(), "orphan", "deleted-original-owner", "nvme-main",
                WorkspaceStorageAllocation.Allocated, 3, new(100, 128, 256), new(1, []), new string('b', 64), DateTimeOffset.UtcNow);
            var receiver = server.Services.GetRequiredService<Receiver>();
            var caps = server.Services.GetRequiredService<Capabilities>();
            foreach (var (minor, enabled) in new[] { (75u, true), (76u, false) })
            {
                caps.Minor = minor; caps.Enabled = enabled;
                var unsupported = await Assert.ThrowsAsync<WorkspaceLifecycleException>(() => gateway.PlanAsync(actor, workspace, "stop", timeout.Token));
                Assert.Equal(WorkspaceLifecycleFailure.Unsupported, unsupported.Failure);
                Assert.Equal(0, receiver.Calls);
            }
            caps.Enabled = true;
            var proof = await gateway.PlanAsync(actor, workspace, "stop", timeout.Token);
            Assert.Equal(workspace.Id, proof.WorkspaceId);
            var request = Assert.IsType<WorkspaceLifecycleRequest>(receiver.LastRequest);
            Assert.Equal(actor.Subject, request.Storage.ActorSubject);
            Assert.NotEqual(workspace.OwnerSubject, request.Storage.ActorSubject);
            Assert.Equal(1, receiver.Calls);

            using var channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions
            {
                HttpHandler = new SocketsHttpHandler { UseProxy = false, ConnectCallback = async (_, token) =>
                {
                    var connection = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                    await connection.ConnectAsync(new UnixDomainSocketEndPoint(socket), token);
                    return new NetworkStream(connection, ownsSocket: true);
                } }
            });
            var client = new DevelopmentLifecycleService.DevelopmentLifecycleServiceClient(channel);
            var expiry = DateTimeOffset.UtcNow.AddSeconds(30).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            Metadata Sign(string method)
            {
                var nonce = Guid.NewGuid().ToString("N");
                var signature = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(
                    DevelopmentLifecycleBinding.Payload(expiry, nonce, method, DevelopmentLifecycleBinding.Hash(request))));
                return new() { { "x-whaledeck-capability", $"{expiry}.{nonce}.{Convert.ToHexString(signature)}" } };
            }
            var changed = request.Clone(); changed.Storage.DiskMib++;
            foreach (var (body, headers) in new[]
            {
                (request, new Metadata()), (request, new Metadata { { "x-whaledeck-capability", receiver.LastToken! } }),
                (changed, Sign(DevelopmentLifecycleBinding.Plan)), (request, Sign(DevelopmentLifecycleBinding.Apply))
            })
            {
                var denied = await Assert.ThrowsAsync<RpcException>(async () => await client.PlanAsync(body, headers, cancellationToken: timeout.Token));
                Assert.Equal(StatusCode.PermissionDenied, denied.StatusCode);
            }
            Assert.Equal(1, receiver.Calls);
            var plan = WorkspaceLifecycle.BuildPlan(workspace, proof);
            var stopped = await gateway.ApplyAsync(actor, workspace, plan, Guid.NewGuid(), timeout.Token);
            Assert.Equal("Stopped", stopped.State);
            Assert.Equal(2, stopped.Epoch);
            Assert.Equal(plan.HostPlanSha256, stopped.PlanSha256);
            receiver.Corrupt = true;
            var corrupt = await Assert.ThrowsAsync<WorkspaceLifecycleException>(() => gateway.PlanAsync(actor, workspace, "stop", timeout.Token));
            Assert.Equal(WorkspaceLifecycleFailure.Integrity, corrupt.Failure);
        }
        finally { directory.Delete(recursive: true); }
    }

    [Theory]
    [InlineData("stop", 1, "Active", "Stopped")]
    [InlineData("resume", 2, "Stopped", "Active")]
    public void ReceiptsRejectWrongEpochOrStateAndPlanBindsWorkspace(string action, long epoch, string before, string after)
    {
        var request = new WorkspaceLifecycleRequest
        {
            Storage = new() { WorkspaceId = Guid.NewGuid().ToString("D"), StorageRootId = "nvme-main",
                EnvironmentSha256 = new string('a', 64), DiskMib = 256, ActorSubject = "owner", TraceId = "trace" },
            Action = action, ExpectedEpoch = (ulong)epoch
        };
        var receipt = Reply(request, false);
        Assert.Equal(before, receipt.State);
        Assert.True(DevelopmentLifecycleBinding.Matches(request, receipt, false));
        receipt.Epoch++;
        Assert.False(DevelopmentLifecycleBinding.Matches(request, receipt, false));
        request.OperationId = Guid.NewGuid().ToString("D"); request.PlanSha256 = new string('c', 64); request.StorageProofSha256 = new string('d', 64);
        var applied = Reply(request, true);
        Assert.Equal(after, applied.State);
        Assert.True(DevelopmentLifecycleBinding.Matches(request, applied, true));
        applied.StorageProofSha256 = new string('e', 64);
        Assert.False(DevelopmentLifecycleBinding.Matches(request, applied, true));
    }

    private static WorkspaceLifecycleReceipt Reply(WorkspaceLifecycleRequest request, bool apply) => new()
    {
        WorkspaceId = request.Storage.WorkspaceId, Action = request.Action,
        Epoch = request.ExpectedEpoch + (apply ? 1u : 0u), State = ((request.Action == "stop") == apply ? "Stopped" : "Active"),
        PlanSha256 = apply ? request.PlanSha256 : new string('c', 64),
        StorageProofSha256 = apply ? request.StorageProofSha256 : new string('d', 64),
        RequestSha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(DevelopmentLifecycleBinding.HostInput(request))))
    };
    public sealed class Capabilities : AgentService.AgentServiceBase
    {
        public uint Minor { get; set; } = 76;
        public bool Enabled { get; set; } = true;
        public override Task<CapabilitiesResponse> GetCapabilities(Google.Protobuf.WellKnownTypes.Empty request, ServerCallContext context)
        {
            var value = new CapabilitiesResponse { ProtocolMajor = 1, ProtocolMinor = Minor };
            if (Enabled) value.Capabilities.Add(DevelopmentLifecycleBinding.Capability);
            return Task.FromResult(value);
        }
    }
    public sealed class Receiver : DevelopmentLifecycleService.DevelopmentLifecycleServiceBase
    {
        public int Calls { get; private set; }
        public bool Corrupt { get; set; }
        public WorkspaceLifecycleRequest? LastRequest { get; private set; }
        public string? LastToken { get; private set; }
        public override Task<WorkspaceLifecycleReceipt> Plan(WorkspaceLifecycleRequest request, ServerCallContext context) => Run(request, context, false);
        public override Task<WorkspaceLifecycleReceipt> Apply(WorkspaceLifecycleRequest request, ServerCallContext context) => Run(request, context, true);
        private Task<WorkspaceLifecycleReceipt> Run(WorkspaceLifecycleRequest request, ServerCallContext context, bool apply)
        {
            Calls++; LastRequest = request; LastToken = context.RequestHeaders.GetValue("x-whaledeck-capability");
            var reply = Reply(request, apply);
            if (Corrupt) reply.RequestSha256 = new string('f', 64);
            return Task.FromResult(reply);
        }
    }
}
