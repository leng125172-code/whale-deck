using System.Text.Json;
using System.Text.Json.Serialization;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Api.Controllers;

/// <summary>Strict transport presence/duplicates/type checks; numerical zero is never an omitted value.</summary>
public sealed class ContainerAclPlanBodyConverter : JsonConverter<ContainerAclPlanBody>
{
    public override bool HandleNull => true;
    public override ContainerAclPlanBody Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader); var root = document.RootElement;
        ContainerAclInput.Fields(root, ["instanceId", "path", "aclType", "entries", "expectedVersion"]);
        var type = ContainerAclInput.Text(root, "aclType"); var raw = root.GetProperty("entries");
        if (type is not ("access" or "default") || raw.ValueKind != JsonValueKind.Array || raw.GetArrayLength() > 64) throw ContainerAclInput.Invalid();
        var entries = new List<ContainerAclEntryDto>();
        foreach (var entry in raw.EnumerateArray())
        {
            ContainerAclInput.Fields(entry, ["tag", "id", "permissions"]);
            var tag = ContainerAclInput.Text(entry, "tag"); var id = entry.GetProperty("id");
            uint? value = null;
            if (tag is "user" or "group")
            {
                if (id.ValueKind != JsonValueKind.Number || !id.TryGetUInt32(out var number) || number == uint.MaxValue) throw ContainerAclInput.Invalid();
                value = number;
            }
            else if (id.ValueKind != JsonValueKind.Null) throw ContainerAclInput.Invalid();
            var bits = entry.GetProperty("permissions");
            if (bits.ValueKind != JsonValueKind.Number || !bits.TryGetUInt32(out var permissions) || permissions > 7) throw ContainerAclInput.Invalid();
            entries.Add(new(tag, value, permissions));
        }
        try { ContainerAclValidation.Entries(entries, type == "default"); }
        catch (ArgumentException) { throw ContainerAclInput.Invalid(); }
        return new(ContainerAclInput.Text(root,"instanceId"),ContainerAclInput.Text(root,"path"),type,entries.ToArray(),ContainerAclInput.Text(root,"expectedVersion"));
    }
    public override void Write(Utf8JsonWriter writer, ContainerAclPlanBody value, JsonSerializerOptions options)
    {
        if (value is null) throw ContainerAclInput.Invalid();
        writer.WriteStartObject(); writer.WriteString("instanceId",value.InstanceId); writer.WriteString("path",value.Path); writer.WriteString("aclType",value.AclType);
        writer.WritePropertyName("entries"); JsonSerializer.Serialize(writer,value.Entries,options); writer.WriteString("expectedVersion",value.ExpectedVersion);writer.WriteEndObject();
    }
}
public sealed class ContainerAclConfirmBodyConverter : JsonConverter<ContainerAclConfirmBody>
{
    public override bool HandleNull => true;
    public override ContainerAclConfirmBody Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document=JsonDocument.ParseValue(ref reader);var root=document.RootElement;
        ContainerAclInput.Fields(root,["planHash","confirmed","version"]);
        return new(ContainerAclInput.Text(root,"planHash"),ContainerAclInput.Confirmed(root),ContainerAclInput.Version(root));
    }
    public override void Write(Utf8JsonWriter writer,ContainerAclConfirmBody value,JsonSerializerOptions options)
    { writer.WriteStartObject();writer.WriteString("planHash",value.PlanHash);writer.WriteBoolean("confirmed",value.Confirmed);writer.WriteNumber("version",value.Version);writer.WriteEndObject(); }
}
public sealed class ContainerAclAcknowledgeBodyConverter : JsonConverter<ContainerAclAcknowledgeBody>
{
    public override bool HandleNull => true;
    public override ContainerAclAcknowledgeBody Read(ref Utf8JsonReader reader,Type typeToConvert,JsonSerializerOptions options)
    {
        using var document=JsonDocument.ParseValue(ref reader);var root=document.RootElement;
        ContainerAclInput.Fields(root,["version","confirmed"]);return new(ContainerAclInput.Version(root),ContainerAclInput.Confirmed(root));
    }
    public override void Write(Utf8JsonWriter writer,ContainerAclAcknowledgeBody value,JsonSerializerOptions options)
    { writer.WriteStartObject();writer.WriteNumber("version",value.Version);writer.WriteBoolean("confirmed",value.Confirmed);writer.WriteEndObject(); }
}
internal static class ContainerAclInput
{
    internal static void Fields(JsonElement root,string[] names)
    {
        if(root.ValueKind!=JsonValueKind.Object) throw Invalid(); var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var field in root.EnumerateObject()) if(!seen.Add(field.Name)||!names.Contains(field.Name,StringComparer.Ordinal)) throw Invalid();
        if(seen.Count!=names.Length) throw Invalid();
    }
    internal static string Text(JsonElement root,string name)=>root.GetProperty(name).ValueKind==JsonValueKind.String?root.GetProperty(name).GetString()??throw Invalid():throw Invalid();
    internal static bool Confirmed(JsonElement root)=>root.GetProperty("confirmed").ValueKind switch {JsonValueKind.True=>true,JsonValueKind.False=>false,_=>throw Invalid()};
    internal static long Version(JsonElement root)=>root.GetProperty("version").ValueKind==JsonValueKind.Number&&root.GetProperty("version").TryGetInt64(out var value)&&value>=0?value:throw Invalid();
    internal static JsonException Invalid()=>new("Explicit canonical ACL fields are required.");
}
