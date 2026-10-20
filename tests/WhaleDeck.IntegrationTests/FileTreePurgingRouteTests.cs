using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Primitives;
using WhaleDeck.Api.Controllers;

namespace WhaleDeck.IntegrationTests;

public sealed class FileTreePurgingRouteTests
{
    [Fact]
    public void RoutesAreAdministratorOnlyNoStoreAndExposeOnlyTheSixWorkflowActions()
    {
        var type = typeof(FileTreePurgingsController);
        Assert.Equal("api/v1/files/tree-purgings", Assert.Single(type.GetCustomAttributes<RouteAttribute>(true)).Template);
        Assert.Contains(type.GetCustomAttributes<AuthorizeAttribute>(true), item => item.Policy == "Administrator");
        var cache = Assert.Single(type.GetCustomAttributes<ResponseCacheAttribute>(true));
        Assert.True(cache.NoStore); Assert.Equal(ResponseCacheLocation.None, cache.Location);

        var expected = new Dictionary<string, (string Verb, string? Template)>
        {
            ["Queue"] = ("POST", null),
            ["Status"] = ("GET", "{id:guid}"),
            ["ByKey"] = ("GET", "by-key/{key}"),
            ["Confirm"] = ("POST", "{id:guid}/confirm"),
            ["Cancel"] = ("POST", "{id:guid}/cancel"),
            ["Acknowledge"] = ("POST", "{id:guid}/acknowledge")
        };
        var actions = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.Equal(expected.Keys.OrderBy(item => item), actions.Select(item => item.Name).OrderBy(item => item));
        foreach (var action in actions)
        {
            var route = Assert.Single(action.GetCustomAttributes<HttpMethodAttribute>(true));
            Assert.Equal(expected[action.Name].Verb, Assert.Single(route.HttpMethods));
            Assert.Equal(expected[action.Name].Template, route.Template);
        }
        Assert.DoesNotContain(actions, item => item.Name.Contains("Apply", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-uuid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA")]
    [InlineData(" aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")]
    [InlineData("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa,aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")]
    public async Task InvalidWriteKeysAreRejectedBeforeReadingIdentity(string? key)
    {
        var controller = Controller();
        if (key is not null) controller.Request.Headers["Idempotency-Key"] = key;
        await AssertRejectedAllWrites(controller, Guid.NewGuid());
    }

    [Fact]
    public async Task MultipleWriteKeysAreRejectedBeforeReadingIdentity()
    {
        var controller = Controller(); var id = Guid.NewGuid();
        controller.Request.Headers["Idempotency-Key"] = new StringValues([id.ToString("D"), id.ToString("D")]);
        await AssertRejectedAllWrites(controller, id);
    }

    [Fact]
    public async Task NonmatchingCanonicalKeyIsRejectedForExistingIntentWritesBeforeReadingIdentity()
    {
        var controller = Controller(); var id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        controller.Request.Headers["Idempotency-Key"] = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
        // Queue intentionally accepts a new canonical key; only existing-intent
        // actions bind the key to the route ID.
        await AssertRejectedIntentWrites(controller, id);
    }

    private static FileTreePurgingsController Controller() => new(null!, null!)
    {
        ControllerContext = new() { HttpContext = new DefaultHttpContext() }
    };

    private static async Task AssertRejectedAllWrites(FileTreePurgingsController controller, Guid id)
    {
        Assert.IsType<BadRequestResult>((await controller.Queue(new(Guid.NewGuid(), "All"), default)).Result);
        await AssertRejectedIntentWrites(controller, id);
    }

    private static async Task AssertRejectedIntentWrites(FileTreePurgingsController controller, Guid id)
    {
        Assert.IsType<BadRequestResult>((await controller.Confirm(id, new("unused", true, 0), default)).Result);
        Assert.IsType<BadRequestResult>((await controller.Cancel(id, default)).Result);
        Assert.IsType<BadRequestResult>(await controller.Acknowledge(id, new(0, true), default));
    }
}
