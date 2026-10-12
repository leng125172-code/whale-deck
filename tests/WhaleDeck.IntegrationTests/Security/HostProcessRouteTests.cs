using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhaleDeck.Api.Controllers;

namespace WhaleDeck.IntegrationTests.Security;

public sealed class HostProcessRouteTests
{
    [Fact]
    public void ProcessesAreAdministratorReadOnlyRoutes()
    {
        Assert.Contains(typeof(HostController).GetCustomAttributes<AuthorizeAttribute>(), item => item.Policy == "Administrator");
        var routes = typeof(HostController).GetMethods().SelectMany(method => method.GetCustomAttributes<HttpGetAttribute>()).Select(item => item.Template);
        Assert.Contains("processes", routes);
        Assert.Contains("processes/{pid:int}", routes);
        Assert.Empty(typeof(HostController).GetMethods().SelectMany(method => method.GetCustomAttributes<HttpPostAttribute>()));
    }
}
