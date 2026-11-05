using System.Text.Json;
using System.Text.Json.Serialization;

namespace WhaleDeck.Api.Controllers;

public sealed class ContainerRecycleRestorePlanBodyConverter : JsonConverter<ContainerRecycleRestorePlanBody>
{
    public override bool HandleNull => true;
    public override ContainerRecycleRestorePlanBody Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document=JsonDocument.ParseValue(ref reader);var root=document.RootElement;ContainerAclInput.Fields(root,["sourceIntentId"]);
        var text=ContainerAclInput.Text(root,"sourceIntentId");
        if(!Guid.TryParseExact(text,"D",out var id)||id==Guid.Empty||id.ToString("D")!=text)throw new JsonException("Canonical recycle source intent required.");
        return new(id);
    }
    public override void Write(Utf8JsonWriter writer, ContainerRecycleRestorePlanBody value, JsonSerializerOptions options)
    {writer.WriteStartObject();writer.WriteString("sourceIntentId",value.SourceIntentId.ToString("D"));writer.WriteEndObject();}
}
