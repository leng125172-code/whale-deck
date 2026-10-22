using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Serilog;
using Serilog.Formatting.Json;
using WhaleDeck.Api.Middleware;
using WhaleDeck.Api.Options;
using WhaleDeck.Api.Security;
using WhaleDeck.Infrastructure;

if (WhaleDeck.Contracts.BuildIdentity.TryPrint(args, typeof(Program).Assembly)) return;

Log.Logger = new LoggerConfiguration().Enrich.FromLogContext().WriteTo.Console(new JsonFormatter()).CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestHeadersTotalSize = 64 * 1024);
    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console(new JsonFormatter()));

    builder.Services.AddOptions<OidcSettings>()
        .Bind(builder.Configuration.GetSection(OidcSettings.SectionName))
        .Validate(settings => !settings.Enabled ||
            ValidEndpoint(settings.Internal) && ValidEndpoint(settings.External),
            "Enabled OIDC requires valid internal and external Authority, ClientId and ClientSecret settings.")
        .ValidateOnStart();

    var oidcSettings = builder.Configuration.GetSection(OidcSettings.SectionName).Get<OidcSettings>() ?? new();
    if (!oidcSettings.Enabled && !builder.Environment.IsDevelopment())
    {
        throw new InvalidOperationException("OIDC authentication must be enabled outside Development.");
    }
    if (!builder.Environment.IsDevelopment() && builder.Configuration.GetValue<bool>("Authentication:Authentik:AllowClaimFallback"))
    {
        throw new InvalidOperationException("Authentik claim fallback is only supported in Development.");
    }
    if (!builder.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(
        builder.Configuration["Authentication:Authentik:AdministratorGroupId"]))
    {
        throw new InvalidOperationException("Authentication:Authentik:AdministratorGroupId is required outside Development.");
    }

    builder.Services.AddInfrastructure(builder.Configuration);
    var dataProtection = builder.Services.AddDataProtection().SetApplicationName("WhaleDeck.Api");
    var dataProtectionDirectory = builder.Configuration["DataProtection:KeyDirectory"];
    if (!string.IsNullOrWhiteSpace(dataProtectionDirectory))
    {
        Directory.CreateDirectory(dataProtectionDirectory);
        dataProtection.PersistKeysToFileSystem(new DirectoryInfo(dataProtectionDirectory));
    }
    // The global MVC antiforgery filter depends on ViewFeatures services even
    // though Whale Deck itself exposes controller APIs rather than Razor views.
    builder.Services.AddControllersWithViews(options => options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
    builder.Services.AddProblemDetails();
    builder.Services.AddOpenApi(options => options.AddOperationTransformer((operation, context, _) =>
    {
        if (context.Description.HttpMethod == "PUT" && context.Description.ActionDescriptor.RouteValues.TryGetValue("controller", out var controller) && controller == "Files" &&
            context.Description.ActionDescriptor.RouteValues.TryGetValue("action", out var action) && action == "Content")
        {
            operation.RequestBody = new Microsoft.OpenApi.OpenApiRequestBody
            {
                Required = true,
                Content = new Dictionary<string, Microsoft.OpenApi.OpenApiMediaType>
                {
                    ["application/octet-stream"] = new() { Schema = new Microsoft.OpenApi.OpenApiSchema { Type = Microsoft.OpenApi.JsonSchemaType.String, Format = "binary" } }
                }
            };
        }
        return Task.CompletedTask;
    }));
    builder.Services.AddHealthChecks();
    builder.Services.AddAntiforgery(options =>
    {
        options.Cookie.Name = "whaledeck.xsrf";
        options.Cookie.HttpOnly = false;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.None;
        options.HeaderName = "X-XSRF-TOKEN";
    });
    builder.Services.AddApiRateLimiting();

    if (oidcSettings.Enabled)
    {
        builder.Services.AddSingleton<ServerSideTicketStore>();
        builder.Services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
            .Configure<ServerSideTicketStore>((options, store) => options.SessionStore = store);
        builder.Services.AddAuthentication(options =>
        {
            options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = "WhaleDeckOidc";
        })
        .AddCookie(options =>
        {
            options.Cookie.Name = "whaledeck.session";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = CookieSecurePolicy.None;
            options.SlidingExpiration = true;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.Events.OnRedirectToLogin = context =>
            {
                if (OidcChallengeBehavior.ShouldReturnUnauthorized(context.Request))
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                else
                    context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            };
        })
        .AddPolicyScheme("WhaleDeckOidc", "Whale Deck Authentik endpoint", options =>
            // Returning 401 in the OIDC redirect event is too late: the
            // handler has already minted nonce/correlation cookies. Anonymous
            // API calls must challenge Cookies, never enter the OIDC handler.
            options.ForwardDefaultSelector = context => OidcChallengeBehavior.SelectChallengeScheme(context.Request))
        .AddOpenIdConnect("AuthentikInternal", options => ConfigureOidc(options, oidcSettings.Internal, "AuthentikInternal"))
        .AddOpenIdConnect("AuthentikExternal", options => ConfigureOidc(options, oidcSettings.External, "AuthentikExternal"));
    }
    else
    {
        builder.Services.AddAuthentication(options => options.DefaultScheme = "Development")
            .AddScheme<AuthenticationSchemeOptions, DevelopmentAuthenticationHandler>("Development", _ => { });
    }

    builder.Services.AddScoped<IAuthorizationHandler, AdministratorAuthorizationHandler>();
    builder.Services.AddScoped<TerminalCookieIdentity>();
    builder.Services.AddScoped<WhaleDeck.Api.Files.FileHttpTransport>();
    builder.Services.AddScoped<WhaleDeck.Api.Files.ContainerFileHttpTransport>();
    builder.Services.AddScoped<WhaleDeck.Api.Files.FilePackingHttpTransport>();
    builder.Services.AddScoped<WhaleDeck.Api.Terminals.HostTerminalWebSocket>();
    var authorization = builder.Services.AddAuthorizationBuilder()
        .AddPolicy("Administrator", policy => policy.RequireAuthenticatedUser().AddRequirements(new AdministratorRequirement()));
    authorization.SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedHost | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = 1;
    });

    var app = builder.Build();
    app.UseForwardedHeaders();
    app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });
    // Keep request logging outside the exception handler. Expected conflicts
    // are converted to their final Problem Details response before Serilog
    // observes the status, so a normal 409 is not emitted as a 500 stack trace.
    app.UseSerilogRequestLogging(options => options.GetLevel = (context, _, exception) =>
        exception is not null || context.Response.StatusCode >= 500
            ? Serilog.Events.LogEventLevel.Error
            : Serilog.Events.LogEventLevel.Debug);
    app.UseExceptionHandler(error => error.Run(TraceAndErrors.WriteProblemAsync));
    app.UseAuthenticatedApiRateLimiting();
    app.Use(SameOriginWriteProtection.InvokeAsync);
    // Run before authorization's OIDC challenge so debug clients receive a
    // deterministic denial rather than following redirects to identity pages.
    if (!app.Environment.IsDevelopment()) app.Use(DebugDocumentationProtection.InvokeAsync);
    app.UseAuthorization();
    app.UseAntiforgery();

    app.UseSwaggerUI(options =>
    {
        options.RoutePrefix = "swagger";
        options.SwaggerEndpoint("/openapi/v1.json", "Whale Deck API V0.1.0");
        options.DocumentTitle = "Whale Deck API";
        options.EnablePersistAuthorization();
        options.HeadContent = """
            <script>
            addEventListener('load', async () => {
              try {
                const response = await fetch('/api/v1/auth/csrf', { credentials: 'include' });
                if (!response.ok) return;
                const csrf = await response.json();
                const original = ui.getConfigs().requestInterceptor;
                ui.getConfigs().requestInterceptor = request => {
                  request.headers[csrf.headerName] = csrf.token;
                  return original ? original(request) : request;
                };
              } catch (_) { }
            });
            </script>
            """;
    });

    var openApi = app.MapOpenApi();
    if (app.Environment.IsDevelopment()) openApi.AllowAnonymous(); else openApi.RequireAuthorization("Administrator");
    app.MapHealthChecks("/health/live").AllowAnonymous();
    app.MapControllers().RequireRateLimiting(ApiRateLimiting.PolicyName);
    await app.RunAsync();
}
catch (Exception exception)
{
    Log.Fatal(exception, "WhaleDeck API terminated unexpectedly");
}
finally
{
    await Log.CloseAndFlushAsync();
}

