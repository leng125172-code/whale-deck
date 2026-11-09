using System.Globalization;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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

public sealed class DevelopmentStorageRpcTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("wd-storage-rpc-");
    private readonly CancellationTokenSource _timeout = new(TimeSpan.FromSeconds(25));
    private CancellationToken TestContextToken => _timeout.Token;

    public void Dispose()
    {
        _timeout.Dispose();
        _directory.Delete(recursive: true);
    }

    [Fact]
    public async Task UnixSocketGatewaySignsExactBodyAndInterceptorRejectsReplayAndTampering()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var keyFile = Path.Combine(_directory.FullName, "key");
        await File.WriteAllBytesAsync(keyFile, key);
        var socket = Path.Combine(_directory.FullName, "agent.sock");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        builder.Configuration["Agent:CapabilityKeyFile"] = keyFile;
        builder.WebHost.ConfigureKestrel(options => options.ListenUnixSocket(socket, listen => listen.Protocols = HttpProtocols.Http2));
        builder.Services.AddGrpc(options => options.Interceptors.Add<CapabilityInterceptor>());
        builder.Services.AddSingleton<CapabilityValidator>();
        builder.Services.AddSingleton(new AgentGrpcService(null!));
        builder.Services.AddSingleton<StorageReceiver>();
        await using var server = builder.Build();
        server.MapGrpcService<AgentGrpcService>();
        server.MapGrpcService<StorageReceiver>();
        await server.StartAsync(TestContextToken);
        using var gateway = new AgentGateway(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Agent:CapabilityKeyFile"] = keyFile, ["Agent:SocketPath"] = socket
        }).Build());
        var actor = new TerminalActor("workspace-owner", null, [], new string('a', 64), "trace", null);
        var workspace = new WorkspaceDefinition(Guid.NewGuid(), "example", actor.Subject, "nvme-local", "Registered",
            1, new(200, 256, 512), new(1, []), new string('a', 64), DateTimeOffset.UtcNow);
        await gateway.AllocateAsync(actor, workspace, TestContextToken);
        var receiver = server.Services.GetRequiredService<StorageReceiver>();
        var request = Assert.IsType<WorkspaceAllocationRequest>(receiver.LastRequest);
        Assert.Equal(workspace.Id.ToString("D"), request.WorkspaceId);
        Assert.Equal(workspace.EnvironmentSha256, request.EnvironmentSha256);
        Assert.Equal(actor.Subject, request.ActorSubject);
        Assert.Equal(actor.TraceId, request.TraceId);
        Assert.Equal(1, receiver.Calls);

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
        var client = new DevelopmentStorageService.DevelopmentStorageServiceClient(channel);
        var expiry = DateTimeOffset.UtcNow.AddSeconds(30).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        string Sign(string? hash)
        {
            var nonce = Guid.NewGuid().ToString("N");
            var payload = hash is null ? $"{expiry}.{nonce}.{DevelopmentStorageBinding.Method}" :
                $"whaledeck-development-storage-capability-v1\n{expiry}.{nonce}.{DevelopmentStorageBinding.Method}.{hash}";
            return $"{expiry}.{nonce}.{Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload)))}";
        }
        var fresh = Sign(DevelopmentStorageBinding.Hash(request));
        var tampered = request.Clone();
        tampered.DiskMib++;
        foreach (var attempt in new[]
        {
            (request, new Metadata()),
            (request, Headers(receiver.LastToken!)),
            (request, Headers(Sign(null))),
            (tampered, Headers(fresh)),
            (request, new Metadata { { "x-whaledeck-capability", fresh }, { "x-whaledeck-capability", fresh } })
        })
        {
            var denied = await Assert.ThrowsAsync<RpcException>(async () =>
                await client.AllocateAsync(attempt.Item1, attempt.Item2, cancellationToken: TestContextToken));
            Assert.Equal(StatusCode.PermissionDenied, denied.StatusCode);
        }
        Assert.Equal(1, receiver.Calls);
        await client.AllocateAsync(request, Headers(fresh), cancellationToken: TestContextToken);
        Assert.Equal(2, receiver.Calls);
        await Assert.ThrowsAsync<ArgumentException>(() => gateway.AllocateAsync(actor with { Subject = "other" }, workspace, TestContextToken));
        receiver.Mismatch = true;
        var invalidReceipt = await Assert.ThrowsAsync<RpcException>(() => gateway.AllocateAsync(actor, workspace, TestContextToken));
        Assert.Equal(StatusCode.DataLoss, invalidReceipt.StatusCode);
        await server.StopAsync(TestContextToken);
    }

    private static Metadata Headers(string value) => new() { { "x-whaledeck-capability", value } };

    [Fact]
    public void HostReceiptMustMatchRequestAndContainCompleteProof()
    {
        var request = new WorkspaceAllocationRequest
        {
            WorkspaceId = Guid.NewGuid().ToString("D"), StorageRootId = "nvme-local",
            EnvironmentSha256 = new string('a', 64), DiskMib = 256
        };
        var hostProof = new
        {
            schemaVersion = 1, workspaceId = request.WorkspaceId, rootId = request.StorageRootId,
            environmentSha256 = request.EnvironmentSha256, diskMiB = request.DiskMib,
            filesystemUuid = Guid.NewGuid().ToString("D"), rootInode = 2, directoryInode = 3
        };
        var valid = JsonSerializer.Serialize(hostProof);
        Assert.True(DevelopmentStorageBinding.Matches(request, DevelopmentStorageGrpcService.ParseReceipt(request, valid)));
        foreach (var invalid in new[]
        {
            JsonSerializer.Serialize(hostProof with { diskMiB = 512 }),
            JsonSerializer.Serialize(hostProof with { directoryInode = 0 }),
            valid.Insert(1, "\"diskMiB\":256,"),
            valid + "{}"
        })
        {
            var rejected = Assert.Throws<RpcException>(() => DevelopmentStorageGrpcService.ParseReceipt(request, invalid));
            Assert.Equal(StatusCode.DataLoss, rejected.StatusCode);
        }
    }

    public sealed class StorageReceiver : DevelopmentStorageService.DevelopmentStorageServiceBase
    {
        public WorkspaceAllocationRequest? LastRequest { get; private set; }
        public string? LastToken { get; private set; }
        public int Calls { get; private set; }
        public bool Mismatch { get; set; }

        public override Task<WorkspaceAllocationReceipt> Allocate(WorkspaceAllocationRequest request, ServerCallContext context)
        {
            Calls++;
            LastRequest = request;
            LastToken = context.RequestHeaders.GetValue("x-whaledeck-capability");
            return Task.FromResult(new WorkspaceAllocationReceipt
            {
                WorkspaceId = request.WorkspaceId, StorageRootId = request.StorageRootId,
                EnvironmentSha256 = request.EnvironmentSha256, DiskMib = Mismatch ? request.DiskMib + 1 : request.DiskMib
            });
        }
    }
}
