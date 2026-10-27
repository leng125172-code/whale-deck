using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WhaleDeck.Infrastructure.Agent;
using App = WhaleDeck.Application.Models;
using Rpc = WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.UnitTests.Agent;

/// <summary>Synthetic SDK/HTTP2 traffic over a private UDS; this is not evidence from the installed Agent or Root helper.</summary>
public sealed class ContainerHistoryGatewayTests
{
    private const string Service = "/whaledeck.agent.v1.ContainerHistoryService/";
    private static readonly App.ContainerFileLocationDto Location = new(new('a', 64),
        "777:2026-10-22T01:02:03.123456789Z", "work/中文.txt");
    private const string After = "11111111111111111111111111111111";
    private const string CaptureId = "22222222222222222222222222222222";
    private static readonly string Version = new('A', 64);
    private static readonly string SourceVersion = new('B', 64);
    private static readonly string CaptureHash = new('C', 64);
    private static readonly string TargetProof = new('D', 64);

    [Fact]
    public async Task ListSendsEveryBoundFieldAndMapsTheBoundPage()
    {
        await using var harness = await Harness.Start();
        var page = await harness.Gateway.ListContainerHistoryAsync(Location, After, 10, Version, default);

        Assert.Equal(Location, page.Location);
        Assert.Equal(TargetProof, page.TargetProof);
        Assert.Equal(Version, page.Version);
        Assert.Equal("", page.NextCursor);
        Assert.Equal(1, page.Scanned);
        Assert.Equal(0, page.InvalidRecords);
        Assert.True(page.Complete);
        Assert.Equal(harness.ExpectedEntry, Assert.Single(page.Entries));

        var call = Assert.Single(harness.Calls);
        Assert.Equal("List", call.Action);
        var request = Assert.IsType<Rpc.ContainerHistoryListRequest>(call.Request);
        AssertLocation(request.Location);
        Assert.Equal(After, request.After);
        Assert.Equal(10U, request.Take);
        Assert.Equal(Version, request.ExpectedVersion);
        harness.AssertSignature(call);
    }

    [Theory]
    [InlineData(2097159)]
    [InlineData(0)]
    public async Task SourceYieldsExactBytesOnlyAfterBoundEndAndRealRpcEof(int length)
    {
        await using var harness = await Harness.Start();
        harness.Content = Enumerable.Range(0, length).Select(index => (byte)(index % 251)).ToArray();
        harness.HoldEof = true;
        var expected = harness.ExpectedEntry;

        var reading = ReadAll(harness.Gateway.ReadContainerHistorySourceAsync(Location, expected, default));
        await harness.EndWritten.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(reading.IsCompleted);
        harness.ReleaseEof.TrySetResult(true);

        Assert.Equal(harness.Content, await reading);
        Assert.True(harness.RpcReachedEof);
        var call = Assert.Single(harness.Calls);
        Assert.Equal("ReadSource", call.Action);
        var request = Assert.IsType<Rpc.ContainerHistorySourceRequest>(call.Request);
        AssertLocation(request.Location);
        Assert.Equal(expected.CaptureId, request.CaptureId);
        Assert.Equal(expected.CaptureHash, request.ExpectedCaptureHash);
        harness.AssertSignature(call);
    }

