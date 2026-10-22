using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhaleDeck.Api.Controllers;

namespace WhaleDeck.IntegrationTests.Api;

public sealed class ContainerFileRouteTests
{
    [Fact]
    public void FourReadRoutesRetainAdministratorNoStoreAndNoHostRootOrWriteEndpoints()
    {
        var type = typeof(ContainerFilesController);
        Assert.Equal("api/v1/containers/{containerId}/files", type.GetCustomAttribute<RouteAttribute>()?.Template);
        Assert.Contains(type.GetCustomAttributes<AuthorizeAttribute>(), value => value.Policy == "Administrator");
        Assert.True(type.GetCustomAttribute<ResponseCacheAttribute>()?.NoStore);
        var methods = type.GetMethods().Where(method => method.GetCustomAttribute<HttpGetAttribute>() is not null).ToArray();
        Assert.Equal(4, methods.Length);
        Assert.Equal(["download", "entries", "entry", "target"], methods.Select(method => method.GetCustomAttribute<HttpGetAttribute>()!.Template).Order());
        Assert.All(methods, method =>
        {
            Assert.Null(method.GetCustomAttribute<AllowAnonymousAttribute>());
            Assert.DoesNotContain(method.GetParameters(), parameter => parameter.Name is "rootId" or "hostPath" or "command");
        });
    }
}
