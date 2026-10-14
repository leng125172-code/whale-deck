using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WhaleDeck.Api.Middleware;
using WhaleDeck.Api.Security;

namespace WhaleDeck.IntegrationTests.Security;

public sealed class ApiRateLimitingTests
{
    [Fact]
    public async Task RealMiddlewareAuthenticatesBeforePartitioningTwoUsersBehindOneProxy()
    {
        await using var services = Services();
        var pipeline = Pipeline(services);
        for (var index = 0; index < ApiRateLimiting.PermitLimit; index++)
        {
            Assert.Equal(200, (await Request(pipeline, services, "alice")).Response.StatusCode);
            Assert.Equal(200, (await Request(pipeline, services, "bob")).Response.StatusCode);
        }
        var rejected = await Request(pipeline, services, "alice");
        Assert.Equal(429, rejected.Response.StatusCode);
        Assert.Equal(429, (await Request(pipeline, services, "bob")).Response.StatusCode);
        Assert.Equal("no-store", rejected.Response.Headers.CacheControl);
        Assert.StartsWith("application/problem+json", rejected.Response.ContentType, StringComparison.Ordinal);
        Assert.InRange(int.Parse(rejected.Response.Headers.RetryAfter.ToString(), System.Globalization.CultureInfo.InvariantCulture), 1, 60);
        rejected.Response.Body.Position = 0;
        using var problem = await JsonDocument.ParseAsync(rejected.Response.Body);
        Assert.Equal("API_RATE_LIMITED", problem.RootElement.GetProperty("code").GetString());
        Assert.Equal("rate-fixture", problem.RootElement.GetProperty("traceId").GetString());
        Assert.DoesNotContain("alice", problem.RootElement.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnonymousBudgetIsPerIpAndAuthenticationIsStillRequired()
    {
        await using var services = Services();
        var pipeline = Pipeline(services);
        for (var index = 0; index < ApiRateLimiting.PermitLimit; index++)
            Assert.Equal(401, (await Request(pipeline, services)).Response.StatusCode);
        Assert.Equal(429, (await Request(pipeline, services)).Response.StatusCode);
        Assert.Equal(401, (await Request(pipeline, services, ip: "127.0.0.2")).Response.StatusCode);
        Assert.Equal(200, (await Request(pipeline, services, "alice")).Response.StatusCode);
    }

    [Fact]
    public async Task ExhaustedOperationBudgetDoesNotPreventSessionChecksCsrfOrLogout()
    {
        await using var services = Services();
        var pipeline = Pipeline(services);
        for (var index = 0; index < ApiRateLimiting.PermitLimit; index++)
            Assert.Equal(200, (await Request(pipeline, services, "alice")).Response.StatusCode);
        Assert.Equal(429, (await Request(pipeline, services, "alice")).Response.StatusCode);
        Assert.Equal(200, (await Request(pipeline, services, "alice", path: "/api/v1/auth/me")).Response.StatusCode);
        Assert.Equal(200, (await Request(pipeline, services, "alice", path: "/api/v1/auth/csrf")).Response.StatusCode);
        Assert.Equal(200, (await Request(pipeline, services, "alice", method: "POST", origin: "http://localhost", path: "/api/v1/auth/logout")).Response.StatusCode);
        // Session operations have their own bounded budget, not an unlimited exemption.
        for (var index = 3; index < ApiRateLimiting.PermitLimit; index++)
            Assert.Equal(200, (await Request(pipeline, services, "alice", path: "/api/v1/auth/me")).Response.StatusCode);
        Assert.Equal(429, (await Request(pipeline, services, "alice", path: "/api/v1/auth/me")).Response.StatusCode);
    }

    [Fact]
    public async Task LimiterDoesNotBypassOriginOrAuthenticationChecks()
    {
        await using var services = Services();
        var pipeline = Pipeline(services);
        Assert.Equal(403, (await Request(pipeline, services, "alice", method: "POST", origin: "http://unapproved.invalid")).Response.StatusCode);
        Assert.Equal(401, (await Request(pipeline, services)).Response.StatusCode);
        Assert.Equal(200, (await Request(pipeline, services, "alice")).Response.StatusCode);
    }

    [Theory]
    [InlineData("sub", null, false)]
    [InlineData(null, "fixture", false)]
    [InlineData("", "fixture", false)]
    [InlineData("sub\n", "fixture", false)]
    [InlineData("127.0.0.1", "fixture", true)]
    public void UnauthenticatedOrMalformedClaimsNeverChooseSubjectPartition(string? subject, string? scheme, bool authenticated)
    {
        var context = new DefaultHttpContext(); context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.User = new ClaimsPrincipal(new ClaimsIdentity(subject is null ? [] : [new Claim("sub", subject)], scheme));
        Assert.Equal(authenticated ? "subject:127.0.0.1" : "anonymous-ip:127.0.0.1", ApiRateLimiting.PartitionKey(context));
    }

    private static ServiceProvider Services()
    {
        var services = new ServiceCollection();
        services.AddLogging(); services.AddMetrics(); services.AddRouting(); services.AddAuthorization();
        services.AddAuthentication("fixture").AddScheme<AuthenticationSchemeOptions, FixtureAuthenticationHandler>("fixture", _ => { });
        services.AddApiRateLimiting();
        return services.BuildServiceProvider();
    }

    private static RequestDelegate Pipeline(IServiceProvider services)
    {
        var application = new ApplicationBuilder(services);
        application.UseAuthenticatedApiRateLimiting();
        application.Use(SameOriginWriteProtection.InvokeAsync);
        application.UseAuthorization();
        application.Run(context => { context.Response.StatusCode = 200; return Task.CompletedTask; });
        return application.Build();
    }

    private static async Task<DefaultHttpContext> Request(RequestDelegate pipeline, IServiceProvider services,
        string? subject = null, string ip = "127.0.0.1", string method = "GET", string? origin = null, string path = "/api/v1/fixture")
    {
        await using var scope = services.CreateAsyncScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider, TraceIdentifier = "rate-fixture" };
        context.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        context.Request.Method = method; context.Request.Path = path;
        context.Request.Scheme = "http"; context.Request.Host = new HostString("localhost");
        if (subject is not null) context.Request.Headers["X-Fixture-Subject"] = subject;
        if (origin is not null) context.Request.Headers.Origin = origin;
        context.Response.Body = new MemoryStream();
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask,
            new EndpointMetadataCollection(new EnableRateLimitingAttribute(ApiRateLimiting.PolicyName), new AuthorizeAttribute()), "rate-fixture"));
        await pipeline(context); return context;
    }

    // Test assembly only. Production never registers or accepts this synthetic identity header.
    private sealed class FixtureAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var subject = Request.Headers["X-Fixture-Subject"].ToString();
            if (subject.Length == 0) return Task.FromResult(AuthenticateResult.NoResult());
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", subject)], Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }
}
