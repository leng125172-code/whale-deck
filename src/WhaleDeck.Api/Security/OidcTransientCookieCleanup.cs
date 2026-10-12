namespace WhaleDeck.Api.Security;

public static class OidcTransientCookieCleanup
{
    public static void Apply(HttpRequest request, HttpResponse response, string callbackPath)
    {
        if (callbackPath is not ("/signin-oidc/internal" or "/signin-oidc/external"))
            throw new ArgumentException("Unknown OIDC callback.", nameof(callbackPath));
        foreach (var name in request.Cookies.Keys.Where(IsTransient).Take(128))
        {
            response.Cookies.Delete(name, new CookieOptions { Path = callbackPath, HttpOnly = true, SameSite = SameSiteMode.Lax });
            if (name.StartsWith("whaledeck.oidc.", StringComparison.Ordinal))
                response.Cookies.Delete(name, new CookieOptions { Path = "/", HttpOnly = true, SameSite = SameSiteMode.Lax });
        }
    }

    private static bool IsTransient(string name) => name.Length <= 512 &&
        (name.StartsWith("whaledeck.oidc.", StringComparison.Ordinal) ||
         name.StartsWith(".AspNetCore.OpenIdConnect.Nonce.", StringComparison.Ordinal) ||
         name.StartsWith(".AspNetCore.Correlation.", StringComparison.Ordinal));
}
