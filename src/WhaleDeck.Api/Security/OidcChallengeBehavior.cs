namespace WhaleDeck.Api.Security;

internal static class OidcChallengeBehavior
{
    public static string SelectChallengeScheme(HttpRequest request) =>
        ShouldReturnUnauthorized(request) ? Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme :
        request.Host.Host == "192.168.22.19" ? "AuthentikInternal" : "AuthentikExternal";

    public static bool ShouldReturnUnauthorized(HttpRequest request) =>
        request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase) &&
        !request.Path.StartsWithSegments("/api/v1/auth/login", StringComparison.OrdinalIgnoreCase);
}
