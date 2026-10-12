using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using WhaleDeck.Api.Controllers;
using WhaleDeck.Api.Security;

namespace WhaleDeck.IntegrationTests.Security;

public sealed class ManagementRouteTests
{
    [Fact]
    public void OperationRoutesDoNotUseMvcReservedActionKey()
    {
        var templates = typeof(ManagementController)
            .GetMethods()
            .SelectMany(method => method.GetCustomAttributes(typeof(HttpPostAttribute), inherit: true))
            .Cast<HttpPostAttribute>()
            .Select(attribute => attribute.Template ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain(templates, template => template.Contains("{action}", StringComparison.Ordinal));
        Assert.Contains(templates, template => template.EndsWith("/{operation}/plan", StringComparison.Ordinal));
        Assert.Contains(templates, template => template.EndsWith("/{operation}", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("/api/v1/auth/me", true)]
    [InlineData("/api/v1/containers", true)]
    [InlineData("/api/v1/auth/login", false)]
    [InlineData("/swagger", false)]
    public void AnonymousApiFetchesDoNotRedirectAcrossOrigins(string path, bool expected)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;

        Assert.Equal(expected, OidcChallengeBehavior.ShouldReturnUnauthorized(context.Request));
    }

    [Theory]
    [InlineData("/api/v1/auth/me", "192.168.100.13", "Cookies")]
    [InlineData("/api/v1/auth/csrf", "192.168.22.19", "Cookies")]
    [InlineData("/api/v1/containers", "192.168.100.13", "Cookies")]
    [InlineData("/api/v1/auth/logout", "192.168.100.13", "Cookies")]
    [InlineData("/api/v1/auth/login", "192.168.100.13", "AuthentikExternal")]
    [InlineData("/signin-oidc/internal/prepare", "192.168.22.19", "AuthentikInternal")]
    public void ApiChallengesNeverEnterOidcAndMintTransientCookies(string path, string host, string expected)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.Host = new HostString(host, 8080);
        Assert.Equal(expected, OidcChallengeBehavior.SelectChallengeScheme(context.Request));
    }
}
