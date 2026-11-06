using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using WhaleDeck.Api.Controllers;
namespace WhaleDeck.IntegrationTests;
public sealed class ContainerRecyclePurgeHttpTests
{
    private const string Body="""{"sourceIntentId":"a26533e0-029e-48d8-88b8-8273058b370d"}""";
    [Theory][InlineData(typeof(ContainerRecyclePurgesController))][InlineData(typeof(ContainerRecyclePurgeRecoveryController))]
    public void EverySurfaceRequiresAdministratorAndNoStore(Type type)
    {
        Assert.Contains(type.GetCustomAttributes(typeof(AuthorizeAttribute),true),v=>((AuthorizeAttribute)v).Policy=="Administrator");
        Assert.Contains(type.GetCustomAttributes(typeof(ResponseCacheAttribute),true),v=>((ResponseCacheAttribute)v).NoStore);
    }
    [Theory][InlineData("sourceIntentId")]
    public void MissingFileFieldsCannotDefault(string field)
    {var node=JsonNode.Parse(Body)!.AsObject();node.Remove(field);Assert.Throws<JsonException>(()=>Read(node.ToJsonString()));}
    [Theory][InlineData("sourceIntentId")][InlineData("SourceIntentId")][InlineData("owner")][InlineData("plan")][InlineData("source")][InlineData("historyDigest")][InlineData("path")][InlineData("instanceId")][InlineData("sourceOperationId")]
    public void DuplicateOrForeignPropertiesAreRejected(string field)=>Assert.Throws<JsonException>(()=>Read(Body[..^1]+",\""+field+"\":0}"));
    [Theory][InlineData("null")][InlineData("[]")][InlineData("false")][InlineData("{\"captureId\":null}")]
    public void AmbiguousBodiesCannotSelectASource(string value)=>Assert.Throws<JsonException>(()=>Read(value));
    [Fact]
    public void ValidSourceRoundTripsExactlyWithoutClientSuppliedPlanOrAttributes()=>Assert.Equal(Body,JsonSerializer.Serialize(Read(Body),JsonSerializerOptions.Web));
    [Theory][InlineData("{\"planHash\":\"x\",\"confirmed\":true}")][InlineData("{\"planHash\":\"x\",\"confirmed\":true,\"confirmed\":false,\"version\":1}")][InlineData("{\"planHash\":\"x\",\"confirmed\":true,\"version\":1.0}")][InlineData("{\"planHash\":\"x\",\"confirmed\":true,\"version\":1,\"captureId\":\"x\"}")]
    public void ConfirmationCannotReplaceSourceOrCoerceVersion(string value)=>Assert.Throws<JsonException>(()=>JsonSerializer.Deserialize<ConfirmContainerRecyclePurgeBody>(value,JsonSerializerOptions.Web));
    [Theory][InlineData("{\"version\":0}")][InlineData("{\"version\":0,\"confirmed\":true,\"confirmed\":false}")][InlineData("{\"version\":-1,\"confirmed\":true}")]
    public void AcknowledgementMustBeExplicit(string value)=>Assert.Throws<JsonException>(()=>JsonSerializer.Deserialize<AcknowledgeContainerRecyclePurgeBody>(value,JsonSerializerOptions.Web));
    [Fact]
    public async Task InvalidKeysFailBeforeAnyIdentityOrBusinessCalls()
    {
        var controller=new ContainerRecyclePurgesController(null!,null!,null!){ControllerContext=new(){HttpContext=new DefaultHttpContext()}};
        var recovery=new ContainerRecyclePurgeRecoveryController(null!,null!){ControllerContext=new(){HttpContext=new DefaultHttpContext()}};
        var id=Guid.Parse("a26533e0-029e-48d8-88b8-8273058b370d");var cid=new string('a',64);
        foreach(var key in new[]{"",Guid.Empty.ToString("D"),id.ToString("D").ToUpperInvariant(),id.ToString("N")})
        {
            controller.Request.Headers["Idempotency-Key"]=key;recovery.Request.Headers["Idempotency-Key"]=key;
            Assert.IsType<BadRequestObjectResult>((await controller.Queue(cid,null!,default)).Result);
            Assert.IsType<BadRequestObjectResult>((await controller.Confirm(cid,id,null!,default)).Result);
            Assert.IsType<BadRequestObjectResult>((await controller.Cancel(cid,id,default)).Result);
            Assert.IsType<BadRequestObjectResult>(await recovery.Acknowledge(id,null!,default));
        }
    }
    private static ContainerRecyclePurgePlanBody? Read(string raw)=>JsonSerializer.Deserialize<ContainerRecyclePurgePlanBody>(raw,JsonSerializerOptions.Web);
    [Theory][InlineData("00000000-0000-0000-0000-000000000000")][InlineData("A26533E0-029E-48D8-88B8-8273058B370D")][InlineData("a26533e0029e48d888b88273058b370d")][InlineData("null")]
    public void SourceReferenceMustBeCanonicalNonemptyGuid(string value)=>Assert.Throws<JsonException>(()=>Read("{\"sourceIntentId\":\""+value+"\"}"));
    [Theory][InlineData("?take=25&after=a26533e0-029e-48d8-88b8-8273058b370d",true)][InlineData("?take=025",false)][InlineData("?take=1&take=2",false)][InlineData("?after=00000000-0000-0000-0000-000000000000",false)][InlineData("?owner=hidden",false)][InlineData("?after=A26533E0-029E-48D8-88B8-8273058B370D",false)]
    public void SourcePagingRejectsUnknownDuplicateAndNoncanonicalQueries(string query,bool valid)
    {
        var http=new DefaultHttpContext();http.Request.QueryString=new(query);
        var action=new ActionContext(http,new Microsoft.AspNetCore.Routing.RouteData(),new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor(),new Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary());
        var context=new Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext(action,[],new Dictionary<string,object?>(),new object());
        new ContainerRecyclePurgeQueryAttribute{AllowTake=true,AllowAfter=true}.OnActionExecuting(context);
        Assert.Equal(valid,context.Result is null);
    }
}
