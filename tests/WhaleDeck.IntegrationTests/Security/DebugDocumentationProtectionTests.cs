using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using WhaleDeck.Api.Middleware;

namespace WhaleDeck.IntegrationTests.Security;

public sealed class DebugDocumentationProtectionTests
{
    [Theory]
    [InlineData("/swagger/index.html", false, false, 401)]
    [InlineData("/openapi/v1.json", false, false, 401)]
    [InlineData("/swagger/swagger-ui-bundle.js", true, false, 403)]
    [InlineData("/openapi/v1.json", true, false, 403)]
    [InlineData("/swagger/index.html", true, true, 200)]
    [InlineData("/openapi/v1.json", true, true, 200)]
    [InlineData("/api/v1/auth/login", false, false, 200)]
    [InlineData("/swagger-like", false, false, 200)]
    public async Task DebugRoutesHaveDeterministicAuthorizationWithoutRedirects(
        string path, bool authenticated, bool administrator, int expectedStatus)
    {
        var authorization = new StubAuthorization(administrator);
        await using var services = new ServiceCollection().AddSingleton<IAuthorizationService>(authorization).BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services, TraceIdentifier = "debug-trace" };
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        if (authenticated) context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "test-subject")], "test-cookie"));
        var reached = false;
        await DebugDocumentationProtection.InvokeAsync(context, _ => { reached = true; return Task.CompletedTask; });

        Assert.Equal(expectedStatus, context.Response.StatusCode);
        Assert.Equal(expectedStatus == 200, reached);
        Assert.False(context.Response.Headers.ContainsKey("Location"));
        if (expectedStatus != 200)
        {
            Assert.StartsWith("application/problem+json", context.Response.ContentType);
            context.Response.Body.Position = 0;
            using var document = await JsonDocument.ParseAsync(context.Response.Body);
            Assert.Equal(authenticated ? "AUTH_FORBIDDEN" : "AUTH_REQUIRED", document.RootElement.GetProperty("code").GetString());
            Assert.Equal("debug-trace", document.RootElement.GetProperty("traceId").GetString());
        }
    }

    private sealed class StubAuthorization(bool administrator) : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName)
        {
            Assert.Equal("Administrator", policyName);
            return Task.FromResult(administrator ? AuthorizationResult.Success() : AuthorizationResult.Failed());
        }
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements) =>
            throw new NotSupportedException();
    }
}
