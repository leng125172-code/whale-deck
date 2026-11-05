using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using WhaleDeck.Api.Controllers;
namespace WhaleDeck.IntegrationTests;
public sealed class ContainerRecycleHttpTests
{
    private const string Body="""{"instanceId":"777:2026-11-03T01:02:03Z","path":"work/tree","expectedVersion":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"}""";
    [Theory][InlineData(typeof(ContainerRecyclesController))][InlineData(typeof(ContainerRecycleRecoveryController))]
    public void EverySurfaceRequiresAdministratorAndNoStore(Type type)
    {
        Assert.Contains(type.GetCustomAttributes(typeof(AuthorizeAttribute),true),v=>((AuthorizeAttribute)v).Policy=="Administrator");
        Assert.Contains(type.GetCustomAttributes(typeof(ResponseCacheAttribute),true),v=>((ResponseCacheAttribute)v).NoStore);
    }
    [Theory][InlineData("instanceId")][InlineData("path")][InlineData("expectedVersion")]
    public void MissingFileFieldsCannotDefault(string field)
    {var node=JsonNode.Parse(Body)!.AsObject();node.Remove(field);Assert.Throws<JsonException>(()=>Read(node.ToJsonString()));}
    [Theory][InlineData("captureId")][InlineData("CaptureId")][InlineData("expectedVersion")][InlineData("aclType")][InlineData("fileEntries")][InlineData("directoryEntries")][InlineData("uid")][InlineData("mode")]
    public void DuplicateOrForeignPropertiesAreRejected(string field)=>Assert.Throws<JsonException>(()=>Read(Body[..^1]+",\""+field+"\":0}"));
    [Theory][InlineData("null")][InlineData("[]")][InlineData("false")][InlineData("{\"captureId\":null}")]
    public void AmbiguousBodiesCannotSelectASource(string value)=>Assert.Throws<JsonException>(()=>Read(value));
    [Fact]
    public void ValidSourceRoundTripsExactlyWithoutClientSuppliedPlanOrAttributes()=>Assert.Equal(Body,JsonSerializer.Serialize(Read(Body),JsonSerializerOptions.Web));
    [Theory][InlineData("{\"planHash\":\"x\",\"confirmed\":true}")][InlineData("{\"planHash\":\"x\",\"confirmed\":true,\"confirmed\":false,\"version\":1}")][InlineData("{\"planHash\":\"x\",\"confirmed\":true,\"version\":1.0}")][InlineData("{\"planHash\":\"x\",\"confirmed\":true,\"version\":1,\"captureId\":\"x\"}")]
    public void ConfirmationCannotReplaceSourceOrCoerceVersion(string value)=>Assert.Throws<JsonException>(()=>JsonSerializer.Deserialize<ConfirmContainerRecycleBody>(value,JsonSerializerOptions.Web));
    [Theory][InlineData("{\"version\":0}")][InlineData("{\"version\":0,\"confirmed\":true,\"confirmed\":false}")][InlineData("{\"version\":-1,\"confirmed\":true}")]
    public void AcknowledgementMustBeExplicit(string value)=>Assert.Throws<JsonException>(()=>JsonSerializer.Deserialize<AcknowledgeContainerRecycleBody>(value,JsonSerializerOptions.Web));
    [Fact]
    public async Task InvalidKeysFailBeforeAnyIdentityOrBusinessCalls()
    {
        var controller=new ContainerRecyclesController(null!,null!){ControllerContext=new(){HttpContext=new DefaultHttpContext()}};
        var recovery=new ContainerRecycleRecoveryController(null!,null!){ControllerContext=new(){HttpContext=new DefaultHttpContext()}};
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
    private static ContainerRecyclePlanBody? Read(string raw)=>JsonSerializer.Deserialize<ContainerRecyclePlanBody>(raw,JsonSerializerOptions.Web);
}
