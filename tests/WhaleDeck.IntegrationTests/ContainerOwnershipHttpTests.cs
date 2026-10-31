using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using WhaleDeck.Api.Controllers;

namespace WhaleDeck.IntegrationTests;

public sealed class ContainerOwnershipHttpTests
{
    private const string Body = """{"instanceId":"777:2026-10-30T01:02:03Z","path":"work/object","uid":2200,"gid":3200,"expectedVersion":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"}""";
    [Theory][InlineData(typeof(ContainerOwnershipsController))][InlineData(typeof(ContainerOwnershipRecoveryController))]
    public void OwnershipSurfacesRequireAdministratorAndNeverCachePrivateMetadata(Type type)
    {
        Assert.Contains(type.GetCustomAttributes(typeof(AuthorizeAttribute), true), a => ((AuthorizeAttribute)a).Policy == "Administrator");
        Assert.Contains(type.GetCustomAttributes(typeof(ResponseCacheAttribute), true), a => ((ResponseCacheAttribute)a).NoStore);
    }
    [Theory][InlineData("uid")][InlineData("gid")][InlineData("instanceId")][InlineData("path")][InlineData("expectedVersion")]
    public void MissingFieldsCannotDefaultToRootOrCreateAnAmbiguousCommand(string field)
    {
        var value = JsonNode.Parse(Body)!.AsObject(); value.Remove(field);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ContainerOwnershipPlanBody>(value.ToJsonString(), JsonSerializerOptions.Web));
    }
    [Theory][InlineData("\"0\"")][InlineData("1.0")][InlineData("1e2")][InlineData("-1")][InlineData("4294967295")][InlineData("4294967296")]
    [InlineData("null")][InlineData("true")][InlineData("{}")][InlineData("[]")]
    public void InvalidOwnerTypesFormatsAndSentinelsCannotBeCoerced(string literal)
    {
        foreach (var field in new[] { "uid", "gid" })
        {
            var value = JsonNode.Parse(Body)!.AsObject(); value[field] = JsonNode.Parse(literal);
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ContainerOwnershipPlanBody>(value.ToJsonString(), JsonSerializerOptions.Web));
        }
    }
    [Theory][InlineData("uid")][InlineData("gid")][InlineData("Uid")][InlineData("mode")][InlineData("recursive")][InlineData("acl")][InlineData("owner")]
    public void DuplicateOrExtraFieldsCannotSelectASecondMeaning(string field)
    {
        var value = Body[..^1] + ",\"" + field + "\":0}";
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ContainerOwnershipPlanBody>(value, JsonSerializerOptions.Web));
    }
    [Theory][InlineData("null")][InlineData("[]")][InlineData("true")]
    public void NonObjectBodyIsRejected(string value) => Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ContainerOwnershipPlanBody>(value, JsonSerializerOptions.Web));
    [Theory][InlineData(0U, 0U)][InlineData(0U, 3200U)][InlineData(2200U, 0U)][InlineData(4294967294U, 4294967294U)]
    public void ExplicitZeroAndLargestSupportedIdsRoundTripExactly(uint uid, uint gid)
    {
        var body = new ContainerOwnershipPlanBody("777:2026-10-30T01:02:03Z", "work/object", uid, gid, new('A', 64));
        var restored = JsonSerializer.Deserialize<ContainerOwnershipPlanBody>(JsonSerializer.Serialize(body, JsonSerializerOptions.Web), JsonSerializerOptions.Web);
        Assert.Equal(body, restored);
    }
    [Fact]
    public void InvalidModelStateHasStableCodeWithoutEchoingPrivateInput()
    {
        var model = new Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary();
        model.AddModelError("uid", "private-test-input-must-not-leak");
        var action = new ActionContext(new DefaultHttpContext(), new Microsoft.AspNetCore.Routing.RouteData(),
            new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor(), model);
        var context = new Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext(action, [], new Dictionary<string, object?>(), new object());
        new ContainerOwnershipInputAttribute().OnActionExecuting(context);
        var problem = Assert.IsType<ProblemDetails>(Assert.IsType<BadRequestObjectResult>(context.Result).Value);
        Assert.Equal("CONTAINER_OWNERSHIP_INPUT_INVALID", problem.Extensions["code"]);
        Assert.DoesNotContain("private-test-input", JsonSerializer.Serialize(problem));
    }
    [Fact]
    public async Task InvalidKeysAndMissingOwnersFailBeforeIdentityOrBusinessCalls()
    {
        var controller = new ContainerOwnershipsController(null!, null!) { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        var recovery = new ContainerOwnershipRecoveryController(null!, null!) { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        var id = Guid.Parse("a26533e0-029e-48d8-88b8-8273058b370d"); const string cid = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        foreach (var key in new[] { "", Guid.Empty.ToString("D"), id.ToString("D").ToUpperInvariant(), id.ToString("N") })
        {
            controller.Request.Headers["Idempotency-Key"] = key;
            var result = Assert.IsType<BadRequestObjectResult>((await controller.Queue(cid, null!, default)).Result);
            Assert.Equal("CONTAINER_OWNERSHIP_IDEMPOTENCY_KEY_REQUIRED", Assert.IsType<ProblemDetails>(result.Value).Extensions["code"]);
            Assert.IsType<BadRequestObjectResult>((await controller.Confirm(cid, id, null!, default)).Result);
            Assert.IsType<BadRequestObjectResult>((await controller.Cancel(cid, id, default)).Result);
        }
        controller.Request.Headers["Idempotency-Key"] = new StringValues([id.ToString("D"), id.ToString("D")]);
        Assert.IsType<BadRequestObjectResult>((await controller.Queue(cid, null!, default)).Result);
        Assert.IsType<BadRequestObjectResult>(await recovery.Acknowledge(id, null!, default));
        controller.Request.Headers["Idempotency-Key"] = id.ToString("D");
        foreach (var body in new[] { new ContainerOwnershipPlanBody("i", "p", null, 0, "v"), new("i", "p", 0, null, "v"), new("i", "p", uint.MaxValue, 0, "v"), new("i", "p", 0, uint.MaxValue, "v") })
        {
            var result = Assert.IsType<BadRequestObjectResult>((await controller.Queue(cid, body, default)).Result);
            Assert.Equal("CONTAINER_OWNERSHIP_UID_GID_REQUIRED", Assert.IsType<ProblemDetails>(result.Value).Extensions["code"]);
        }
    }
}
