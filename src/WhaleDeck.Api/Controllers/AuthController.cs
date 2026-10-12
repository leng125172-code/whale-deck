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
        return Challenge(new AuthenticationProperties { RedirectUri = safeReturnUrl }, "WhaleDeckOidc");
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
