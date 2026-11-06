using System.Text.Json;
using System.Text.Json.Serialization;

namespace WhaleDeck.Api.Controllers;

public sealed class ContainerRecycleArchivePlanBodyConverter : JsonConverter<ContainerRecycleArchivePlanBody>
{
    public override bool HandleNull => true;
    public override ContainerRecycleArchivePlanBody Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document=JsonDocument.ParseValue(ref reader);var root=document.RootElement;ContainerAclInput.Fields(root,["sourceIntentId"]);
        var text=ContainerAclInput.Text(root,"sourceIntentId");
        if(!Guid.TryParseExact(text,"D",out var id)||id==Guid.Empty||id.ToString("D")!=text)throw new JsonException("Canonical recycle source intent required.");
        return new(id);
    }
    public override void Write(Utf8JsonWriter writer, ContainerRecycleArchivePlanBody value, JsonSerializerOptions options)
    {writer.WriteStartObject();writer.WriteString("sourceIntentId",value.SourceIntentId.ToString("D"));writer.WriteEndObject();}
}
