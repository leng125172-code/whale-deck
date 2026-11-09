using System.Globalization;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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

public sealed class DevelopmentRunnerRpcTests
{
    private static readonly TerminalActor Actor = new("workspace-owner", null, [], new string('a', 64), "trace", null);
    private static readonly WorkspaceEnvironment Environment = new(1,
        [new("node", "22", "registry.example/node@sha256:" + new string('b', 64))]);
    private static readonly string EnvironmentJson = DevelopmentWorkspaces.EncodeEnvironment(Environment);

    [Fact]
    public async Task RunnerUnixSocketBindsMethodBodyAndReplayAndChecksGatewayBudget()
    {
        var directory = Directory.CreateTempSubdirectory("wd-run-rpc-");
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var key = RandomNumberGenerator.GetBytes(32);
            var keyFile = Path.Combine(directory.FullName, "key");
            var socket = Path.Combine(directory.FullName, "agent.sock");
            await File.WriteAllBytesAsync(keyFile, key, timeout.Token);
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.Logging.ClearProviders();
            builder.Configuration["Agent:CapabilityKeyFile"] = keyFile;
            builder.WebHost.ConfigureKestrel(options => options.ListenUnixSocket(socket, listen => listen.Protocols = HttpProtocols.Http2));
            builder.Services.AddGrpc(options => options.Interceptors.Add<CapabilityInterceptor>());
            builder.Services.AddSingleton<CapabilityValidator>();
            builder.Services.AddSingleton<RunnerCapabilities>();
            builder.Services.AddSingleton<Receiver>();
            await using var server = builder.Build();
            server.MapGrpcService<RunnerCapabilities>();
            server.MapGrpcService<Receiver>();
            await server.StartAsync(timeout.Token);
            using var gateway = new AgentGateway(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Agent:CapabilityKeyFile"] = keyFile, ["Agent:SocketPath"] = socket
            }).Build());
            var workspace = new WorkspaceDefinition(Guid.NewGuid(), "example", Actor.Subject, "nvme", WorkspaceStorageAllocation.Allocated,
                3, new(400, 512, 4096), Environment, DevelopmentRunnerBinding.Digest(EnvironmentJson), DateTimeOffset.UtcNow);
            var command = new WorkspaceRunCommand(Guid.NewGuid(), "node", ["/usr/local/bin/node", "--version"], 200, 256, 10);
            var started = await gateway.StartAsync(Actor, workspace, command, timeout.Token);
            var receiver = server.Services.GetRequiredService<Receiver>();
            var request = Assert.IsType<WorkspaceRunRequest>(receiver.LastRequest);
            var firstToken = receiver.LastToken;
            Assert.Equal(command.RunId, started.RunId);
            Assert.Equal(Actor.Subject, request.Storage.ActorSubject);
            Assert.Equal(command.Arguments, request.Argv.ToArray());
            Assert.Equal(started, await gateway.ObserveAsync(Actor, workspace, command, timeout.Token));
            await Assert.ThrowsAsync<ArgumentException>(() => gateway.StartAsync(Actor, workspace, command with { CpuMillis = 401 }, timeout.Token));
            await Assert.ThrowsAsync<ArgumentException>(() => gateway.ObserveAsync(Actor with { Subject = "other" }, workspace, command, timeout.Token));
            Assert.Equal(2, receiver.Calls);

            using var channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions
            {
                HttpHandler = new SocketsHttpHandler
                {
                    UseProxy = false,
                    ConnectCallback = async (_, token) =>
                    {
                        var connection = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                        await connection.ConnectAsync(new UnixDomainSocketEndPoint(socket), token);
                        return new NetworkStream(connection, ownsSocket: true);
                    }
                }
            });
            var client = new DevelopmentRunnerService.DevelopmentRunnerServiceClient(channel);
            var expiry = DateTimeOffset.UtcNow.AddSeconds(30).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            Metadata Sign(string method, bool legacy = false)
            {
                var nonce = Guid.NewGuid().ToString("N");
                var payload = legacy ? $"{expiry}.{nonce}.{method}" :
                    DevelopmentRunnerBinding.Payload(expiry, nonce, method, DevelopmentRunnerBinding.Hash(request));
                return Headers($"{expiry}.{nonce}.{Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload)))}");
            }
            var changed = request.Clone();
            changed.Argv[1] = "--help";
            foreach (var (body, metadata) in new[]
            {
                (request, Headers(firstToken)), (request, Sign(DevelopmentRunnerBinding.Start, legacy: true)),
                (request, Sign(DevelopmentRunnerBinding.Observe)), (changed, Sign(DevelopmentRunnerBinding.Start)),
                (request, Sign(DevelopmentRunnerBinding.Cancel)), (request, Sign(DevelopmentRunnerBinding.Reclaim)),
                (request, new Metadata())
            })
            {
                var denied = await Assert.ThrowsAsync<RpcException>(async () =>
                    await client.StartAsync(body, metadata, cancellationToken: timeout.Token));
                Assert.Equal(StatusCode.PermissionDenied, denied.StatusCode);
            }
            Assert.Equal(2, receiver.Calls);
            var forbidden = await Assert.ThrowsAsync<RpcException>(async () =>
                await client.CancelAsync(request, Sign(DevelopmentRunnerBinding.Reclaim), cancellationToken: timeout.Token));
            Assert.Equal(StatusCode.PermissionDenied, forbidden.StatusCode);
            Assert.Equal(started, await gateway.CancelAsync(Actor, workspace, command, timeout.Token));
            Assert.Equal(started, await gateway.ReclaimAsync(Actor, workspace, command, timeout.Token));
            Assert.Equal(4, receiver.Calls);
            receiver.ChangeInputHash = true;
            var invalid = await Assert.ThrowsAsync<RpcException>(() => gateway.ObserveAsync(Actor, workspace, command, timeout.Token));
            Assert.Equal(StatusCode.DataLoss, invalid.StatusCode);

            var artifactCommand = command with { Arguments = ["/usr/local/bin/node", "--test", "--test-reporter=junit",
                "--test-reporter-destination=/tmp/whaledeck-results/tests.xml"] };
            var artifactRun = request.Clone();
            artifactRun.Argv.Clear();
            artifactRun.Argv.Add(artifactCommand.Arguments);
            var report = new WorkspaceRunnerArtifact(WorkspaceTestFormat.JUnit, "Present", Receiver.Artifact.Length,
                Convert.ToHexStringLower(SHA256.HashData(Receiver.Artifact)));
            var binding = new WorkspaceRunnerReceipt(command.RunId, new string('a', 64), "Succeeded", 0, report);
            var artifact = new WorkspaceTestArtifactRequest
            {
                Run = artifactRun, RequestSha256 = binding.RequestSha256, ArtifactSha256 = report.Sha256, Size = (uint)report.Size
            };
            var artifactNonce = Guid.NewGuid().ToString("N");
            var artifactPayload = DevelopmentRunnerBinding.Payload(expiry, artifactNonce, DevelopmentRunnerBinding.ReadTestArtifact,
                DevelopmentRunnerBinding.Hash(artifact));
            var artifactHeaders = Headers($"{expiry}.{artifactNonce}.{Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(artifactPayload)))}");
            var swapped = await Assert.ThrowsAsync<RpcException>(async () =>
                await client.RetireTestArtifactAsync(artifact, artifactHeaders, cancellationToken: timeout.Token));
            Assert.Equal(StatusCode.PermissionDenied, swapped.StatusCode);
            var modified = artifact.Clone();
            modified.Size++;
            foreach (var (body, headers) in new[] { (modified, artifactHeaders), (artifact, new Metadata()) })
            {
                using var deniedStream = client.ReadTestArtifact(body, headers, cancellationToken: timeout.Token);
                Assert.Equal(StatusCode.PermissionDenied,
                    (await Assert.ThrowsAsync<RpcException>(() => deniedStream.ResponseStream.MoveNext(timeout.Token))).StatusCode);
            }
            using (var allowed = client.ReadTestArtifact(artifact, artifactHeaders, cancellationToken: timeout.Token))
                Assert.True(await allowed.ResponseStream.MoveNext(timeout.Token));
            using (var replayed = client.ReadTestArtifact(artifact, artifactHeaders, cancellationToken: timeout.Token))
                Assert.Equal(StatusCode.PermissionDenied,
                    (await Assert.ThrowsAsync<RpcException>(() => replayed.ResponseStream.MoveNext(timeout.Token))).StatusCode);
            Assert.Equal(Receiver.Artifact, await gateway.ReadTestArtifactAsync(Actor, workspace, artifactCommand, binding, timeout.Token));
            await gateway.RetireTestArtifactAsync(Actor, workspace, artifactCommand, binding, timeout.Token);
            receiver.ChangeArtifact = true;
            var corrupted = await Assert.ThrowsAsync<WorkspaceRunnerException>(() =>
                gateway.ReadTestArtifactAsync(Actor, workspace, artifactCommand, binding, timeout.Token));
            Assert.Equal(WorkspaceRunnerFailure.Integrity, corrupted.Failure);

            receiver.ChangeInputHash = false;
            var reporting = artifactCommand with { RequireControl = true, ReportRetentionDays = 14 };
            server.Services.GetRequiredService<RunnerCapabilities>().TransferEnabled = false;
            var unsupported = await Assert.ThrowsAsync<WorkspaceRunnerException>(() =>
                gateway.StartAsync(Actor, workspace, reporting, timeout.Token));
            Assert.Equal(WorkspaceRunnerFailure.Unsupported, unsupported.Failure);
            Assert.Equal(report, (await gateway.ObserveAsync(Actor, workspace, reporting, timeout.Token)).TestReport);
            Assert.Equal(report, (await gateway.CancelAsync(Actor, workspace, reporting, timeout.Token)).TestReport);
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public void HostReceiptRequiresMatchingWireDigestAndTerminalExitSemantics()
    {
        var request = new WorkspaceRunRequest { RunId = Guid.NewGuid().ToString("N"), Storage = new() };
        string Reply(string status, int? code, string input) => JsonSerializer.Serialize(new
        {
            runId = request.RunId, requestSha256 = new string('a', 64), inputSha256 = input, status, runtimeExitCode = code
        });
        var digest = DevelopmentRunnerBinding.Digest(DevelopmentRunnerBinding.HostInput(request));
        foreach (var status in new[] { "TimedOut", "Cancelled", "Interrupted" })
            Assert.False(DevelopmentRunnerGrpcService.ParseReceipt(request, Reply(status, null, digest)).HasRuntimeExitCode);
        Assert.Equal(9, DevelopmentRunnerGrpcService.ParseReceipt(request, Reply("Failed", 9, digest)).RuntimeExitCode);
        foreach (var invalid in new[] { Reply("Succeeded", 9, digest), Reply("TimedOut", 0, digest), Reply("Succeeded", 0, new string('0', 64)) })
            Assert.Equal(StatusCode.DataLoss, Assert.Throws<RpcException>(() => DevelopmentRunnerGrpcService.ParseReceipt(request, invalid)).StatusCode);
    }

    [Fact]
    public void TestArtifactManifestMustMatchExactCommandFormatAndTerminalStatus()
    {
        var request = new WorkspaceRunRequest { RunId = Guid.NewGuid().ToString("N"), Storage = new(), Runtime = "node" };
        request.Argv.Add(["/usr/local/bin/node", "--test", "--test-reporter=junit",
            "--test-reporter-destination=/tmp/whaledeck-results/tests.xml"]);
        var digest = new string('a', 64);
        string Reply(object report, string status = "Succeeded", int? exitCode = 0) => JsonSerializer.Serialize(new
        {
            runId = request.RunId, requestSha256 = digest, inputSha256 = DevelopmentRunnerBinding.Digest(DevelopmentRunnerBinding.HostInput(request)),
            status, runtimeExitCode = exitCode, testReport = report
        });
        var present = new { format = "JUnit", state = "Present", size = 27, sha256 = digest };
        Assert.Equal((uint)27, DevelopmentRunnerGrpcService.ParseReceipt(request, Reply(present)).TestReport.Size);
        Assert.Equal("Unavailable", DevelopmentRunnerGrpcService.ParseReceipt(request,
            Reply(new { format = "JUnit", state = "Unavailable" }, "Interrupted", null)).TestReport.State);
        foreach (var invalid in new[] { Reply(present with { format = "Trx" }),
            Reply(present with { size = DevelopmentRunnerBinding.MaximumArtifactBytes + 1 }), Reply(present, "Cancelled", null),
            Reply(new { format = "JUnit", state = "Missing", size = 0 }) })
            Assert.Equal(StatusCode.DataLoss, Assert.Throws<RpcException>(() => DevelopmentRunnerGrpcService.ParseReceipt(request, invalid)).StatusCode);
        request.Argv[3] += ".custom";
        Assert.Equal(StatusCode.DataLoss,
            Assert.Throws<RpcException>(() => DevelopmentRunnerGrpcService.ParseReceipt(request, Reply(present))).StatusCode);
    }

    private static Metadata Headers(string value) => new() { { "x-whaledeck-capability", value } };

    public sealed class RunnerCapabilities : AgentService.AgentServiceBase
    {
        public bool TransferEnabled { get; set; } = true;

        public override Task<CapabilitiesResponse> GetCapabilities(Google.Protobuf.WellKnownTypes.Empty request, ServerCallContext context)
        {
            var result = new CapabilitiesResponse
            {
                ProtocolMajor = 1, ProtocolMinor = TransferEnabled ? DevelopmentRunnerBinding.ArtifactTransferProtocolMinor :
                    DevelopmentRunnerBinding.ReportProtocolMinor
            };
            result.Capabilities.Add([DevelopmentRunnerBinding.Capability, DevelopmentRunnerBinding.ControlCapability,
                DevelopmentRunnerBinding.ReportCapability]);
            if (TransferEnabled) result.Capabilities.Add(DevelopmentRunnerBinding.ArtifactTransferCapability);
            return Task.FromResult(result);
        }
    }

    public sealed class Receiver : DevelopmentRunnerService.DevelopmentRunnerServiceBase
    {
        internal static byte[] Artifact { get; } = Enumerable.Repeat((byte)42, DevelopmentRunnerBinding.ArtifactChunkBytes + 1).ToArray();
        public WorkspaceRunRequest? LastRequest { get; private set; }
        public string LastToken { get; private set; } = "";
        public int Calls { get; private set; }
        public bool ChangeInputHash { get; set; }
        public bool ChangeArtifact { get; set; }

        public override Task<WorkspaceRunReceipt> Start(WorkspaceRunRequest request, ServerCallContext context) => Reply(request, context);
        public override Task<WorkspaceRunReceipt> Observe(WorkspaceRunRequest request, ServerCallContext context) => Reply(request, context);
        public override Task<WorkspaceRunReceipt> Cancel(WorkspaceRunRequest request, ServerCallContext context) => Reply(request, context);
        public override Task<WorkspaceRunReceipt> Reclaim(WorkspaceRunRequest request, ServerCallContext context) => Reply(request, context);

        public override async Task ReadTestArtifact(WorkspaceTestArtifactRequest request,
            IServerStreamWriter<WorkspaceTestArtifactChunk> responseStream, ServerCallContext context)
        {
            await responseStream.WriteAsync(new WorkspaceTestArtifactChunk
                { Content = ByteString.CopyFrom(Artifact, 0, DevelopmentRunnerBinding.ArtifactChunkBytes) });
            await responseStream.WriteAsync(new WorkspaceTestArtifactChunk { Content = ByteString.CopyFrom([ChangeArtifact ? (byte)0 : Artifact[^1]]) });
        }

        public override Task<WorkspaceTestArtifactRetirement> RetireTestArtifact(WorkspaceTestArtifactRequest request, ServerCallContext context) =>
            Task.FromResult(new WorkspaceTestArtifactRetirement { RunId = request.Run.RunId, RequestSha256 = request.RequestSha256,
                ArtifactSha256 = request.ArtifactSha256, Size = request.Size,
                InputSha256 = DevelopmentRunnerBinding.Digest(DevelopmentRunnerBinding.HostInput(request)) });

        private Task<WorkspaceRunReceipt> Reply(WorkspaceRunRequest request, ServerCallContext context)
        {
            LastRequest = request;
            LastToken = context.RequestHeaders.GetValue("x-whaledeck-capability")!;
            Calls++;
            return Task.FromResult(new WorkspaceRunReceipt
            {
                RunId = request.RunId, Status = "Succeeded", RuntimeExitCode = 0, RequestSha256 = new string('a', 64),
                InputSha256 = ChangeInputHash ? new string('0', 64) : DevelopmentRunnerBinding.Digest(DevelopmentRunnerBinding.HostInput(request)),
                TestReport = DevelopmentRunnerBinding.ReportFormat(request) is { } format ? new()
                {
                    Format = format, State = "Present", Size = (uint)Artifact.Length,
                    Sha256 = Convert.ToHexStringLower(SHA256.HashData(Artifact))
                } : null
            });
        }
    }
}
