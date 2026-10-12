using Microsoft.AspNetCore.Http;
using WhaleDeck.Api.Security;

namespace WhaleDeck.IntegrationTests.Security;

public sealed class OidcTransientCookieCleanupTests
{
    [Fact]
    public void PreparationRemovesLegacyAndNamespacedCookiesButKeepsRealSessions()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = ".AspNetCore.OpenIdConnect.Nonce.old=abc; .AspNetCore.Correlation.old=abc; whaledeck.oidc.AuthentikExternal.nonce.old=abc; whaledeck.session=keep; authentik_session=keep; other=keep";
        OidcTransientCookieCleanup.Apply(context.Request, context.Response, "/signin-oidc/external");
        var values = context.Response.Headers.SetCookie.ToString();
        Assert.Contains(".AspNetCore.OpenIdConnect.Nonce.old=", values);
        Assert.Contains(".AspNetCore.Correlation.old=", values);
        Assert.Contains("whaledeck.oidc.AuthentikExternal.nonce.old=", values);
        Assert.Contains("path=/signin-oidc/external", values);
        // Delete(name) replaces an existing Set-Cookie for the same name;
        // deleting a second path would silently discard the actual callback
        // path deletion and leave interrupted-login cookies in the browser.
        Assert.All(context.Response.Headers.SetCookie, header => Assert.Contains("path=/signin-oidc/external", header));
        Assert.DoesNotContain("whaledeck.session", values);
        Assert.DoesNotContain("authentik_session", values);
        Assert.DoesNotContain("other=", values);
    }

    [Fact]
    public void CleanupRejectsUnapprovedPaths()
    {
        var context = new DefaultHttpContext();
        Assert.Throws<ArgumentException>(() => OidcTransientCookieCleanup.Apply(context.Request, context.Response, "/"));
    }
}
