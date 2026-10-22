using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Configuration;
using WhaleDeck.Infrastructure.Agent;
using App = WhaleDeck.Application.Models;
using Rpc = WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.UnitTests.Agent;

/// <summary>In-memory gRPC projection/signature tests, not installed UDS evidence.</summary>
public sealed class ContainerFileGatewayTests
{
    private const string Prefix = "/whaledeck.agent.v1.ContainerFileService/";
    private static App.ContainerFileLocationDto Location => new(new('a', 64), "777:2026-10-22T01:02:03.123456789Z", "中文/🫧.bin");

    [Theory]
    [InlineData(1U, 37U, true)][InlineData(2U, 38U, true)][InlineData(0U, 38U, true)][InlineData(1U, 38U, false)]
    public async Task OldMinorWrongMajorAndMissingCapabilityRefuseBeforeContainerRequest(uint major, uint minor, bool capability)
    {
        using var harness = new Harness([], major, minor, capability);
        var error = await Assert.ThrowsAsync<RpcException>(() => harness.Gateway.InspectContainerFileTargetAsync(Location.ContainerId, default));
        Assert.Equal(StatusCode.Unimplemented, error.StatusCode);
        Assert.Equal("CONTAINER_FILES_CAPABILITY_UNAVAILABLE", error.Status.Detail);
        Assert.Null(harness.Handler.Request);
    }

    [Theory]
    [InlineData("InspectTarget")][InlineData("List")][InlineData("Inspect")][InlineData("Download")]
    public async Task FourMethodsKeepExactContainerInstanceUnicodeAndIndependentSignature(string method)
    {
        var entry = new Rpc.FileEntry { Name = "🫧.bin", Kind = "File", SizeBytes = 0, Version = new('A', 64), Mode = "0640" };
        var page = new Rpc.FileListResponse { Version = new('B', 64), NextCursor = "下页" }; page.Entries.Add(entry);
        IMessage[] response = method switch
        {
            "InspectTarget" => [new Rpc.ContainerTerminalTarget { ContainerId = Location.ContainerId, InstanceId = Location.InstanceId, Name = "ordinary", ImageId = "sha256:" + new string('b', 64), ConfiguredUser = "1000:1000" }],
            "List" => [page], "Inspect" => [entry],
            _ => [new Rpc.FileDownloadFrame { Kind = "metadata", Metadata = entry }, new Rpc.FileDownloadFrame { Kind = "end", Version = entry.Version, Sha256 = new('C', 64), SizeBytes = 0 }]
        };
        using var harness = new Harness(response);
        if (method == "InspectTarget")
        {
            var target = await harness.Gateway.InspectContainerFileTargetAsync(Location.ContainerId, default);
            Assert.Equal(Location.ContainerId, target.ContainerId); Assert.Equal(Location.InstanceId, target.InstanceId); Assert.Equal("1000:1000", target.ConfiguredUser);
            var request = Assert.IsType<Rpc.ResourceReference>(harness.Handler.Request);
            Assert.Equal("Container", request.ResourceType); Assert.Equal(Location.ContainerId, request.ResourceId);
        }
        else
        {
            Rpc.ContainerFileLocation location;
            if (method == "List")
            {
                var actual = await harness.Gateway.ListContainerFilesAsync(Location, "上一页", 73, default);
                Assert.Equal("下页", actual.NextCursor); Assert.Equal(new string('B', 64), actual.Version); Assert.Equal(entry.Name, Assert.Single(actual.Entries).Name);
                var request = Assert.IsType<Rpc.ContainerFileListRequest>(harness.Handler.Request);
                Assert.Equal("上一页", request.After); Assert.Equal(73U, request.Take); location = request.Location;
            }
            else if (method == "Inspect")
            {
                var actual = Assert.IsType<App.FileEntryDto>(await harness.Gateway.InspectContainerFileAsync(Location, default));
                Assert.Equal(entry.Name, actual.Name); Assert.Equal(entry.Version, actual.Version);
                location = Assert.IsType<Rpc.ContainerFileLocation>(harness.Handler.Request);
            }
            else
            {
                var frames = new List<App.FileDownloadPart>();
                await foreach (var frame in harness.Gateway.DownloadContainerFileAsync(Location, entry.Version, default)) frames.Add(frame);
                Assert.Equal(["metadata", "end"], frames.Select(frame => frame.Kind));
                Assert.Equal(entry.Version, frames[^1].Version); Assert.Equal(new string('C', 64), frames[^1].Sha256);
                var request = Assert.IsType<Rpc.ContainerFileDownloadRequest>(harness.Handler.Request);
                Assert.Equal(entry.Version, request.ExpectedVersion); location = request.Location;
            }
            Assert.Equal(Location.ContainerId, location.ContainerId); Assert.Equal(Location.InstanceId, location.InstanceId); Assert.Equal(Location.Path, location.Path);
            Assert.Equal(3, Rpc.ContainerFileLocation.Descriptor.Fields.InFieldNumberOrder().Count);
        }
        harness.AssertSignature(method);
    }

