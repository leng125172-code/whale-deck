using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhaleDeck.Api.Security;
using WhaleDeck.Application.Abstractions;

namespace WhaleDeck.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(IIdentityDirectory identityDirectory, IAntiforgery antiforgery) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("login")]
    public IActionResult Login([FromQuery] string returnUrl = "/")
    {
        var safeReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : "/";
        if (User.Identity?.IsAuthenticated == true) return LocalRedirect(safeReturnUrl);
        var network = Request.Host.Host == "192.168.22.19" ? "internal" : "external";
        // Callback-scoped cookies are not sent to /api/v1/auth/login. Visit a
        // path beneath the callback first so old interrupted-login cookies can
        // be removed before creating a fresh OIDC challenge.
        return Redirect($"/signin-oidc/{network}/prepare?returnUrl={Uri.EscapeDataString(safeReturnUrl)}");
    }

    [AllowAnonymous]
    [HttpGet("/signin-oidc/{network}/prepare")]
    public IActionResult PrepareLogin(string network, [FromQuery] string returnUrl = "/")
    {
        if (network is not ("internal" or "external")) return NotFound();
        var safeReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : "/";
        OidcTransientCookieCleanup.Apply(Request, Response, $"/signin-oidc/{network}");
        return Challenge(new AuthenticationProperties { RedirectUri = safeReturnUrl },
            network == "internal" ? "AuthentikInternal" : "AuthentikExternal");
    }

    [HttpPost("logout")]
    public IActionResult Logout()
    {
        var scheme = User.FindFirst("whaledeck:oidc-scheme")?.Value;
        if (scheme is not ("AuthentikInternal" or "AuthentikExternal")) scheme = "AuthentikExternal";
        return SignOut(new AuthenticationProperties { RedirectUri = "/login" }, CookieAuthenticationDefaults.AuthenticationScheme, scheme);
    }

    [HttpGet("csrf")]
    public IActionResult Csrf()
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        Response.Headers.CacheControl = "no-store";
        return Ok(new { headerName = tokens.HeaderName, formFieldName = tokens.FormFieldName, token = tokens.RequestToken });
    }

    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        var subject = User.RequireSubject();
        var groups = User.FindAll("groups").Select(item => item.Value).ToArray();
        return Ok(await identityDirectory.ResolveCurrentAsync(subject, User.Identity?.Name, groups, cancellationToken));
    }
}
