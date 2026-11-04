using System.Text.Json;
using System.Text.Json.Serialization;

namespace WhaleDeck.Api.Controllers;

public sealed class ContainerRecursiveAclRestorePlanBodyConverter : JsonConverter<ContainerRecursiveAclRestorePlanBody>
{
    public override bool HandleNull => true;
    public override ContainerRecursiveAclRestorePlanBody Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader); var root = document.RootElement;
        ContainerAclInput.Fields(root, ["instanceId", "path", "captureId", "expectedHistoryDigest", "expectedVersion"]);
        return new(ContainerAclInput.Text(root, "instanceId"), ContainerAclInput.Text(root, "path"), ContainerAclInput.Text(root, "captureId"),
            ContainerAclInput.Text(root, "expectedHistoryDigest"), ContainerAclInput.Text(root, "expectedVersion"));
    }
    public override void Write(Utf8JsonWriter writer, ContainerRecursiveAclRestorePlanBody value, JsonSerializerOptions options)
    {
        writer.WriteStartObject(); writer.WriteString("instanceId", value.InstanceId); writer.WriteString("path", value.Path); writer.WriteString("captureId", value.CaptureId);
        writer.WriteString("expectedHistoryDigest", value.ExpectedHistoryDigest); writer.WriteString("expectedVersion", value.ExpectedVersion); writer.WriteEndObject();
    }
}
