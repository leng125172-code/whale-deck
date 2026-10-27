using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Services;

public static class ContainerFileRestoreValidation
{
    public static readonly JsonSerializerOptions Json = new() { MaxDepth = 8, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private static readonly HashSet<string> Fields = ["CaptureId", "ContainerId", "OriginalInstanceId", "Path", "SourceVersion", "SizeBytes", "Sha256", "CreatedAtUnixMs", "DifferentSourceDevice", "Availability", "CaptureHash"];
    public static string Snapshot(ContainerHistoryEntryDto source)
    {
        ContainerFileHistoryValidation.Entry(source);
        if (source.Availability != "Present") throw new InvalidOperationException("CONTAINER_HISTORY_SOURCE_UNAVAILABLE");
        var result = JsonSerializer.Serialize(source, Json);
        if (Encoding.UTF8.GetByteCount(result) > 32768) throw new ArgumentException("CONTAINER_HISTORY_SOURCE_INVALID");
        return result;
    }
    public static ContainerHistoryEntryDto Read(string value)
    {
        if (Encoding.UTF8.GetByteCount(value) > 32768) throw new ArgumentException("CONTAINER_HISTORY_SOURCE_INVALID");
        try
        {
            using var document = JsonDocument.Parse(value, new() { MaxDepth = 8 });
            Exact(document.RootElement, Fields);
            var source = document.RootElement.Deserialize<ContainerHistoryEntryDto>(Json) ?? throw Invalid();
            if (Snapshot(source) != value) throw Invalid();
            return source;
        }
        catch (Exception error) when (error is JsonException or FormatException or InvalidOperationException or KeyNotFoundException)
        { throw Invalid(); }
    }
    public static void Claims(ContainerFileRestorePlanClaims claims)
    {
        if (claims is null || claims.Source is null || claims.Target is null) throw Invalid();
        ContainerFileUploadIntentValidation.Claims(new(claims.Actor, claims.LoginBinding, claims.Target, claims.Source.SizeBytes,
            claims.Source.Sha256, claims.CreatedAtUtc, claims.ExpiresAtUtc));
        ContainerFileHistoryValidation.Source(claims.Target.Location, claims.Source);
        _ = Snapshot(claims.Source);
    }
    public static void Exact(JsonElement value, IReadOnlySet<string> fields)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Invalid();
        var names = value.EnumerateObject().Select(item => item.Name).ToArray();
        if (names.Length != fields.Count || !names.ToHashSet(StringComparer.Ordinal).SetEquals(fields)) throw Invalid();
    }
    public static ArgumentException Invalid() => new("CONTAINER_HISTORY_SOURCE_INVALID");
}
