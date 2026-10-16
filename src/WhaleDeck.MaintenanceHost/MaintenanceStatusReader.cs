using System.Net;
using System.Text.Json;

namespace WhaleDeck.MaintenanceHost;

/// <summary>Read bounded transient state; a reboot without a state file is not proof of an outage.</summary>
public sealed class MaintenanceStatusReader(HttpClient client, TimeProvider clock) : IDisposable
{
    public const string ProbeAddress = "http://127.0.0.1:18080/health/live";
    private const int MaximumBytes = 8192;
    private readonly object _probeLock = new();
    private Task<bool>? _probe;
    private DateTimeOffset _probeAt;
    private static readonly HashSet<string> KnownStates = new(StringComparer.OrdinalIgnoreCase)
    { "healthy", "updating", "Queued", "Running", "Succeeded", "Failed", "WaitingForHealth", "RollingBack", "RolledBack", "Canceled" };

    public async Task<MaintenanceStatusResult> ReadAsync(string path, CancellationToken cancellationToken)
    {
        MaintenanceStatus? recorded;
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var buffer = new byte[MaximumBytes + 1]; var count = 0;
            while (count < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(count), cancellationToken);
                if (read == 0) break;
                count += read;
            }
            if (count > MaximumBytes) return Invalid();
            using var document = JsonDocument.Parse(buffer.AsMemory(0, count), new() { MaxDepth = 8 });
            var root = document.RootElement;
            var state = Text(root, "state", 64);
            if (state is null || !KnownStates.Contains(state)) return Invalid();
            // Only public, bounded status fields are returned. Unknown fields never pass through.
            var phase = Text(root, "phase", 128) ?? (state.Equals("healthy", StringComparison.OrdinalIgnoreCase) || state.Equals("Succeeded", StringComparison.OrdinalIgnoreCase) ? "Ready" : "Maintenance");
            recorded = new(state, phase, Text(root, "message", 512) ?? "平台维护状态已记录。",
                Identity(root), Timestamp(root, "updatedAtUtc") ?? Timestamp(root, "observedAtUtc"));
        }
        catch (FileNotFoundException) { recorded = null; }
        catch (DirectoryNotFoundException) { recorded = null; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        { return Invalid(); }

        if (recorded is not null && !recorded.State.Equals("healthy", StringComparison.OrdinalIgnoreCase) &&
            !recorded.State.Equals("Succeeded", StringComparison.OrdinalIgnoreCase)) return new(200, recorded);
        if (!await GatewayReady(cancellationToken))
            return new(503, new("Unavailable", "GatewayOffline", "平台服务尚不可用，正在等待恢复。"));
        return new(200, recorded ?? new("healthy", "Ready", "平台服务运行中。"));
    }

    private Task<bool> GatewayReady(CancellationToken cancellationToken)
    {
        Task<bool> probe;
        lock (_probeLock)
        {
            var now = clock.GetUtcNow();
            if (_probe is null || now < _probeAt || now - _probeAt >= TimeSpan.FromSeconds(2))
            {
                _probeAt = now;
                _probe = ProbeAsync();
            }
            probe = _probe;
        }
        // A single disconnected browser must not cancel the shared bounded probe.
        return probe.WaitAsync(cancellationToken);
    }
    private async Task<bool> ProbeAsync()
    {
        try
        {
            using var response = await client.GetAsync(ProbeAddress, HttpCompletionOption.ResponseHeadersRead, CancellationToken.None);
            return response.StatusCode == HttpStatusCode.OK;
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException) { return false; }
    }
    private static MaintenanceStatusResult Invalid() => new(503, new("Unavailable", "InvalidMaintenanceState", "维护状态暂不可读，请稍后重试或检查维护服务。"));
    private static string? Text(JsonElement root, string name, int limit)
    {
        if (root.ValueKind != JsonValueKind.Object) throw new JsonException();
        if (!root.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind != JsonValueKind.String) throw new JsonException();
        var text = value.GetString();
        if (text is not null && text.Length > limit) throw new JsonException();
        return text;
    }
    private static string? Identity(JsonElement root)
    {
        var value = Text(root, "operationId", 36);
        if (string.IsNullOrEmpty(value)) return null;
        return Guid.TryParseExact(value, "D", out _) ? value : throw new JsonException();
    }
    private static DateTimeOffset? Timestamp(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.TryGetDateTimeOffset(out var time) ? time : null;
    public void Dispose() { client.Dispose(); GC.SuppressFinalize(this); }
}

public sealed record MaintenanceStatus(string State, string Phase, string Message, string? OperationId = null, DateTimeOffset? UpdatedAtUtc = null);
public sealed record MaintenanceStatusResult(int StatusCode, MaintenanceStatus Body);
