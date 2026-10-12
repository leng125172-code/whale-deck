using System.Net.NetworkInformation;
using System.Text.Json;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Agent.Services;

public sealed class HostGrpcService(HostReader host, OperationStore operations, BoundedProcessRunner processes, HostProcessStore hostProcesses) : HostService.HostServiceBase
{
    public override Task<HostProcessesResponse> ListProcesses(HostProcessesRequest request, ServerCallContext context) =>
        Task.FromResult(hostProcesses.Query(request));

    public override Task<HostProcessSnapshot> GetProcess(HostProcessRequest request, ServerCallContext context) =>
        Task.FromResult(hostProcesses.Get(request));

    private const string PrivilegedHelper = "/usr/local/libexec/whaledeck-privileged";
    private static readonly HashSet<string> JournalUnits = new(StringComparer.Ordinal)
    {
        "docker.service",
        "whaledeck-agent.service",
        "whaledeck-maintenance.service",
        "database-platform-workstation-update.service",
        "database-platform-workstation-update.timer"
    };
    private static readonly HashSet<string> JournalPriorities = new(StringComparer.OrdinalIgnoreCase)
    {
        "emerg", "alert", "crit", "err", "warning", "notice", "info", "debug"
    };
    public override Task<HostInfoResponse> GetHostInfo(Empty request, ServerCallContext context)
    {
        var snapshot = host.Read();
        var response = new HostInfoResponse
        {
            HostName = snapshot.HostName,
            Distribution = snapshot.Distribution,
            KernelVersion = snapshot.Kernel,
            Architecture = snapshot.Architecture,
            BootTimeUnixSeconds = snapshot.BootTimeUnixSeconds,
            UptimeSeconds = snapshot.UptimeSeconds,
            LogicalProcessorCount = snapshot.ProcessorCount,
            TotalMemoryBytes = snapshot.TotalMemoryBytes
        };
        response.Addresses.AddRange(snapshot.Addresses.Select(item => new HostAddress
        {
            InterfaceName = item.InterfaceName,
            Address = item.Address
        }));
        return Task.FromResult(response);
    }

