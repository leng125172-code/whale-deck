using System.Text.Json;
using System.Text.Json.Serialization;

namespace WhaleDeck.Api.Controllers;

/// <summary>Exact transport fields: both ordinary modes are explicit strings, never defaults or coercions.</summary>
public sealed class ContainerRecursivePermissionPlanBodyConverter : JsonConverter<ContainerRecursivePermissionPlanBody>
{
    public override bool HandleNull => true;
    public override ContainerRecursivePermissionPlanBody Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw Invalid();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in root.EnumerateObject())
            if (!seen.Add(field.Name) || field.Name is not ("instanceId" or "path" or "fileMode" or "directoryMode" or "expectedVersion")) throw Invalid();
        if (seen.Count != 5) throw Invalid();
        var file = Text(root, "fileMode"); var directory = Text(root, "directoryMode");
        if (!ContainerRecursivePermissionHttp.Mode(file) || !ContainerRecursivePermissionHttp.Mode(directory)) throw Invalid();
        return new(Text(root, "instanceId"), Text(root, "path"), file, directory, Text(root, "expectedVersion"));
    }
    private static string Text(JsonElement root, string name) => root.GetProperty(name).ValueKind == JsonValueKind.String
        ? root.GetProperty(name).GetString() ?? throw Invalid() : throw Invalid();
    public override void Write(Utf8JsonWriter writer, ContainerRecursivePermissionPlanBody value, JsonSerializerOptions options)
    {
        if (value is null || !ContainerRecursivePermissionHttp.Mode(value.FileMode) || !ContainerRecursivePermissionHttp.Mode(value.DirectoryMode)) throw Invalid();
        writer.WriteStartObject(); writer.WriteString("instanceId", value.InstanceId); writer.WriteString("path", value.Path);
        writer.WriteString("fileMode", value.FileMode); writer.WriteString("directoryMode", value.DirectoryMode); writer.WriteString("expectedVersion", value.ExpectedVersion); writer.WriteEndObject();
    }
    private static JsonException Invalid() => new("Explicit canonical recursive permission fields are required.");
}
