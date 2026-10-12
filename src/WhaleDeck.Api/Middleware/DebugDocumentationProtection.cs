using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace WhaleDeck.Api.Middleware;

/// <summary>Production debug routes fail closed without starting an OIDC redirect loop.</summary>
public static class DebugDocumentationProtection
{
    public static async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (!context.Request.Path.StartsWithSegments("/swagger") && !context.Request.Path.StartsWithSegments("/openapi"))
        {
            await next(context);
            return;
        }

        var authenticated = context.User.Identity?.IsAuthenticated == true;
        if (authenticated && (await context.RequestServices.GetRequiredService<IAuthorizationService>()
            .AuthorizeAsync(context.User, "Administrator")).Succeeded)
        {
            await next(context);
            return;
        }

        var status = authenticated ? StatusCodes.Status403Forbidden : StatusCodes.Status401Unauthorized;
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = status,
            Title = authenticated ? "Administrator access is required." : "Authentication is required.",
            Extensions = { ["code"] = authenticated ? "AUTH_FORBIDDEN" : "AUTH_REQUIRED", ["traceId"] = context.TraceIdentifier }
        }, options: null, contentType: "application/problem+json", cancellationToken: context.RequestAborted);
    }
}