static bool ValidEndpoint(OidcEndpointSettings settings) =>
    Uri.TryCreate(settings.Authority, UriKind.Absolute, out _) &&
    !string.IsNullOrWhiteSpace(settings.BackchannelHost) &&
    settings.BackchannelPort is > 0 and <= 65535 &&
    !string.IsNullOrWhiteSpace(settings.ClientId) &&
    !string.IsNullOrWhiteSpace(settings.ClientSecret);

static void ConfigureOidc(OpenIdConnectOptions options, OidcEndpointSettings settings, string scheme)
{
    options.Authority = settings.Authority;
    // The browser must see the IP-based Authentik issuer, while containers
    // must not hairpin through a host-published port. Connect the OIDC
    // backchannel directly to Authentik but retain the public request URI and
    // Host header so discovery, issuer validation and redirects stay aligned.
    options.BackchannelHttpHandler = new SocketsHttpHandler
    {
        ConnectCallback = async (_, cancellationToken) =>
        {
            var client = new TcpClient();
            try
            {
                await client.ConnectAsync(settings.BackchannelHost, settings.BackchannelPort, cancellationToken);
                return client.GetStream();
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }
    };
    options.ClientId = settings.ClientId;
    options.ClientSecret = settings.ClientSecret;
    options.ResponseType = OpenIdConnectResponseType.Code;
    options.UsePkce = true;
    options.Scope.Add("email");
    options.SaveTokens = false;
    options.GetClaimsFromUserInfoEndpoint = true;
    options.MapInboundClaims = false;
    options.RequireHttpsMetadata = false;
    options.CallbackPath = settings.CallbackPath;
    options.SignedOutCallbackPath = settings.SignedOutCallbackPath;
    // Both approved front doors are HTTP IP addresses. Authentik is served on
    // the same scheme and host (a different port), so Lax remains same-site
    // for the form_post callback without producing unusable Secure cookies.
    options.NonceCookie.SecurePolicy = CookieSecurePolicy.None;
    options.NonceCookie.SameSite = SameSiteMode.Lax;
    options.NonceCookie.Name = $"whaledeck.oidc.{scheme}.nonce.";
    options.NonceCookie.Path = settings.CallbackPath;
    options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.None;
    options.CorrelationCookie.SameSite = SameSiteMode.Lax;
    options.CorrelationCookie.Name = $"whaledeck.oidc.{scheme}.correlation.";
    options.CorrelationCookie.Path = settings.CallbackPath;
    options.TokenValidationParameters.NameClaimType = "name";
    options.TokenValidationParameters.RoleClaimType = "groups";
    options.Events.OnRedirectToIdentityProvider = context =>
    {
        if (OidcChallengeBehavior.ShouldReturnUnauthorized(context.Request))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.HandleResponse();
            return Task.CompletedTask;
        }

        var host = context.Request.Host.Host;
        var approvedHostName = string.Equals(host, "precision-7920-tower.local", StringComparison.OrdinalIgnoreCase);
        if (!approvedHostName &&
            (!IPAddress.TryParse(host, out var address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)))
        {
            throw new InvalidOperationException("Whale Deck OIDC requires an approved workstation IP address or mDNS host name.");
        }

        context.ProtocolMessage.IssuerAddress = new UriBuilder(context.ProtocolMessage.IssuerAddress)
        {
            Host = host,
            Port = 8081
        }.Uri.AbsoluteUri;
        return Task.CompletedTask;
    };
    options.Events.OnTokenValidated = context =>
    {
        if (context.Principal?.Identity is ClaimsIdentity identity)
            identity.AddClaim(new Claim("whaledeck:oidc-scheme", scheme));
        // Only the ID token is needed for RP-initiated logout. SaveTokens stays
        // false, so access/refresh tokens are not retained. The cookie ticket
        // store above keeps this token in Valkey, never in browser storage.
        var idToken = context.TokenEndpointResponse?.IdToken ?? context.ProtocolMessage.IdToken;
        if (string.IsNullOrWhiteSpace(idToken)) throw new InvalidOperationException("OIDC did not supply a logout ID token.");
        (context.Properties ??= new AuthenticationProperties()).StoreTokens([new AuthenticationToken { Name = "id_token", Value = idToken }]);
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToIdentityProviderForSignOut = context =>
    {
        // Match the browser's actual workstation NIC, just like login does.
        var host = context.Request.Host.Host;
        if (!string.Equals(host, "precision-7920-tower.local", StringComparison.OrdinalIgnoreCase) &&
            (!IPAddress.TryParse(host, out var address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)))
            throw new InvalidOperationException("Whale Deck OIDC requires an approved workstation IP address or mDNS host name.");
        context.ProtocolMessage.IssuerAddress = new UriBuilder(context.ProtocolMessage.IssuerAddress)
        {
            Host = host,
            Port = 8081
        }.Uri.AbsoluteUri;
        return Task.CompletedTask;
    };
    options.Events.OnRemoteFailure = context =>
    {
        OidcTransientCookieCleanup.Apply(context.Request, context.Response, settings.CallbackPath);
        context.Response.Redirect("/login?reason=auth-expired");
        context.HandleResponse();
        return Task.CompletedTask;
    };
}

public partial class Program;
