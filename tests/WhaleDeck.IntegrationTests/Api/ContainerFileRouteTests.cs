using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhaleDeck.Api.Controllers;

namespace WhaleDeck.IntegrationTests.Api;

public sealed class ContainerFileRouteTests
{
    [Theory]
    [InlineData(typeof(ContainerDirectoriesController))]
    [InlineData(typeof(ContainerDirectoryRecoveryController))]
    public void DirectoryIntentAndCurrentAdministratorRecoveryArePrivateNoStoreRoutes(Type type)
    {
        Assert.Contains(type.GetCustomAttributes<AuthorizeAttribute>(), value => value.Policy == "Administrator");
        Assert.True(type.GetCustomAttribute<ResponseCacheAttribute>()?.NoStore);
        Assert.Null(type.GetCustomAttribute<AllowAnonymousAttribute>());
        var methods = type.GetMethods().Where(method => method.GetCustomAttribute<HttpPostAttribute>() is not null).ToArray();
        Assert.NotEmpty(methods);
        Assert.All(methods, method => Assert.Null(method.GetCustomAttribute<AllowAnonymousAttribute>()));
        Assert.DoesNotContain(type.GetMethods(), method => method.Name.Contains("Restore", StringComparison.Ordinal) || method.Name.Contains("Shell", StringComparison.Ordinal));
    }
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
