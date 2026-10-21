using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using WhaleDeck.Contracts.Agent.V1;

/// <summary>Read-only installed UDS probe. No host commands, synthetic samples or identity mutations.</summary>
internal static class HostMetricProbe
{
    public static async Task RunAsync()
    {
        if (!OperatingSystem.IsLinux()) throw new InvalidOperationException("METRIC_PROBE_LINUX_REQUIRED");
        var key = File.ReadAllBytes("/etc/whaledeck/agent-capability.key");
        try
        {
            if (key.Length != 32) throw new InvalidOperationException("METRIC_PROBE_KEY_INVALID");
            using var handler = new SocketsHttpHandler { UseProxy = false, ConnectCallback = async (_, token) =>
            {
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                try { await socket.ConnectAsync(new UnixDomainSocketEndPoint("/run/whaledeck/agent.sock"), token); return new NetworkStream(socket, true); }
                catch { socket.Dispose(); throw; }
            } };
            using var channel = GrpcChannel.ForAddress("http://localhost", new() { HttpHandler = handler });
            var capabilities = await new AgentService.AgentServiceClient(channel).GetCapabilitiesAsync(new(), deadline: DateTime.UtcNow.AddSeconds(10));
            Check(capabilities.ProtocolMajor == 1 && capabilities.ProtocolMinor >= 37 && capabilities.Capabilities.Contains("host.metrics.replay"));
            var client = new HostService.HostServiceClient(channel);
            var snapshot = await Read(new());
            Check(snapshot.Count == 1);
            var start = snapshot.Keys.Single();
            // Deliberately do not read during three canonical intervals: latest-only polling cannot pass.
            await Task.Delay(TimeSpan.FromSeconds(20));
            var replay = await Read(new() { ReplayRecent = true, AfterSampledAtUtc = Timestamp.FromDateTimeOffset(start) });
            Check(replay.Count is >= 3 and <= 5);
            var stamps = replay.Keys.Order().ToArray();
            Check(stamps[0] > start);
            Check(stamps.Zip(stamps.Skip(1)).All(pair => (pair.Second - pair.First).TotalSeconds is > 4 and < 9));
            var again = await Read(new() { ReplayRecent = true, AfterSampledAtUtc = Timestamp.FromDateTimeOffset(start) });
            foreach (var (timestamp, bytes) in replay)
                Check(again.TryGetValue(timestamp, out var repeated) && bytes.SequenceEqual(repeated));
            var tail = await Read(new() { ReplayRecent = true, AfterSampledAtUtc = Timestamp.FromDateTimeOffset(stamps[^1]) });
            Check(tail.Keys.All(timestamp => timestamp > stamps[^1]));
            Console.WriteLine(JsonSerializer.Serialize(new { hostMetricReplay = "passed", protocolMinor = capabilities.ProtocolMinor,
                deliberatelyUnreadSeconds = 20, recoveredCanonicalBatches = replay.Count, originalFramesStable = true, exclusiveCursor = true }));

            async Task<Dictionary<DateTimeOffset, byte[]>> Read(MetricsRequest request)
            {
                const string method = "/whaledeck.agent.v1.HostService/StreamMetrics";
                var nonce = DateTimeOffset.UtcNow.AddSeconds(45).ToUnixTimeSeconds() + "." + Guid.NewGuid().ToString("N");
                var headers = new Metadata { { "x-whaledeck-capability", nonce + "." + Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(nonce + "." + method))) } };
                using var call = client.StreamMetrics(request, headers, deadline: DateTime.UtcNow.AddSeconds(15));
                var result = new Dictionary<DateTimeOffset, byte[]>();
                using var batch = new MemoryStream();
                DateTimeOffset? current = null;
                var count = 0;
                while (await call.ResponseStream.MoveNext())
                {
                    var point = call.ResponseStream.Current;
                    var timestamp = point.SampledAtUtc.ToDateTimeOffset();
                    if (point.MetricKind == "batch.complete")
                    {
                        Check(current == timestamp && batch.Length > 0 && result.Count < 120);
                        Check(result.TryAdd(timestamp, SHA256.HashData(batch.ToArray())));
                        batch.SetLength(0); batch.Position = 0; current = null;
                    }
                    else
                    {
                        Check(++count <= 65_536 && (current is null || current == timestamp));
                        current = timestamp;
                        using var coded = new Google.Protobuf.CodedOutputStream(batch, leaveOpen: true);
                        point.WriteTo(coded); coded.Flush();
                    }
                }
                Check(current is null && batch.Length == 0);
                return result;
            }
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    private static void Check(bool value)
    {
        if (!value) throw new InvalidOperationException("METRIC_PROBE_ASSERTION_FAILED");
    }
}
