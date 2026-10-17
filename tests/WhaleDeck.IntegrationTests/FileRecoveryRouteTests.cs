using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhaleDeck.Api.Controllers;

namespace WhaleDeck.IntegrationTests;

public sealed class FileRecoveryRouteTests
{
    [Fact]
    public void InventoryAndRecoveryRequireCurrentAdministratorAndExposeNoReplayEndpoint()
    {
        Assert.Contains(typeof(FileRecoveryController).GetCustomAttributes(typeof(AuthorizeAttribute), true), item => ((AuthorizeAttribute)item).Policy == "Administrator");
        var methods = typeof(FileRecoveryController).GetMethods().Where(method => method.DeclaringType == typeof(FileRecoveryController)).ToArray();
        Assert.Equal(7, methods.Length);
        Assert.Equal(4, methods.Count(method => method.IsDefined(typeof(HttpGetAttribute), true)));
        Assert.Equal(3, methods.Count(method => method.IsDefined(typeof(HttpPostAttribute), true)));
        Assert.DoesNotContain(methods, method => method.Name.Contains("Apply", StringComparison.OrdinalIgnoreCase) || method.Name.Contains("Replay", StringComparison.OrdinalIgnoreCase));
        var cache = Assert.Single(typeof(FileRecoveryController).GetCustomAttributes(typeof(ResponseCacheAttribute), true).Cast<ResponseCacheAttribute>());
        Assert.True(cache.NoStore); Assert.Equal(ResponseCacheLocation.None, cache.Location);
    }
    [Fact]
    public async Task RecoveryWritesRequireOneExactUuidKeyBeforeIdentityOrBusinessLogic()
    {
        var controller = new FileRecoveryController(null!, null!, null!) { ControllerContext = new() { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() } };
        var id = Guid.NewGuid();
        Assert.IsType<BadRequestResult>((await controller.Queue(null!, default)).Result);
        Assert.IsType<BadRequestResult>((await controller.Confirm(id, new("unused", true, 0), default)).Result);
        Assert.IsType<BadRequestResult>((await controller.Cancel(id, default)).Result);
        controller.Request.Headers["Idempotency-Key"] = new Microsoft.Extensions.Primitives.StringValues([id.ToString("D"), id.ToString("D")]);
        Assert.IsType<BadRequestResult>((await controller.Cancel(id, default)).Result);
    }
}
