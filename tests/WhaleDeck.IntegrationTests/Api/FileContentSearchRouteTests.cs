using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhaleDeck.Api.Controllers;

namespace WhaleDeck.IntegrationTests.Api;

public sealed class FileContentSearchRouteTests
{
    [Fact]
    public void ReadOnlyContentPostRetainsAdministratorNoStoreAndBoundedBodyGuards()
    {
        var type = typeof(FileBrowserController);
        Assert.Contains(type.GetCustomAttributes<AuthorizeAttribute>(), value => value.Policy == "Administrator");
        Assert.True(type.GetCustomAttribute<ResponseCacheAttribute>()?.NoStore);
        var method = type.GetMethod(nameof(FileBrowserController.ContentSearch))!;
        Assert.Equal("content-search", Assert.Single(method.GetCustomAttributes<HttpPostAttribute>()).Template);
        var size = Assert.Single(method.CustomAttributes, value => value.AttributeType == typeof(RequestSizeLimitAttribute));
        Assert.Equal(65536L, size.ConstructorArguments[0].Value);
        Assert.Null(method.GetCustomAttribute<AllowAnonymousAttribute>());
    }
}
