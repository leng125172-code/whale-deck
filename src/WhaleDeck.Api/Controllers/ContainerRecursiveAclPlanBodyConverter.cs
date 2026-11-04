using System.Text.Json;
using System.Text.Json.Serialization;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Api.Controllers;

/// <summary>Explicit presence for both targets; null files/default and empty directories/clear are distinct.</summary>
public sealed class ContainerRecursiveAclPlanBodyConverter : JsonConverter<ContainerRecursiveAclPlanBody>
{
    public override bool HandleNull => true;
    public override ContainerRecursiveAclPlanBody Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader); var root = document.RootElement;
        ContainerAclInput.Fields(root, ["instanceId", "path", "aclType", "fileEntries", "directoryEntries", "expectedVersion"]);
        var kind = ContainerAclInput.Text(root, "aclType");
        if (kind is not ("access" or "default")) throw ContainerAclInput.Invalid();
        var raw = root.GetProperty("fileEntries");
        IReadOnlyList<ContainerAclEntryDto>? files;
        if (kind == "default") { if (raw.ValueKind != JsonValueKind.Null) throw ContainerAclInput.Invalid(); files = null; }
        else files = Entries(raw, false);
        return new(ContainerAclInput.Text(root, "instanceId"), ContainerAclInput.Text(root, "path"), kind, files,
            Entries(root.GetProperty("directoryEntries"), kind == "default"), ContainerAclInput.Text(root, "expectedVersion"));
    }
    private static ContainerAclEntryDto[] Entries(JsonElement raw, bool empty)
    {
        if (raw.ValueKind != JsonValueKind.Array || raw.GetArrayLength() > 64) throw ContainerAclInput.Invalid();
        var entries = new List<ContainerAclEntryDto>();
        foreach (var entry in raw.EnumerateArray())
        {
            ContainerAclInput.Fields(entry, ["tag", "id", "permissions"]);
            var tag = ContainerAclInput.Text(entry, "tag"); var rawId = entry.GetProperty("id"); uint? id = null;
            if (tag is "user" or "group")
            {
                if (rawId.ValueKind != JsonValueKind.Number || !rawId.TryGetUInt32(out var number) || number == uint.MaxValue) throw ContainerAclInput.Invalid();
                id = number;
            }
            else if (rawId.ValueKind != JsonValueKind.Null) throw ContainerAclInput.Invalid();
            var bits = entry.GetProperty("permissions");
            if (bits.ValueKind != JsonValueKind.Number || !bits.TryGetUInt32(out var permissions) || permissions > 7) throw ContainerAclInput.Invalid();
            entries.Add(new(tag, id, permissions));
        }
        try { ContainerAclValidation.Entries(entries, empty); } catch (ArgumentException) { throw ContainerAclInput.Invalid(); }
        return entries.ToArray();
    }
    public override void Write(Utf8JsonWriter writer, ContainerRecursiveAclPlanBody value, JsonSerializerOptions options)
    {
        if (value is null) throw ContainerAclInput.Invalid();
        writer.WriteStartObject(); writer.WriteString("instanceId", value.InstanceId); writer.WriteString("path", value.Path); writer.WriteString("aclType", value.AclType);
        writer.WritePropertyName("fileEntries"); JsonSerializer.Serialize(writer, value.FileEntries, options);
        writer.WritePropertyName("directoryEntries"); JsonSerializer.Serialize(writer, value.DirectoryEntries, options);
        writer.WriteString("expectedVersion", value.ExpectedVersion); writer.WriteEndObject();
    }
}