    [Theory]
    [InlineData("missing-end")]
    [InlineData("extra-after-end")]
    [InlineData("wrong-sha")]
    [InlineData("wrong-offset")]
    [InlineData("metadata-container")]
    [InlineData("metadata-capture-hash")]
    public async Task InvalidOrTruncatedSourceNeverEnumeratesAsSuccess(string variant)
    {
        await using var harness = await Harness.Start();
        harness.Content = [42, 43, 44];
        harness.InvalidSource = variant;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ReadAll(harness.Gateway.ReadContainerHistorySourceAsync(Location, harness.ExpectedEntry, default)));
        Assert.Equal("CONTAINER_HISTORY_OUTPUT_INVALID", error.Message);
        Assert.Single(harness.Calls);
    }

    [Theory]
    [InlineData(2U, 42U, true, true)]
    [InlineData(1U, 41U, true, true)]
    [InlineData(1U, 42U, false, true)]
    [InlineData(1U, 42U, true, false)]
    public async Task IncompleteNegotiationRefusesBothMethodsWithoutServiceCalls(
        uint major, uint minor, bool history, bool source)
    {
        await using var harness = await Harness.Start(major, minor, history, source);
        var list = await Assert.ThrowsAsync<RpcException>(() =>
            harness.Gateway.ListContainerHistoryAsync(Location, After, 10, Version, default));
        var read = await Assert.ThrowsAsync<RpcException>(() =>
            ReadAll(harness.Gateway.ReadContainerHistorySourceAsync(Location, harness.ExpectedEntry, default)));
        Assert.Equal(StatusCode.Unimplemented, list.StatusCode);
        Assert.Equal("CONTAINER_HISTORY_CAPABILITY_UNAVAILABLE", list.Status.Detail);
        Assert.Equal(StatusCode.Unimplemented, read.StatusCode);
        Assert.Equal("CONTAINER_HISTORY_CAPABILITY_UNAVAILABLE", read.Status.Detail);
        Assert.Empty(harness.Calls);
    }

    private static void AssertLocation(Rpc.ContainerFileLocation location)
    {
        Assert.Equal(Location.ContainerId, location.ContainerId);
        Assert.Equal(Location.InstanceId, location.InstanceId);
        Assert.Equal(Location.Path, location.Path);
    }

    private static async Task<byte[]> ReadAll(IAsyncEnumerable<ReadOnlyMemory<byte>> source)
    {
        using var output = new MemoryStream();
        await foreach (var chunk in source) await output.WriteAsync(chunk);
        return output.ToArray();
    }

    public sealed record Captured(string Action, IMessage Request, string Token, string BodyHash);

    public sealed class Harness : IAsyncDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "wd-history-" + Guid.NewGuid().ToString("N")[..10]);
        private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
        private WebApplication _server = null!;
        public AgentGateway Gateway { get; private set; } = null!;
        public uint Major { get; private init; }
        public uint Minor { get; private init; }
        public bool HistoryCapability { get; private init; }
        public bool SourceCapability { get; private init; }
        public byte[] Content { get; set; } = [42];
        public string? InvalidSource { get; set; }
        public bool HoldEof { get; set; }
        public bool RpcReachedEof { get; set; }
        public ConcurrentQueue<Captured> Calls { get; } = new();
        public TaskCompletionSource<bool> EndWritten { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> ReleaseEof { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public App.ContainerHistoryEntryDto ExpectedEntry => new(CaptureId, Location.ContainerId, Location.InstanceId,
            Location.Path, SourceVersion, Content.LongLength, Convert.ToHexString(SHA256.HashData(Content)),
            1760000000000L, true, "Present", CaptureHash);

        public static async Task<Harness> Start(uint major = 1, uint minor = 42, bool history = true, bool source = true)
        {
            var harness = new Harness { Major = major, Minor = minor, HistoryCapability = history, SourceCapability = source };
            if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Private UDS transport fixture requires Linux.");
            Directory.CreateDirectory(harness._directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var socket = Path.Combine(harness._directory, "agent.sock");
            var key = Path.Combine(harness._directory, "fixture.key");
            File.WriteAllBytes(key, harness._key);
            File.SetUnixFileMode(key, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            try
            {
                var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
                builder.Logging.ClearProviders();
                builder.WebHost.ConfigureKestrel(options =>
                    options.ListenUnixSocket(socket, listen => listen.Protocols = HttpProtocols.Http2));
                builder.Services.AddGrpc();
                builder.Services.AddSingleton(harness);
                harness._server = builder.Build();
                harness._server.MapGrpcService<FakeAgent>();
                harness._server.MapGrpcService<FakeHistory>();
                await harness._server.StartAsync();
                harness.Gateway = new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Agent:SocketPath"] = socket,
                    ["Agent:CapabilityKeyFile"] = key
                }).Build());
                return harness;
            }
            catch
            {
                await harness.DisposeAsync();
                throw;
            }
        }

        public void Capture(string action, IMessage request, ServerCallContext context)
        {
            Calls.Enqueue(new(action, request,
                context.RequestHeaders.GetValue("x-whaledeck-capability") ?? "",
                context.RequestHeaders.GetValue("x-whaledeck-request-sha256") ?? ""));
        }

        public void AssertSignature(Captured call)
        {
            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            digest.AppendData(Encoding.UTF8.GetBytes(
                "whaledeck-container-history-request-v1\n" + call.Request.Descriptor.FullName + "\n"));
            digest.AppendData(call.Request.ToByteArray());
            Assert.Equal(Convert.ToHexString(digest.GetHashAndReset()), call.BodyHash);

            var pieces = call.Token.Split('.');
            Assert.Equal(3, pieces.Length);
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var expires = long.Parse(pieces[0], CultureInfo.InvariantCulture);
            Assert.InRange(expires - now, 1, 60);
            Assert.True(Guid.TryParseExact(pieces[1], "N", out var nonce));
            Assert.NotEqual(Guid.Empty, nonce);

            string Mac(string domain, string method) => Convert.ToHexString(HMACSHA256.HashData(_key,
                Encoding.UTF8.GetBytes(domain + "\n" + pieces[0] + "." + pieces[1] + "." + method + "." + call.BodyHash)));
            var method = Service + call.Action;
            Assert.Equal(Mac("whaledeck-container-history-capability-v1", method), pieces[2]);
            Assert.NotEqual(Mac("whaledeck-container-history-capability-v1",
                Service + (call.Action == "List" ? "ReadSource" : "List")), pieces[2]);
            Assert.NotEqual(Mac("whaledeck-container-file-capability-v1", method), pieces[2]);
            Assert.NotEqual(Mac("whaledeck-container-history-capability-v1",
                "/whaledeck.agent.v1.ContainerFileWriteService/" + call.Action), pieces[2]);
        }

        public async ValueTask DisposeAsync()
        {
            Gateway?.Dispose();
            if (_server is not null)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await _server.StopAsync(timeout.Token);
                await _server.DisposeAsync();
            }
            CryptographicOperations.ZeroMemory(_key);
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
    }

    public sealed class FakeAgent(Harness harness) : Rpc.AgentService.AgentServiceBase
    {
        public override Task<Rpc.CapabilitiesResponse> GetCapabilities(
            Google.Protobuf.WellKnownTypes.Empty request, ServerCallContext context)
        {
            var response = new Rpc.CapabilitiesResponse { ProtocolMajor = harness.Major, ProtocolMinor = harness.Minor };
            if (harness.HistoryCapability) response.Capabilities.Add("docker.files.history");
            if (harness.SourceCapability) response.Capabilities.Add("docker.files.history-source");
            return Task.FromResult(response);
        }
    }

    public sealed class FakeHistory(Harness harness) : Rpc.ContainerHistoryService.ContainerHistoryServiceBase
    {
        public override Task<Rpc.ContainerHistoryPage> List(Rpc.ContainerHistoryListRequest request, ServerCallContext context)
        {
            harness.Capture("List", request.Clone(), context);
            var expected = harness.ExpectedEntry;
            var response = new Rpc.ContainerHistoryPage
            {
                Location = request.Location.Clone(),
                TargetProof = TargetProof,
                Version = Version,
                NextCursor = "",
                Scanned = 1,
                InvalidRecords = 0,
                Complete = true
            };
            response.Entries.Add(Entry(expected));
            return Task.FromResult(response);
        }

        public override async Task ReadSource(Rpc.ContainerHistorySourceRequest request,
            IServerStreamWriter<Rpc.ContainerHistorySourceFrame> responseStream, ServerCallContext context)
        {
            harness.Capture("ReadSource", request.Clone(), context);
            var expected = harness.ExpectedEntry;
            var metadata = Entry(expected);
            if (harness.InvalidSource == "metadata-container") metadata.ContainerId = new('b', 64);
            if (harness.InvalidSource == "metadata-capture-hash") metadata.CaptureHash = new('E', 64);
            await responseStream.WriteAsync(new Rpc.ContainerHistorySourceFrame
            {
                Kind = "metadata",
                Metadata = metadata,
                TargetProof = TargetProof,
                CaptureHash = expected.CaptureHash
            });

            long offset = 0;
            for (var start = 0; start < harness.Content.Length; start += 32768)
            {
                var chunk = harness.Content.AsSpan(start, Math.Min(32768, harness.Content.Length - start)).ToArray();
                await responseStream.WriteAsync(new Rpc.ContainerHistorySourceFrame
                {
                    Kind = "chunk",
                    Offset = harness.InvalidSource == "wrong-offset" && start == 0 ? 1 : offset,
                    Data = ByteString.CopyFrom(chunk)
                });
                offset += chunk.Length;
            }

            if (harness.InvalidSource != "missing-end")
            {
                await responseStream.WriteAsync(new Rpc.ContainerHistorySourceFrame
                {
                    Kind = "end",
                    SizeBytes = expected.SizeBytes,
                    Sha256 = harness.InvalidSource == "wrong-sha" ? new('F', 64) : expected.Sha256,
                    CaptureHash = expected.CaptureHash,
                    TargetProof = TargetProof
                });
                if (harness.InvalidSource == "extra-after-end")
                    await responseStream.WriteAsync(new Rpc.ContainerHistorySourceFrame
                        { Kind = "chunk", Offset = offset, Data = ByteString.CopyFrom([42]) });
            }

            harness.EndWritten.TrySetResult(true);
            if (harness.HoldEof) await harness.ReleaseEof.Task.WaitAsync(context.CancellationToken);
            harness.RpcReachedEof = true;
        }

        private static Rpc.ContainerHistoryEntry Entry(App.ContainerHistoryEntryDto value) => new()
        {
            CaptureId = value.CaptureId,
            ContainerId = value.ContainerId,
            OriginalInstanceId = value.OriginalInstanceId,
            Path = value.Path,
            SourceVersion = value.SourceVersion,
            SizeBytes = value.SizeBytes,
            Sha256 = value.Sha256,
            CreatedAtUnixMs = value.CreatedAtUnixMs,
            DifferentSourceDevice = value.DifferentSourceDevice,
            Availability = value.Availability,
            CaptureHash = value.CaptureHash
        };
    }
}
