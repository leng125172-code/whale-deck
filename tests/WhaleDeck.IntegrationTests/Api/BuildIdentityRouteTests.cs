using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhaleDeck.Api.Controllers;
using WhaleDeck.Contracts;

namespace WhaleDeck.IntegrationTests.Api;

public sealed class BuildIdentityRouteTests
{
    [Fact]
    public void PublicStatusProjectsOnlyBinaryIdentityAndPreservesPublicVersionShape()
    {
        var result = Assert.IsType<OkObjectResult>(new SystemController().Status());
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result.Value));
        var build = json.RootElement.GetProperty("build");
        var expected = BuildIdentity.FromAssembly(typeof(SystemController).Assembly);
        Assert.Equal("V" + expected.Version, json.RootElement.GetProperty("version").GetString());
        Assert.Equal(expected.SourceRevision, build.GetProperty("sourceRevision").GetString());
        Assert.Equal("WhaleDeck.Api", build.GetProperty("component").GetString());
        Assert.Equal(3, build.EnumerateObject().Count());
        var method = typeof(SystemController).GetMethod(nameof(SystemController.Status))!;
        Assert.NotNull(method.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.True(method.GetCustomAttribute<ResponseCacheAttribute>()?.NoStore);
    }
}
