using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using WhaleDeck.Api.Controllers;

namespace WhaleDeck.IntegrationTests;

public sealed class ContainerAclHttpTests
{
    private const string Body="""{"instanceId":"777:2026-11-03T01:02:03Z","path":"work/acl","aclType":"access","entries":[{"tag":"user_obj","id":null,"permissions":6},{"tag":"user","id":0,"permissions":0},{"tag":"group_obj","id":null,"permissions":4},{"tag":"mask","id":null,"permissions":6},{"tag":"other","id":null,"permissions":0}],"expectedVersion":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"}""";
    [Theory][InlineData(typeof(ContainerAclsController))][InlineData(typeof(ContainerAclRecoveryController))]
    public void AclSurfacesRequireAdministratorAndNeverCachePrivateMetadata(Type type)
    {
        Assert.Contains(type.GetCustomAttributes(typeof(AuthorizeAttribute),true),a=>((AuthorizeAttribute)a).Policy=="Administrator");
        Assert.Contains(type.GetCustomAttributes(typeof(ResponseCacheAttribute),true),a=>((ResponseCacheAttribute)a).NoStore);
    }
    [Theory][InlineData("instanceId")][InlineData("path")][InlineData("aclType")][InlineData("entries")][InlineData("expectedVersion")]
    public void MissingRootFieldsCannotDefaultOrSelectAnAclType(string field)
    {var root=JsonNode.Parse(Body)!.AsObject();root.Remove(field);Assert.Throws<JsonException>(()=>Read(root.ToJsonString()));}
    [Theory][InlineData("tag")][InlineData("id")][InlineData("permissions")]
    public void MissingEntryFieldsCannotDefaultToRootOrGrantPermission(string field)
    {var root=JsonNode.Parse(Body)!;root["entries"]![1]!.AsObject().Remove(field);Assert.Throws<JsonException>(()=>Read(root.ToJsonString()));}
    [Theory][InlineData("id","null")][InlineData("id","true")][InlineData("id","\"0\"")][InlineData("id","1e2")][InlineData("id","1.0")][InlineData("id","-1")][InlineData("id","4294967295")]
    [InlineData("permissions","null")][InlineData("permissions","true")][InlineData("permissions","\"0\"")][InlineData("permissions","1e0")][InlineData("permissions","1.0")][InlineData("permissions","-1")][InlineData("permissions","8")]
    public void EntryNumbersAreExplicitIntegersWithoutCoercion(string field,string literal)
    {var root=JsonNode.Parse(Body)!;root["entries"]![1]![field]=JsonNode.Parse(literal);Assert.Throws<JsonException>(()=>Read(root.ToJsonString()));}
    [Theory][InlineData("entries")][InlineData("Entries")][InlineData("recursive")][InlineData("owner")][InlineData("uid")][InlineData("mode")]
    public void DuplicateOrAdditionalRootPropertiesAreRejected(string field)=>Assert.Throws<JsonException>(()=>Read(Body[..^1]+",\""+field+"\":0}"));
    [Theory][InlineData("tag")][InlineData("id")][InlineData("permissions")][InlineData("Permissions")][InlineData("extra")]
    public void DuplicateOrExtraEntryPropertiesAreRejected(string field)
    {var value=Body.Replace("\"tag\":\"user\",", "\""+field+"\":0,\"tag\":\"user\",",StringComparison.Ordinal);Assert.Throws<JsonException>(()=>Read(value));}
    [Theory][InlineData("mask")][InlineData("other")][InlineData("group_obj")][InlineData("user_obj")]
    public void MissingBaseOrMaskEntriesCannotBeSilentlyAdded(string tag)
    {var root=JsonNode.Parse(Body)!;var entries=root["entries"]!.AsArray();entries.RemoveAt(entries.Select((e,i)=>(e,i)).Single(v=>v.e!["tag"]!.GetValue<string>()==tag).i);Assert.Throws<JsonException>(()=>Read(root.ToJsonString()));}
    [Theory][InlineData("null")][InlineData("[]")][InlineData("false")]
    public void BodyMustBeAnObject(string value)=>Assert.Throws<JsonException>(()=>Read(value));
    [Fact]
    public void ExplicitZeroAndDefaultClearRoundTripWithoutMissingValueDefaults()
    {
        var value=Read(Body)!;Assert.Equal(0U,value.Entries[1].Id);Assert.Equal(0U,value.Entries[1].Permissions);
        Assert.Equal(Body,JsonSerializer.Serialize(value,JsonSerializerOptions.Web));
        var root=JsonNode.Parse(Body)!;root["entries"]=new JsonArray();Assert.Throws<JsonException>(()=>Read(root.ToJsonString()));
        root["aclType"]="default";Assert.Empty(Read(root.ToJsonString())!.Entries);
    }
    [Theory][InlineData("{\"planHash\":\"x\",\"confirmed\":true}")][InlineData("{\"planHash\":\"x\",\"confirmed\":true,\"confirmed\":false,\"version\":1}")][InlineData("{\"planHash\":\"x\",\"confirmed\":true,\"version\":1,\"mode\":7}")][InlineData("{\"planHash\":\"x\",\"confirmed\":1,\"version\":1}")][InlineData("{\"planHash\":\"x\",\"confirmed\":true,\"version\":1.0}")]
    public void ConfirmationRejectsMissingDuplicatesExtrasOrNoncanonicalTypes(string raw)=>Assert.Throws<JsonException>(()=>JsonSerializer.Deserialize<ContainerAclConfirmBody>(raw,JsonSerializerOptions.Web));
    [Theory][InlineData("{\"confirmed\":true}")][InlineData("{\"confirmed\":true,\"version\":0,\"version\":1}")][InlineData("{\"confirmed\":true,\"version\":0,\"planHash\":\"x\"}")][InlineData("{\"confirmed\":null,\"version\":0}")]
    public void AcknowledgementRejectsAmbiguousFields(string raw)=>Assert.Throws<JsonException>(()=>JsonSerializer.Deserialize<ContainerAclAcknowledgeBody>(raw,JsonSerializerOptions.Web));
    [Fact]
    public void InputErrorDoesNotEchoPrivateJson()
    {
        var model=new Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary();model.AddModelError("entries","private-must-not-leak");
        var action=new ActionContext(new DefaultHttpContext(),new Microsoft.AspNetCore.Routing.RouteData(),new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor(),model);
        var context=new Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext(action,[],new Dictionary<string,object?>(),new object());new ContainerAclInputAttribute().OnActionExecuting(context);
        var problem=Assert.IsType<ProblemDetails>(Assert.IsType<BadRequestObjectResult>(context.Result).Value);Assert.Equal("CONTAINER_ACL_INPUT_INVALID",problem.Extensions["code"]);Assert.DoesNotContain("private-must-not-leak",JsonSerializer.Serialize(problem));
    }
    private static ContainerAclPlanBody? Read(string raw)=>JsonSerializer.Deserialize<ContainerAclPlanBody>(raw,JsonSerializerOptions.Web);
}
