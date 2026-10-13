using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

/// <summary>Validated rule semantics and tri-state lifecycle; missing telemetry is never recovery.</summary>
public sealed record MonitoringAlertPolicy(double? Threshold, double? CriticalThreshold, double? RecoveryThreshold,
    int MinimumCoveragePercent, string[] ResourceIds, string[] DeviceIds)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public static MonitoringAlertPolicy Parse(SaveAlertRuleCommand command)
    {
        if (command.RuleType is not ("CpuUtilization" or "MemoryUtilization" or "DiskUtilization" or "AgentOffline" or "ContainerHealth" or "BackupFailure"))
            throw new ArgumentException("Unsupported alert rule type.");
        if (command.EvaluationWindowSeconds is < 12 or > 86400 || command.Severity is not ("Info" or "Warning" or "Critical"))
            throw new ArgumentException("Invalid alert evaluation window or severity.");
        using var selector = ReadObject(command.ResourceSelectorJson);
        CheckKeys(selector.RootElement, ["resourceIds", "deviceIds"]);
        var resources = ReadIds(selector.RootElement, "resourceIds");
        var devices = ReadIds(selector.RootElement, "deviceIds");
        using var threshold = ReadObject(command.ThresholdJson);
        CheckKeys(threshold.RootElement, ["operator", "value", "criticalValue", "recoveryValue", "minimumCoveragePercent"]);
        if (threshold.RootElement.TryGetProperty("operator", out var op) && (op.ValueKind != JsonValueKind.String || op.GetString() != ">="))
            throw new ArgumentException("Only the >= threshold operator is supported.");
        var value = ReadNumber(threshold.RootElement, "value");
        var critical = ReadNumber(threshold.RootElement, "criticalValue");
        var recovery = ReadNumber(threshold.RootElement, "recoveryValue");
        var coverage = ReadNumber(threshold.RootElement, "minimumCoveragePercent") ?? 80;
        if (coverage is < 50 or > 100 || coverage != Math.Truncate(coverage))
            throw new ArgumentException("Minimum coverage must be an integer from 50 to 100.");
        var metric = command.RuleType.EndsWith("Utilization", StringComparison.Ordinal);
        if (metric && (value is null || value is < 0 or > 100 || critical is < 0 or > 100 || recovery is < 0 or > 100 || critical < value || recovery > value))
            throw new ArgumentException("Percentage thresholds must be 0–100, recovery <= value <= criticalValue.");
        if (!metric && (devices.Length != 0 || critical is not null || recovery is not null))
            throw new ArgumentException("Device and multi-threshold fields apply only to utilization rules.");
        return new(value, critical, recovery, (int)coverage, resources, devices);
    }

    public bool Includes(ManagedResource resource) => ResourceIds.Length == 0 || ResourceIds.Contains(resource.ExternalId, StringComparer.Ordinal) ||
        ResourceIds.Any(id => Guid.TryParse(id, out var stableId) && stableId == resource.Id);

    public bool IncludesDevice(string dimensions)
    {
        if (DeviceIds.Length == 0) return true;
        using var json = ReadObject(dimensions);
        return json.RootElement.TryGetProperty("DeviceId", out var device) && device.ValueKind == JsonValueKind.String &&
            DeviceIds.Contains(device.GetString()!, StringComparer.Ordinal);
    }

    public AlertObservation Metric(Guid resourceId, double? average, long validSamples, DateTimeOffset? latestValid,
        DateTimeOffset? latestObserved, int windowSeconds, DateTimeOffset now, string severity, bool alreadyActive)
    {
        var expected = Math.Max(1, windowSeconds / 6);
        var coverage = Math.Min(100, 100d * validSamples / expected);
        var known = average is { } number && double.IsFinite(number) && latestValid == latestObserved && latestValid >= now.AddSeconds(-18) &&
            latestValid <= now && coverage >= MinimumCoveragePercent;
        var threshold = alreadyActive ? RecoveryThreshold ?? Threshold : Threshold;
        bool? triggered = known ? average >= threshold : null;
        var level = known && CriticalThreshold is { } critical && average >= critical ? "Critical" : severity;
        return new(resourceId, triggered, level, average, coverage, latestObserved, known ? "Observed" : "TelemetryUnknown");
    }

    public static string Fingerprint(Guid ruleId, Guid? resourceId) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes($"{ruleId:D}:{resourceId?.ToString("D") ?? "global"}")));

    public static MonitoringAlertDetail? ReadDetail(AlertEvent item)
    {
        if (item.SummaryCode == "ALERT_BACKUPSTORAGECAPACITY" || item.DetailJson.Length > 2048) return null;
        try
        {
            var value = JsonSerializer.Deserialize<MonitoringAlertDetail>(item.DetailJson, JsonOptions);
            return value?.Reason is not null ? value : null;
        }
        catch (JsonException) { return null; }
    }

    public static IReadOnlyCollection<string> Apply(AlertEvent item, AlertObservation observation, int occurrenceWindowSeconds, DateTimeOffset now)
    {
        var history = new List<string>();
        item.DetailJson = JsonSerializer.Serialize(observation with { Value = observation.Value is { } value && double.IsFinite(value) ? value : null },
            JsonOptions);
        if (observation.Triggered == false)
        {
            item.State = "Recovered"; item.RecoveredAtUtc = now; item.LastOccurredAtUtc = now; item.SilencedUntilUtc = null;
            history.Add("Recovered");
            return history;
        }
        if (observation.Triggered == true && item.Severity != observation.Severity)
        {
            var escalated = Rank(observation.Severity) > Rank(item.Severity);
            item.Severity = observation.Severity;
            history.Add("SeverityChanged");
            if (escalated && item.State == "Acknowledged") Reactivate(item);
        }
        // Silence is a notification disposition, not a claim that the risk disappeared.
        if (item.State == "Silenced" && (item.SilencedUntilUtc is null || item.SilencedUntilUtc <= now))
        {
            Reactivate(item); item.SilencedUntilUtc = null; history.Add("SilenceExpired");
        }
        if (observation.Triggered == true && item.LastOccurredAtUtc <= now.AddSeconds(-occurrenceWindowSeconds))
        {
            item.LastOccurredAtUtc = now;
            if (item.OccurrenceCount < int.MaxValue) item.OccurrenceCount++;
        }
        return history;
    }

    private static void Reactivate(AlertEvent item)
    {
        item.State = "Active"; item.AcknowledgedAtUtc = null; item.AcknowledgedBySubject = null;
    }
    private static int Rank(string severity) => severity switch { "Critical" => 2, "Warning" => 1, _ => 0 };
    private static JsonDocument ReadObject(string value)
    {
        if (value.Length > 4096) throw new ArgumentException("Alert JSON exceeds its budget.");
        JsonDocument json;
        try { json = JsonDocument.Parse(value, new JsonDocumentOptions { MaxDepth = 8 }); }
        catch (JsonException) { throw new ArgumentException("Alert configuration JSON is invalid."); }
        if (json.RootElement.ValueKind == JsonValueKind.Object) return json;
        json.Dispose(); throw new ArgumentException("Alert configuration must be a JSON object.");
    }
    private static void CheckKeys(JsonElement root, string[] allowed)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
            if (!allowed.Contains(property.Name, StringComparer.Ordinal) || !keys.Add(property.Name))
                throw new ArgumentException("Unknown or duplicate alert configuration field.");
    }
    private static string[] ReadIds(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var ids)) return [];
        if (ids.ValueKind != JsonValueKind.Array || ids.GetArrayLength() is < 1 or > 100)
            throw new ArgumentException("Alert selectors require 1–100 explicit identifiers.");
        return ids.EnumerateArray().Select(value => value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 and <= 256 } id &&
            !string.IsNullOrWhiteSpace(id) && !id.Any(char.IsControl) ? id : throw new ArgumentException("Invalid alert selector identifier.")).Distinct(StringComparer.Ordinal).ToArray();
    }
    private static double? ReadNumber(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value)) return null;
        return value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number)
            ? number : throw new ArgumentException("Alert threshold must be finite and numeric.");
    }
}

public sealed record AlertObservation(Guid? ResourceId, bool? Triggered, string Severity, double? Value = null,
    double? CoveragePercent = null, DateTimeOffset? ObservedAtUtc = null, string Reason = "Observed");
