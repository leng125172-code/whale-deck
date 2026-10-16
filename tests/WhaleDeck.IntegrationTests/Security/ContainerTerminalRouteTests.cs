using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using WhaleDeck.Api.Controllers;
using WhaleDeck.Application.Models;

namespace WhaleDeck.IntegrationTests.Security;

public sealed class ContainerTerminalRouteTests
{
    [Fact]
    public void ContainerTerminalRoutesRequireAdministratorAndNeverAnonymous()
    {
        Assert.Contains(typeof(TerminalsController).GetCustomAttributes<AuthorizeAttribute>(), item => item.Policy == "Administrator");
        Assert.Empty(typeof(TerminalsController).GetMethods().SelectMany(method => method.GetCustomAttributes<AllowAnonymousAttribute>()));
        var routes = typeof(TerminalsController).GetMethods().SelectMany(method => method.GetCustomAttributes<HttpMethodAttribute>()).Select(item => item.Template);
        Assert.Contains("containers/{containerId}", routes); Assert.Contains("containers/grants", routes);
    }

    [Theory]
    [InlineData("http://foreign.invalid", "")]
    [InlineData("http://localhost", "?token=not-allowed")]
    public async Task WebSocketOriginAndQueryAreRejectedBeforeReadingTickets(string origin, string query)
    {
        var context = new DefaultHttpContext(); context.Request.Scheme = "http"; context.Request.Host = new("localhost");
        context.Request.Headers.Origin = origin; context.Request.QueryString = new(query);
        var controller = new TerminalsController(null!, null!, null!) { ControllerContext = new() { HttpContext = context } };
        await controller.Connect(Guid.NewGuid()); Assert.Equal(403, context.Response.StatusCode);
    }

    [Fact]
    public async Task GrantHeaderMustMatchBodyBeforeReadingTicketsOrCallingAgent()
    {
        var controller = new TerminalsController(null!, null!, null!) { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        var command = new CreateContainerTerminalCommand(true, Guid.NewGuid().ToString("D"), new('a', 64), "10:instance", "sh");
        var result = await controller.IssueContainer(command, default);
        Assert.IsType<BadRequestResult>(result.Result);
    }
}