    [Theory]
    [InlineData(StatusCode.NotFound)][InlineData(StatusCode.PermissionDenied)][InlineData(StatusCode.Aborted)]
    public async Task OnlyMissingFilesBecomeNullOtherRefusalsPropagate(StatusCode status)
    {
        using var harness = new Harness([], status: status);
        if (status == StatusCode.NotFound) Assert.Null(await harness.Gateway.InspectContainerFileAsync(Location, default));
        else Assert.Equal(status, (await Assert.ThrowsAsync<RpcException>(() => harness.Gateway.InspectContainerFileAsync(Location, default))).StatusCode);
    }

    private sealed class Harness : IDisposable
    {
        private readonly string _file = Path.Combine(Path.GetTempPath(), "wd-container-file-key-" + Guid.NewGuid().ToString("N"));
        private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
        public AgentGateway Gateway { get; }
        public Handler Handler { get; }
        public Harness(IMessage[] response, uint major = 1, uint minor = 38, bool capability = true, StatusCode status = StatusCode.OK)
        {
            File.WriteAllBytes(_file, _key);
            Gateway = new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:CapabilityKeyFile"] = _file }).Build());
            Handler = new(response, major, minor, capability, status);
            var channel = GrpcChannel.ForAddress("http://127.0.0.1", new() { HttpHandler = Handler });
            var field = typeof(AgentGateway).GetField("_channel", BindingFlags.Instance | BindingFlags.NonPublic)!;
            ((GrpcChannel)field.GetValue(Gateway)!).Dispose(); field.SetValue(Gateway, channel);
            typeof(AgentGateway).GetField("_agent", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Gateway, new Rpc.AgentService.AgentServiceClient(channel));
        }
        public void AssertSignature(string method)
        {
            var parts = Assert.IsType<string>(Handler.Capability).Split('.'); Assert.Equal(3, parts.Length);
            var message = parts[0] + "." + parts[1] + ".";
            Assert.Equal(Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(message + Prefix + method))), parts[2]);
            Assert.NotEqual(Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(message + "/whaledeck.agent.v1.FileTransferService/" + method))), parts[2]);
        }
        public void Dispose()
        {
            Gateway.Dispose();
            if (typeof(AgentGateway).GetField("_capabilityKey", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Gateway) is byte[] copy) CryptographicOperations.ZeroMemory(copy);
            CryptographicOperations.ZeroMemory(_key); File.Delete(_file);
        }
    }

    private sealed class Handler(IMessage[] frames, uint major, uint minor, bool capability, StatusCode status) : HttpMessageHandler
    {
        public IMessage? Request { get; private set; }
        public string? Capability { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var capabilities = path == "/whaledeck.agent.v1.AgentService/GetCapabilities";
            IMessage[] messages;
            if (capabilities)
            {
                var value = new Rpc.CapabilitiesResponse { ProtocolMajor = major, ProtocolMinor = minor };
                if (capability) value.Capabilities.Add("docker.files.read"); messages = [value];
            }
            else
            {
                Assert.StartsWith(Prefix, path, StringComparison.Ordinal);
                Capability = Assert.Single(request.Headers.GetValues("x-whaledeck-capability"));
                var raw = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
                Assert.Equal(0, raw[0]); Assert.Equal(raw.Length - 5, BinaryPrimitives.ReadInt32BigEndian(raw.AsSpan(1, 4)));
                Request = path[Prefix.Length..] switch { "InspectTarget" => Rpc.ResourceReference.Parser.ParseFrom(raw.AsSpan(5)),
                    "List" => Rpc.ContainerFileListRequest.Parser.ParseFrom(raw.AsSpan(5)), "Inspect" => Rpc.ContainerFileLocation.Parser.ParseFrom(raw.AsSpan(5)),
                    "Download" => Rpc.ContainerFileDownloadRequest.Parser.ParseFrom(raw.AsSpan(5)), _ => throw new InvalidOperationException() };
                messages = status == StatusCode.OK ? frames : [];
            }
            using var body = new MemoryStream();
            foreach (var frame in messages)
            {
                var bytes = frame.ToByteArray(); var prefix = new byte[5]; BinaryPrimitives.WriteInt32BigEndian(prefix.AsSpan(1), bytes.Length);
                body.Write(prefix); body.Write(bytes);
            }
            var result = new HttpResponseMessage(HttpStatusCode.OK) { Version = HttpVersion.Version20, Content = new ByteArrayContent(body.ToArray()) };
            result.Content.Headers.ContentType = new MediaTypeHeaderValue("application/grpc");
            result.TrailingHeaders.Add("grpc-status", ((int)(capabilities ? StatusCode.OK : status)).ToString(System.Globalization.CultureInfo.InvariantCulture));
            return result;
        }
    }
}
