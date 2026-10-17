using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhaleDeck.Api.Controllers;

namespace WhaleDeck.IntegrationTests;

public sealed class FileRecoveryRouteTests
{
    [Fact]
    public void InventoryRequiresCurrentAdministratorAndHasNoPrematureWriteOrReplayEndpoint()
    {
        Assert.Contains(typeof(FileRecoveryController).GetCustomAttributes(typeof(AuthorizeAttribute), true), item => ((AuthorizeAttribute)item).Policy == "Administrator");
        var methods = typeof(FileRecoveryController).GetMethods().Where(method => method.DeclaringType == typeof(FileRecoveryController)).ToArray();
        Assert.Equal(2, methods.Length); Assert.All(methods, method => Assert.Single(method.GetCustomAttributes(typeof(HttpGetAttribute), true)));
        var cache = Assert.Single(typeof(FileRecoveryController).GetCustomAttributes(typeof(ResponseCacheAttribute), true).Cast<ResponseCacheAttribute>());
        Assert.True(cache.NoStore); Assert.Equal(ResponseCacheLocation.None, cache.Location);
    }
}
