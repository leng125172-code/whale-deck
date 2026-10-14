using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;

namespace WhaleDeck.Api.Security;

public static class ApiRateLimiting
{
    public const string PolicyName = "api";
    public const int PermitLimit = 120;

    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services) => services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.AddPolicy(PolicyName, context => RateLimitPartition.GetFixedWindowLimiter(BudgetScope(context) + ":" + PartitionKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = PermitLimit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
            }));
        options.OnRejected = async (rejected, cancellationToken) =>
        {
            var context = rejected.HttpContext;
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers.CacheControl = "no-store";
            if (rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                context.Response.Headers.RetryAfter = Math.Clamp((int)Math.Ceiling(retryAfter.TotalSeconds), 1, 60).ToString(CultureInfo.InvariantCulture);
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = "The API request budget was exceeded. Retry after the current window.",
                Extensions = { ["code"] = "API_RATE_LIMITED", ["traceId"] = context.TraceIdentifier }
            }, options: null, contentType: "application/problem+json", cancellationToken: cancellationToken);
        };
    });

    /// <summary>Endpoint routing must already run. Cookie authentication establishes the partition before limiting.</summary>
    public static IApplicationBuilder UseAuthenticatedApiRateLimiting(this IApplicationBuilder application)
    {
        application.UseAuthentication();
        return application.UseRateLimiter();
    }

    public static string PartitionKey(HttpContext context)
    {
        var subject = context.User.FindFirst("sub")?.Value;
        return context.User.Identity?.IsAuthenticated == true && !string.IsNullOrWhiteSpace(subject) &&
            subject.Length <= 256 && !subject.Any(char.IsControl)
            ? "subject:" + subject
            : "anonymous-ip:" + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
    }

    private static string BudgetScope(HttpContext context) =>
        context.Request.Path.StartsWithSegments("/api/v1/auth", StringComparison.OrdinalIgnoreCase) ||
        context.Request.Path.StartsWithSegments("/signin-oidc", StringComparison.OrdinalIgnoreCase)
            ? "session" : "operations";
}
