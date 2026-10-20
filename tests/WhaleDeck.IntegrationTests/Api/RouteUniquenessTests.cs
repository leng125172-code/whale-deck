using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using WhaleDeck.Api.Controllers;

namespace WhaleDeck.IntegrationTests.Api;

public sealed class RouteUniquenessTests
{
    [Fact]
    public void NoTwoDeclaredControllerActionsShareTheSameVerbAndExactRoute()
    {
        var endpoints = Collect(typeof(FilesController).Assembly.GetTypes());
        Assert.Contains("POST api/v1/files/tree-packings", endpoints.Keys);
        Assert.Contains("POST api/v1/files/tree-copies", endpoints.Keys);
        Assert.Contains("POST api/v1/files/tree-purgings", endpoints.Keys);
    }
    [Fact]
    public void RegressionGuardRejectsTheOriginalCopiedControllerPrefix()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Collect([typeof(FirstController), typeof(SecondController)]));
        Assert.Contains("POST api/v1/files/tree-copies", error.Message, StringComparison.Ordinal);
    }
    private static Dictionary<string, string> Collect(IEnumerable<Type> controllers)
    {
        var endpoints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var controller in controllers.Where(type => !type.IsAbstract && typeof(ControllerBase).IsAssignableFrom(type)))
        {
            var prefixes = controller.GetCustomAttributes<RouteAttribute>(true).Select(route => route.Template).DefaultIfEmpty("");
            foreach (var method in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (method.IsDefined(typeof(NonActionAttribute))) continue;
                foreach (var mapping in method.GetCustomAttributes<HttpMethodAttribute>(true))
                    foreach (var prefix in prefixes)
                    {
                        var suffix = mapping.Template ?? "";
                        var path = (suffix.StartsWith('/') || suffix.StartsWith("~/", StringComparison.Ordinal) ? suffix.TrimStart('~', '/') : prefix.Trim('/') + "/" + suffix.Trim('/'))
                            .Replace("[controller]", controller.Name.Replace("Controller", "", StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase)
                            .Replace("[action]", method.Name, StringComparison.OrdinalIgnoreCase).Trim('/');
                        foreach (var verb in mapping.HttpMethods)
                        {
                            var key = verb + " " + path; var action = controller.Name + "." + method.Name;
                            if (!endpoints.TryAdd(key, action)) throw new InvalidOperationException($"Ambiguous route {key}: {endpoints[key]} and {action}");
                        }
                    }
            }
        }
        return endpoints;
    }
    [Route("api/v1/files/tree-copies")]
    private sealed class FirstController : ControllerBase
    {
        [HttpPost] public NoContentResult Queue() => NoContent();
    }
    [Route("api/v1/files/tree-copies")]
    private sealed class SecondController : ControllerBase
    {
        [HttpPost] public NoContentResult Queue() => NoContent();
    }
}
