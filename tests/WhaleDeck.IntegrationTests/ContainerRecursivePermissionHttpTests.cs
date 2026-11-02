using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using WhaleDeck.Api.Controllers;

namespace WhaleDeck.IntegrationTests;

public sealed class ContainerRecursivePermissionHttpTests
{
    private const string Body = """{"instanceId":"777:2026-10-30T01:02:03Z","path":"work/tree","fileMode":"0640","directoryMode":"0750","expectedVersion":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"}""";
    [Theory][InlineData(typeof(ContainerRecursivePermissionsController))][InlineData(typeof(ContainerRecursivePermissionRecoveryController))]
    public void SurfacesRequireAdministratorAndNeverCachePrivateMetadata(Type type)
    {
        Assert.Contains(type.GetCustomAttributes(typeof(AuthorizeAttribute), true), a => ((AuthorizeAttribute)a).Policy == "Administrator");
        Assert.Contains(type.GetCustomAttributes(typeof(ResponseCacheAttribute), true), a => ((ResponseCacheAttribute)a).NoStore);
    }
    [Theory][InlineData("fileMode")][InlineData("directoryMode")][InlineData("instanceId")][InlineData("path")][InlineData("expectedVersion")]
    public void MissingFieldsCannotSelectImplicitModes(string field)
    {
        var value = JsonNode.Parse(Body)!.AsObject(); value.Remove(field);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ContainerRecursivePermissionPlanBody>(value.ToJsonString(), JsonSerializerOptions.Web));
    }
    [Theory][InlineData("640")][InlineData("null")][InlineData("true")][InlineData("{}")][InlineData("[]")]
    [InlineData("\"640\"")][InlineData("\"4755\"")][InlineData("\"0780\"")][InlineData("\"0750 \\n\"")][InlineData("\"u+rwx\"")]
    public void InvalidModeTypesSpecialBitsAndFormatsCannotBeCoerced(string literal)
    {
        foreach (var field in new[] { "fileMode", "directoryMode" })
        {
            var value = JsonNode.Parse(Body)!.AsObject(); value[field] = JsonNode.Parse(literal);
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ContainerRecursivePermissionPlanBody>(value.ToJsonString(), JsonSerializerOptions.Web));
        }
    }
    [Theory][InlineData("fileMode")][InlineData("directoryMode")][InlineData("FileMode")][InlineData("mode")][InlineData("recursive")][InlineData("acl")][InlineData("uid")]
    public void DuplicateOrExtraFieldsCannotSelectASecondMeaning(string field) =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ContainerRecursivePermissionPlanBody>(Body[..^1] + ",\"" + field + "\":\"0700\"}", JsonSerializerOptions.Web));
    [Theory][InlineData("null")][InlineData("[]")][InlineData("true")]
    public void NonObjectBodyIsRejected(string value) => Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ContainerRecursivePermissionPlanBody>(value, JsonSerializerOptions.Web));
    [Theory][InlineData("0000", "0777")][InlineData("0640", "0750")]
    public void TwoExplicitModesRoundTripExactly(string file, string directory)
    {
        var body = new ContainerRecursivePermissionPlanBody("777:2026-10-30T01:02:03Z", "work/tree", file, directory, new('A', 64));
        Assert.Equal(body, JsonSerializer.Deserialize<ContainerRecursivePermissionPlanBody>(JsonSerializer.Serialize(body, JsonSerializerOptions.Web), JsonSerializerOptions.Web));
    }
    [Theory][InlineData("{}")][InlineData("{\"confirmed\":true}")][InlineData("{\"version\":0}")]
    [InlineData("{\"confirmed\":true,\"version\":0,\"version\":1}")][InlineData("{\"confirmed\":false,\"confirmed\":true,\"version\":0}")]
    [InlineData("{\"confirmed\":true,\"version\":\"0\"}")][InlineData("{\"confirmed\":true,\"version\":-1}")]
    [InlineData("{\"confirmed\":true,\"version\":1.0}")][InlineData("{\"confirmed\":true,\"version\":9223372036854775808}")]
    [InlineData("{\"confirmed\":true,\"version\":0,\"extra\":true}")]
    public void MutationBodiesRejectAmbiguousConfirmationOrVersion(string body)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AcknowledgeContainerRecursivePermissionBody>(body, JsonSerializerOptions.Web));
        var confirm = body[..^1] + ",\"planHash\":\"opaque\"}";
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ConfirmContainerRecursivePermissionBody>(confirm, JsonSerializerOptions.Web));
    }
    [Fact]
    public void ConfirmationTokenCannotBeMissingRepeatedOrCoerced()
    {
        foreach (var json in new[] { "{\"confirmed\":true,\"version\":0}", "{\"planHash\":null,\"confirmed\":true,\"version\":0}", "{\"planHash\":\"\",\"confirmed\":true,\"version\":0}", "{\"planHash\":\"a\",\"planHash\":\"b\",\"confirmed\":true,\"version\":0}" })
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ConfirmContainerRecursivePermissionBody>(json, JsonSerializerOptions.Web));
    }
    [Fact]
    public void InvalidModelStateHasStableCodeWithoutEchoingPrivateInput()
    {
        var model = new Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary(); model.AddModelError("fileMode", "private-test-input-must-not-leak");
        var action = new ActionContext(new DefaultHttpContext(), new Microsoft.AspNetCore.Routing.RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor(), model);
        var context = new Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext(action, [], new Dictionary<string, object?>(), new object());
        new ContainerRecursivePermissionInputAttribute().OnActionExecuting(context);
        var problem = Assert.IsType<ProblemDetails>(Assert.IsType<BadRequestObjectResult>(context.Result).Value);
        Assert.Equal("CONTAINER_RECURSIVE_PERMISSION_INPUT_INVALID", problem.Extensions["code"]); Assert.DoesNotContain("private-test-input", JsonSerializer.Serialize(problem));
    }
    [Fact]
    public async Task InvalidKeysAndModesFailBeforeIdentityOrBusinessCalls()
    {
        var controller = new ContainerRecursivePermissionsController(null!, null!) { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        var recovery = new ContainerRecursivePermissionRecoveryController(null!, null!) { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        var id = Guid.Parse("a26533e0-029e-48d8-88b8-8273058b370d"); var cid = new string('a', 64);
        foreach (var key in new[] { "", Guid.Empty.ToString("D"), id.ToString("D").ToUpperInvariant(), id.ToString("N") })
        {
            controller.Request.Headers["Idempotency-Key"] = key;
            var result = Assert.IsType<BadRequestObjectResult>((await controller.Queue(cid, null!, default)).Result);
            Assert.Equal("CONTAINER_RECURSIVE_PERMISSION_IDEMPOTENCY_KEY_REQUIRED", Assert.IsType<ProblemDetails>(result.Value).Extensions["code"]);
            Assert.IsType<BadRequestObjectResult>((await controller.Confirm(cid, id, null!, default)).Result); Assert.IsType<BadRequestObjectResult>((await controller.Cancel(cid, id, default)).Result);
        }
        controller.Request.Headers["Idempotency-Key"] = new StringValues([id.ToString("D"), id.ToString("D")]);
        Assert.IsType<BadRequestObjectResult>((await controller.Queue(cid, null!, default)).Result); Assert.IsType<BadRequestObjectResult>(await recovery.Acknowledge(id, null!, default));
        controller.Request.Headers["Idempotency-Key"] = id.ToString("D");
        foreach (var body in new[] { new ContainerRecursivePermissionPlanBody("i", "p", null!, "0750", "v"), new("i", "p", "0640", null!, "v"), new("i", "p", "4755", "0750", "v") })
        {
            var result = Assert.IsType<BadRequestObjectResult>((await controller.Queue(cid, body, default)).Result);
            Assert.Equal("CONTAINER_RECURSIVE_PERMISSION_MODES_REQUIRED", Assert.IsType<ProblemDetails>(result.Value).Extensions["code"]);
        }
    }
}
