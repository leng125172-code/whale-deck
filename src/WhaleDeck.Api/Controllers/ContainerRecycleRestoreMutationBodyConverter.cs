using System.Text.Json;
using System.Text.Json.Serialization;

namespace WhaleDeck.Api.Controllers;

[JsonConverter(typeof(ContainerRecycleRestoreMutationBodyConverter))]
public sealed record ConfirmContainerRecycleRestoreBody(string PlanHash, bool Confirmed, long Version);
[JsonConverter(typeof(ContainerRecycleRestoreMutationBodyConverter))]
public sealed record AcknowledgeContainerRecycleRestoreBody(long Version, bool Confirmed);

/// <summary>Require explicit canonical confirmation fields; reject duplicate, missing, extra and coerced values.</summary>
public sealed class ContainerRecycleRestoreMutationBodyConverter : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(ConfirmContainerRecycleRestoreBody) || typeToConvert == typeof(AcknowledgeContainerRecycleRestoreBody);
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) => typeToConvert == typeof(ConfirmContainerRecycleRestoreBody) ? new ConfirmConverter() : new AcknowledgeConverter();
    private static JsonDocument Read(ref Utf8JsonReader reader, bool confirm)
    {
        var doc = JsonDocument.ParseValue(ref reader);
        try
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object) throw Invalid();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in doc.RootElement.EnumerateObject())
                if (!seen.Add(field.Name) || field.Name is not ("version" or "confirmed") && (!confirm || field.Name != "planHash")) throw Invalid();
            if (seen.Count != (confirm ? 3 : 2) || doc.RootElement.GetProperty("confirmed").ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                doc.RootElement.GetProperty("version").ValueKind != JsonValueKind.Number || !doc.RootElement.GetProperty("version").TryGetInt64(out var version) || version < 0) throw Invalid();
            if (confirm && (doc.RootElement.GetProperty("planHash").ValueKind != JsonValueKind.String || doc.RootElement.GetProperty("planHash").GetString() is not { Length: > 0 and <= 4096 })) throw Invalid();
            return doc;
        }
        catch { doc.Dispose(); throw; }
    }
    private static JsonException Invalid() => new("Explicit canonical recycle confirmation fields are required.");
    private sealed class ConfirmConverter : JsonConverter<ConfirmContainerRecycleRestoreBody>
    {
        public override bool HandleNull => true;
        public override ConfirmContainerRecycleRestoreBody Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        { using var doc = ContainerRecycleRestoreMutationBodyConverter.Read(ref reader, true); var root = doc.RootElement; return new(root.GetProperty("planHash").GetString()!, root.GetProperty("confirmed").GetBoolean(), root.GetProperty("version").GetInt64()); }
        public override void Write(Utf8JsonWriter writer, ConfirmContainerRecycleRestoreBody value, JsonSerializerOptions options)
        { writer.WriteStartObject(); writer.WriteString("planHash", value.PlanHash); writer.WriteBoolean("confirmed", value.Confirmed); writer.WriteNumber("version", value.Version); writer.WriteEndObject(); }
    }
    private sealed class AcknowledgeConverter : JsonConverter<AcknowledgeContainerRecycleRestoreBody>
    {
        public override bool HandleNull => true;
        public override AcknowledgeContainerRecycleRestoreBody Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        { using var doc = ContainerRecycleRestoreMutationBodyConverter.Read(ref reader, false); var root = doc.RootElement; return new(root.GetProperty("version").GetInt64(), root.GetProperty("confirmed").GetBoolean()); }
        public override void Write(Utf8JsonWriter writer, AcknowledgeContainerRecycleRestoreBody value, JsonSerializerOptions options)
        { writer.WriteStartObject(); writer.WriteNumber("version", value.Version); writer.WriteBoolean("confirmed", value.Confirmed); writer.WriteEndObject(); }
    }
}
