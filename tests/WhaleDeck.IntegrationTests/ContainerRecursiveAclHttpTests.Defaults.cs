using System.Text.Json;
using System.Text.Json.Nodes;
using WhaleDeck.Api.Controllers;

namespace WhaleDeck.IntegrationTests;
public sealed partial class ContainerRecursiveAclHttpTests
{
    [Theory][InlineData(false)][InlineData(true)]
    public void DefaultHasExplicitNullFilesAndExplicitDirectoryClear(bool clear)
    {
        var node=JsonNode.Parse(Body)!.AsObject();node["aclType"]="default";node["fileEntries"]=null;
        if(clear)node["directoryEntries"]=new JsonArray();
        var value=Read(node.ToJsonString())!;Assert.Null(value.FileEntries);Assert.Equal(clear?0:5,value.DirectoryEntries.Count);
        var roundTrip=Read(JsonSerializer.Serialize(value,JsonSerializerOptions.Web))!;Assert.Null(roundTrip.FileEntries);Assert.Equal(value.DirectoryEntries,roundTrip.DirectoryEntries);
    }
    [Theory][InlineData("[]")][InlineData("{}")][InlineData("false")]
    public void DefaultFileWrapperCannotBeSupplied(string invalid)
    {var node=JsonNode.Parse(Body)!.AsObject();node["aclType"]="default";node["fileEntries"]=JsonNode.Parse(invalid);Assert.Throws<JsonException>(()=>Read(node.ToJsonString()));}
    [Theory][InlineData("fileEntries")][InlineData("directoryEntries")]
    public void AccessCannotTurnNullOrEmptyTargetsIntoDefaults(string field)
    {
        var node=JsonNode.Parse(Body)!.AsObject();node[field]=null;Assert.Throws<JsonException>(()=>Read(node.ToJsonString()));
        node[field]=new JsonArray();Assert.Throws<JsonException>(()=>Read(node.ToJsonString()));
    }
    [Theory][InlineData("fileEntries")][InlineData("directoryEntries")]
    public void BothTargetsRejectMissingNamedIdentityMaskAndDuplicateProperties(string field)
    {
        foreach(var mutation in new[]{"id","permissions","mask","duplicate"})
        {
            var node=JsonNode.Parse(Body)!.AsObject();var entries=node[field]!.AsArray();
            if(mutation=="mask")entries.RemoveAt(3);
            else if(mutation=="duplicate")entries.Add(entries[^1]!.DeepClone());
            else entries[1]!.AsObject().Remove(mutation);
            Assert.Throws<JsonException>(()=>Read(node.ToJsonString()));
        }
    }
    [Theory][InlineData(typeof(ConfirmContainerRecursiveAclBody))][InlineData(typeof(AcknowledgeContainerRecursiveAclBody))]
    public void ConfirmationRejectsMissingDuplicateOrCoercedFields(Type type)
    {
        foreach(var raw in new[]{"null","{}","{\"version\":0,\"confirmed\":true,\"confirmed\":true}","{\"version\":\"0\",\"confirmed\":true}","{\"version\":0,\"confirmed\":1}"})
            Assert.Throws<JsonException>(()=>JsonSerializer.Deserialize(raw,type,JsonSerializerOptions.Web));
    }
}