    public override async Task StreamMetrics(MetricsRequest request, IServerStreamWriter<MetricPoint> responseStream, ServerCallContext context)
    {
        var singleBatch = request.IntervalSeconds == 0;
        var interval = TimeSpan.FromSeconds(Math.Clamp(request.IntervalSeconds, 6, 60));
        while (!context.CancellationToken.IsCancellationRequested)
        {
            var usage = host.ReadUsage();
            var timestamp = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow);
            await responseStream.WriteAsync(new MetricPoint { MetricKind = "cpu.utilization", DeviceId = "host", Value = usage.CpuPercent, Unit = "percent", Quality = "Good", SampledAtUtc = timestamp });
            await responseStream.WriteAsync(new MetricPoint { MetricKind = "memory.utilization", DeviceId = "host", Value = usage.MemoryPercent, Unit = "percent", Quality = "Good", SampledAtUtc = timestamp });
            foreach (var metric in host.ReadDeviceMetrics())
            {
                await responseStream.WriteAsync(new MetricPoint { MetricKind = metric.Kind, DeviceId = metric.DeviceId, Value = metric.Value, Unit = metric.Unit, Quality = "Good", SampledAtUtc = timestamp });
            }
            await responseStream.WriteAsync(new MetricPoint { MetricKind = "batch.complete", DeviceId = "host", Unit = "none", Quality = "Good", SampledAtUtc = timestamp });
            if (singleBatch) return;
            await Task.Delay(interval, context.CancellationToken);
        }
    }

    public override Task<ResourceCollectionResponse> ListNetworkInterfaces(Empty request, ServerCallContext context)
    {
        var response = new ResourceCollectionResponse();
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            var snapshot = new ResourceSnapshot
            {
                ResourceId = $"network-interface:{adapter.Id}",
                DisplayName = adapter.Name,
                ResourceType = "NetworkInterface",
                State = adapter.OperationalStatus.ToString(),
                Version = adapter.Speed.ToString(System.Globalization.CultureInfo.InvariantCulture)
            };
            snapshot.Attributes["type"] = adapter.NetworkInterfaceType.ToString();
            snapshot.Attributes["description"] = adapter.Description;
            snapshot.Attributes["isVirtual"] = IsVirtualInterface(adapter).ToString().ToLowerInvariant();
            snapshot.Attributes["macAddress"] = adapter.GetPhysicalAddress().ToString();
            var properties = adapter.GetIPProperties();
            snapshot.Attributes["addresses"] = string.Join(", ", properties.UnicastAddresses.Select(item => item.Address.ToString()));
            snapshot.Attributes["gateways"] = string.Join(", ", properties.GatewayAddresses.Select(item => item.Address.ToString()));
            snapshot.Attributes["dnsServers"] = string.Join(", ", properties.DnsAddresses.Select(item => item.ToString()));
            try
            {
                var statistics = adapter.GetIPStatistics();
                snapshot.Attributes["bytesReceived"] = statistics.BytesReceived.ToString(System.Globalization.CultureInfo.InvariantCulture);
                snapshot.Attributes["bytesSent"] = statistics.BytesSent.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (NetworkInformationException)
            {
                snapshot.Attributes["bytesReceived"] = "0";
                snapshot.Attributes["bytesSent"] = "0";
            }
            response.Resources.Add(snapshot);
        }
        return Task.FromResult(response);
    }

    public override async Task<ResourceCollectionResponse> ListStorageDevices(Empty request, ServerCallContext context)
    {
        var inventory = await processes.RunAsync(
            "/usr/bin/lsblk",
            ["--json", "--bytes", "--output", "NAME,KNAME,TYPE,SIZE,FSTYPE,MOUNTPOINTS,MODEL,ROTA,TRAN"],
            null,
            TimeSpan.FromSeconds(15),
            "STORAGE_QUERY_FAILED",
            context.CancellationToken);
        var response = new ResourceCollectionResponse();
        using var document = JsonDocument.Parse(inventory.StandardOutput);
        foreach (var device in FlattenDevices(document.RootElement.GetProperty("blockdevices")))
        {
            var name = Text(device, "name");
            var type = Text(device, "type");
            if (string.IsNullOrWhiteSpace(name) || type is not ("disk" or "part" or "lvm")) continue;
            var snapshot = new ResourceSnapshot
            {
                ResourceId = $"storage-device:{name}",
                DisplayName = $"/dev/{name}",
                ResourceType = type == "disk" ? "BlockDevice" : "Filesystem",
                State = "Available",
                Version = Text(device, "fstype")
            };
            snapshot.Attributes["sizeBytes"] = Number(device, "size");
            snapshot.Attributes["mountPoints"] = ArrayText(device, "mountpoints");
            snapshot.Attributes["model"] = Text(device, "model");
            snapshot.Attributes["rotational"] = Scalar(device, "rota") is "1" or "true" ? "true" : "false";
            snapshot.Attributes["transport"] = Text(device, "tran");
            if (type == "disk")
            {
                try
                {
                    var smart = await processes.RunAsync(
                        "/usr/bin/sudo",
                        ["-n", PrivilegedHelper, "storage-smart", $"/dev/{name}"],
                        null,
                        TimeSpan.FromSeconds(20),
                        "SMART_QUERY_FAILED",
                        context.CancellationToken);
                    using var smartDocument = JsonDocument.Parse(smart.StandardOutput);
                    var root = smartDocument.RootElement;
                    snapshot.Attributes["smartPassed"] = root.TryGetProperty("passed", out var passed) && passed.ValueKind is JsonValueKind.True or JsonValueKind.False
                        ? passed.GetBoolean().ToString().ToLowerInvariant()
                        : "unknown";
                    snapshot.Attributes["temperatureCelsius"] = root.TryGetProperty("temperature", out var temperature) && temperature.TryGetInt32(out var value)
                        ? value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        : string.Empty;
                }
                catch (InvalidOperationException)
                {
                    snapshot.Attributes["smartPassed"] = "unknown";
                    snapshot.Attributes["temperatureCelsius"] = string.Empty;
                }
            }
            response.Resources.Add(snapshot);
        }
        return response;
    }

    public override async Task<JournalResponse> QueryJournal(JournalRequest request, ServerCallContext context)
    {
        var take = Math.Clamp(checked((int)request.Take), 1, 500);
        var sinceMinutes = Math.Clamp(checked((int)request.SinceMinutes), 1, 10_080);
        var unit = string.IsNullOrWhiteSpace(request.Unit) ? null : request.Unit;
        if (unit is not null && !JournalUnits.Contains(unit))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "The journal unit is not approved."));
        var priority = string.IsNullOrWhiteSpace(request.Priority) ? null : request.Priority;
        if (priority is not null && !JournalPriorities.Contains(priority))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "The journal priority is invalid."));
        if (request.Keyword.Length > 128 || request.Keyword.Any(char.IsControl))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "The journal keyword is invalid."));

        var arguments = new List<string>
        {
            "--no-pager", "--output=json", "--reverse", "--since", $"-{sinceMinutes} minutes", "--lines", take.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        foreach (var approvedUnit in unit is null ? JournalUnits : [unit])
        {
            arguments.Add("--unit");
            arguments.Add(approvedUnit);
        }
        if (priority is not null) { arguments.Add("--priority"); arguments.Add(priority); }
        var result = await processes.RunAsync("/usr/bin/journalctl", arguments, null, TimeSpan.FromSeconds(20), "JOURNAL_QUERY_FAILED", context.CancellationToken);
        var response = new JournalResponse();
        foreach (var line in result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                using var entry = JsonDocument.Parse(line);
                var root = entry.RootElement;
                var message = Text(root, "MESSAGE");
                if (!string.IsNullOrWhiteSpace(request.Keyword) && !message.Contains(request.Keyword, StringComparison.OrdinalIgnoreCase)) continue;
                var timestamp = long.TryParse(Text(root, "__REALTIME_TIMESTAMP"), out var microseconds)
                    ? DateTimeOffset.FromUnixTimeMilliseconds(microseconds / 1000)
                    : DateTimeOffset.UtcNow;
                response.Entries.Add(new JournalEntry
                {
                    OccurredAtUtc = Timestamp.FromDateTimeOffset(timestamp),
                    Unit = Text(root, "_SYSTEMD_UNIT"),
                    Priority = Text(root, "PRIORITY"),
                    Process = Text(root, "_COMM"),
                    ProcessId = int.TryParse(Text(root, "_PID"), out var processId) ? processId : 0,
                    Message = message.Length <= 2048 ? message : message[..2048]
                });
            }
            catch (JsonException)
            {
                // A bounded output can end midway through the final journal entry.
            }
        }
        return response;
    }

    private static IEnumerable<JsonElement> FlattenDevices(JsonElement devices)
    {
        foreach (var device in devices.EnumerateArray())
        {
            yield return device;
            if (device.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
                foreach (var child in FlattenDevices(children)) yield return child;
        }
    }

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;

    private static string Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetRawText() : string.Empty;

    private static string Scalar(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False
            ? value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.GetRawText()
            : string.Empty;

    private static string ArrayText(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? string.Join(", ", value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()))
            : string.Empty;

    private static bool IsVirtualInterface(NetworkInterface adapter) =>
        adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel ||
        adapter.Name.StartsWith("veth", StringComparison.OrdinalIgnoreCase) ||
        adapter.Name.StartsWith("br-", StringComparison.OrdinalIgnoreCase) ||
        adapter.Name.StartsWith("docker", StringComparison.OrdinalIgnoreCase) ||
        adapter.Name.StartsWith("virbr", StringComparison.OrdinalIgnoreCase);

    public override Task<OperationHandle> RebootHost(RegisteredActionRequest request, ServerCallContext context)
    {
        if (!string.Equals(request.Action, "reboot", StringComparison.OrdinalIgnoreCase))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Only the reboot action is supported."));
        }
        var operation = operations.Create("HostRebootQueued");
        operations.Save(operation, "工作站正在准备重启。");
        return Task.FromResult(operation);
    }
}
