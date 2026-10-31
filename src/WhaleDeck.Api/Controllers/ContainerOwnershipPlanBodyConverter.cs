using System.Text.Json;
using System.Text.Json.Serialization;

namespace WhaleDeck.Api.Controllers;

/// <summary>Transport-only presence/type/duplicate checks. Zero is an explicit owner, never a missing-field default.</summary>
public sealed class ContainerOwnershipPlanBodyConverter : JsonConverter<ContainerOwnershipPlanBody>
{
    public override bool HandleNull => true;
    public override ContainerOwnershipPlanBody Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw Invalid();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in root.EnumerateObject())
        {
            if (!seen.Add(field.Name) || field.Name is not ("instanceId" or "path" or "uid" or "gid" or "expectedVersion")) throw Invalid();
        }
        if (seen.Count != 5) throw Invalid();
        var instance = Text(root, "instanceId"); var path = Text(root, "path"); var expected = Text(root, "expectedVersion");
        return new(instance, path, Owner(root, "uid"), Owner(root, "gid"), expected);
    }
    private static string Text(JsonElement root, string name) => root.GetProperty(name).ValueKind == JsonValueKind.String
        ? root.GetProperty(name).GetString() ?? throw Invalid() : throw Invalid();
    private static uint Owner(JsonElement root, string name)
    {
        var value = root.GetProperty(name);
        return value.ValueKind == JsonValueKind.Number && value.TryGetUInt32(out var owner) && owner != uint.MaxValue ? owner : throw Invalid();
    }
    public override void Write(Utf8JsonWriter writer, ContainerOwnershipPlanBody value, JsonSerializerOptions options)
    {
        if (value is null || value.Uid is null || value.Gid is null || value.Uid == uint.MaxValue || value.Gid == uint.MaxValue) throw Invalid();
        writer.WriteStartObject(); writer.WriteString("instanceId", value.InstanceId); writer.WriteString("path", value.Path);
        writer.WriteNumber("uid", value.Uid.Value); writer.WriteNumber("gid", value.Gid.Value); writer.WriteString("expectedVersion", value.ExpectedVersion); writer.WriteEndObject();
    }
    private static JsonException Invalid() => new("Explicit canonical container ownership fields are required.");
}
