using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WhaleDeck.Api.Controllers;
using WhaleDeck.Api.Files;
using WhaleDeck.Api.Middleware;
using WhaleDeck.Api.Security;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Agent;
using WhaleDeck.Infrastructure.Identity;

namespace WhaleDeck.IntegrationTests.Security;

/// <summary>Real MVC, protected opaque Cookie and CSRF over loopback; synthetic directory/Agent/SQL, not installed SSO evidence.</summary>
public sealed partial class ContainerUploadHttpTests
{
    private const string Cid = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Instance = "777:2026-10-27T01:02:03.123456789Z";
    private static string Base => $"/api/v1/containers/{Cid}/files/uploads";
    private static readonly byte[] Data = [0, 255, 1, 2, 3];

    [Fact]
    public async Task GeneratedOpenApiDescribesBinaryReceiveWithoutWideningOtherOperations()
    {
        await using var fixture = await Fixture.Start(); using var client = await fixture.Client("anonymous",false);
        using var json = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var paths = json.RootElement.GetProperty("paths");
        var content = paths.GetProperty("/api/v1/containers/{containerId}/files/uploads/{id}/content").GetProperty("put");
        var body = content.GetProperty("requestBody"); Assert.True(body.GetProperty("required").GetBoolean());
        var media = Assert.Single(body.GetProperty("content").EnumerateObject()); Assert.Equal("application/octet-stream",media.Name);
        Assert.Equal("binary",media.Value.GetProperty("schema").GetProperty("format").GetString());
        Assert.True(content.GetProperty("responses").TryGetProperty("202",out _));
        Assert.False(paths.GetProperty("/api/v1/containers/{containerId}/files/uploads/{id}").GetProperty("get").TryGetProperty("requestBody",out _));
        var plan = paths.GetProperty("/api/v1/containers/{containerId}/files/uploads/plan").GetProperty("post");
        Assert.False(plan.GetProperty("requestBody").GetProperty("content").TryGetProperty("application/octet-stream",out _));
    }

    [Fact]
    public async Task NonCanonicalIdentifiersNeverReachTheService()
    {
        await using var fixture = await Fixture.Start(); using var client = await fixture.Client("admin-subject",true); var token = await Csrf(client);
        foreach (var cid in new[] { new string('A',64),new string('a',63),new string('a',63)+"%0A","ordinary-name" })
        {
            using var request = Write(client,HttpMethod.Post,$"/api/v1/containers/{cid}/files/uploads/plan",token,JsonContent.Create(Plan()));
            using var response = await client.SendAsync(request); Assert.Equal(HttpStatusCode.NotFound,response.StatusCode);
        }
        foreach (var id in new[] { "AA0A8865-343E-49BD-B01A-72680131BF39","aa0a8865343e49bdb01a72680131bf39","aa0a8865-343e-49bd-b01a-72680131bf3%0A" })
        { using var response = await client.GetAsync($"{Base}/{id}"); Assert.Equal(HttpStatusCode.NotFound,response.StatusCode); }
        Assert.Equal(0,fixture.Gateway.Inspections); Assert.Equal(0,fixture.Repository.Enqueued);
    }

    [Theory]
    [InlineData(null)][InlineData(1073741825L)]
    public async Task MissingAndOverBudgetContentLengthsRejectBeforeReadingIdentityOrBytes(long? length)
    {
        var context = new DefaultHttpContext(); var id = Guid.NewGuid(); context.Request.Headers["Idempotency-Key"] = id.ToString("D");
        context.Request.ContentLength = length;
        var controller = new ContainerUploadsController(null!,null!) { ControllerContext = new() { HttpContext = context } };
        Assert.IsType<BadRequestResult>((await controller.Content(Cid,id,default)).Result);
    }

    [Fact]
    public async Task RealCookieCsrfPlanRegisterAndRawReceiveOnlyStageWithRouteBoundOwnership()
    {
        await using var fixture = await Fixture.Start(); using var client = await fixture.Client("admin-subject",true);
        var token = await Csrf(client); var intent = await Create(client,token);
        Assert.Equal("AwaitingUpload",intent.State); Assert.Equal(intent.Id,intent.JobId); Assert.Equal(0,fixture.Gateway.Stages);
        using var body = new ByteArrayContent(Data); body.Headers.ContentType = new("application/octet-stream");
        using var request = Write(client,HttpMethod.Put,$"{Base}/{intent.Id:D}/content",token,body,intent.Id.ToString("D"));
        using var response = await client.SendAsync(request); Assert.Equal(HttpStatusCode.Accepted,response.StatusCode);
        Assert.Contains("no-store",response.Headers.CacheControl!.ToString(),StringComparison.OrdinalIgnoreCase);
        var ready = (await response.Content.ReadFromJsonAsync<ContainerFileUploadIntentDto>())!;
        Assert.Equal("Ready",ready.State); Assert.Equal(Data,fixture.Gateway.Bytes); Assert.Equal(0,fixture.Gateway.Publications);
        Assert.Equal($"{Base}/{intent.Id:D}",response.Headers.Location!.OriginalString);
        using var status = await client.GetAsync($"{Base}/{intent.Id:D}"); Assert.Equal(HttpStatusCode.OK,status.StatusCode);
        var wrongCid = new string('b',64);
        using var wrong = Write(client,HttpMethod.Post,$"/api/v1/containers/{wrongCid}/files/uploads/{intent.Id:D}/cancel",token,null,intent.Id.ToString("D"));
        using var rejected = await client.SendAsync(wrong); Assert.Equal(HttpStatusCode.NotFound,rejected.StatusCode); Assert.False(fixture.Repository.Canceled);
        using var cancel = Write(client,HttpMethod.Post,$"{Base}/{intent.Id:D}/cancel",token,null,intent.Id.ToString("D"));
        using var accepted = await client.SendAsync(cancel); Assert.Equal(HttpStatusCode.Accepted,accepted.StatusCode); Assert.True(fixture.Repository.Canceled);
    }

    [Theory]
    [InlineData("anonymous",false,HttpStatusCode.Unauthorized)]
    [InlineData("ordinary-subject",false,HttpStatusCode.Forbidden)]
    public async Task AnonymousAndOrdinaryCookiesCannotInspectOrRegister(string subject,bool administrator,HttpStatusCode expected)
    {
        await using var fixture=await Fixture.Start(); using var client=await fixture.Client(subject,administrator);
        using var get=await client.GetAsync($"{Base}/aa0a8865-343e-49bd-b01a-72680131bf39"); Assert.Equal(expected,get.StatusCode);
        var token=await Csrf(client);
        using var request=Write(client,HttpMethod.Post,Base+"/plan",token,JsonContent.Create(Plan()));
        using var response=await client.SendAsync(request); Assert.Equal(expected,response.StatusCode);
        Assert.Equal(0,fixture.Gateway.Inspections); Assert.Equal(0,fixture.Repository.Enqueued);
    }

    [Theory]
    [InlineData("csrf")][InlineData("origin")][InlineData("missing-origin")]
    public async Task UnsafeRequestsRejectMissingCsrfAndWrongOriginsBeforeAgentAccess(string variant)
    {
        await using var fixture=await Fixture.Start(); using var client=await fixture.Client("admin-subject",true); var token=await Csrf(client);
        using var request=Write(client,HttpMethod.Post,Base+"/plan",variant=="csrf"?null:token,JsonContent.Create(Plan()));
        if (variant=="origin") request.Headers.Remove("Origin");
        if (variant=="origin") request.Headers.Add("Origin","http://unapproved.invalid");
        if (variant=="missing-origin") request.Headers.Remove("Origin");
        using var response=await client.SendAsync(request); Assert.Contains(response.StatusCode,new[] { HttpStatusCode.BadRequest,HttpStatusCode.Forbidden });
        Assert.Equal(0,fixture.Gateway.Inspections); Assert.Equal(0,fixture.Repository.Enqueued);
    }

    [Fact]
    public async Task RemovedOpaqueCookieDuringStagingCannotMarkTheIntentReady()
    {
        await using var fixture=await Fixture.Start(); using var client=await fixture.Client("admin-subject",true); var token=await Csrf(client); var intent=await Create(client,token);
        fixture.Gateway.AfterStage=()=>fixture.Store.RemoveAsync(fixture.AdminKey!);
        using var body=new ByteArrayContent(Data); body.Headers.ContentType=new("application/octet-stream");
        using var request=Write(client,HttpMethod.Put,$"{Base}/{intent.Id:D}/content",token,body,intent.Id.ToString("D"));
        using var response=await client.SendAsync(request); Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
        Assert.Equal(1,fixture.Gateway.Stages); Assert.Equal(0,fixture.Repository.ReadyCalls); Assert.Equal(0,fixture.Gateway.Publications);
        using var status=await client.GetAsync($"{Base}/{intent.Id:D}"); Assert.Equal(HttpStatusCode.Unauthorized,status.StatusCode);
    }

    [Fact]
    public async Task SignedCookieWithStaleAdministratorClaimsCannotBypassTheCurrentDirectory()
    {
        await using var fixture=await Fixture.Start(); using var client=await fixture.Client("admin-subject",true); var token=await Csrf(client); fixture.Directory.Admin=false;
        using var request=Write(client,HttpMethod.Post,Base+"/plan",token,JsonContent.Create(Plan()));
        using var response=await client.SendAsync(request); Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode); Assert.Equal(0,fixture.Gateway.Inspections);
    }

    [Theory]
    [InlineData("encoding")][InlineData("range")][InlineData("content-range")][InlineData("key")][InlineData("type")]
    public async Task RawTransportRejectsUnsupportedHeadersAndMediaTypesWithoutReceiving(string variant)
    {
        await using var fixture=await Fixture.Start(); using var client=await fixture.Client("admin-subject",true); var token=await Csrf(client); var intent=await Create(client,token);
        using var body=new ByteArrayContent(Data); body.Headers.ContentType=new(variant=="type"?"application/json":"application/octet-stream");
        if (variant=="encoding") body.Headers.ContentEncoding.Add("identity");
        if (variant=="content-range") body.Headers.TryAddWithoutValidation("Content-Range","bytes 0-4/5");
        using var request=Write(client,HttpMethod.Put,$"{Base}/{intent.Id:D}/content",token,body,variant=="key"?Guid.NewGuid().ToString("D"):intent.Id.ToString("D"));
        if (variant=="range") request.Headers.Range=new(0,4);
        using var response=await client.SendAsync(request); Assert.Equal(variant=="type"?HttpStatusCode.UnsupportedMediaType:HttpStatusCode.BadRequest,response.StatusCode);
        Assert.Equal(0,fixture.Gateway.Stages); Assert.Equal(0,fixture.Repository.ReadyCalls);
    }
    private static ContainerUploadPlanBody Plan() => new(Instance,"private/file.bin",Data.Length,Convert.ToHexString(SHA256.HashData(Data)),"ABSENT");
    private static HttpRequestMessage Write(HttpClient client,HttpMethod method,string path,string? csrf,HttpContent? content=null,string? key=null)
    {
        var request=new HttpRequestMessage(method,path) { Content=content }; request.Headers.Add("Origin",client.BaseAddress!.GetLeftPart(UriPartial.Authority));
        if (csrf is not null) request.Headers.Add("X-CSRF-TOKEN",csrf); if (key is not null) request.Headers.Add("Idempotency-Key",key); return request;
    }
    private static async Task<string> Csrf(HttpClient client) => (await client.GetFromJsonAsync<Token>("/test/csrf"))!.Value;
    private sealed record Token(string Value);
    private static async Task<ContainerFileUploadIntentDto> Create(HttpClient client,string csrf)
    {
        using var planRequest=Write(client,HttpMethod.Post,Base+"/plan",csrf,JsonContent.Create(Plan())); using var planResponse=await client.SendAsync(planRequest);
        Assert.Equal(HttpStatusCode.OK,planResponse.StatusCode); var plan=(await planResponse.Content.ReadFromJsonAsync<FileUploadPlanDto>())!;
        var key=Guid.NewGuid().ToString("D"); using var create=Write(client,HttpMethod.Post,Base,csrf,JsonContent.Create(new SubmitFileUploadCommand(plan.PlanHash,true,key)),key);
        using var response=await client.SendAsync(create); Assert.Equal(HttpStatusCode.Accepted,response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ContainerFileUploadIntentDto>())!;
    }
    private sealed class Fixture : IAsyncDisposable
    {
        private WebApplication _app=null!; private readonly string _key=Path.GetTempFileName();
        public ServerSideTicketStore Store { get; private set; }=null!; public string? AdminKey { get; private set; }
        public Gateway Gateway { get; }=new(); public Repository Repository { get; }=new(); public Directory Directory { get; }=new();
        public HistoryFixture History { get; }=new();
        public static async Task<Fixture> Start()
        {
            var fixture=new Fixture(); await File.WriteAllBytesAsync(fixture._key,RandomNumberGenerator.GetBytes(32));
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(fixture._key,UnixFileMode.UserRead|UnixFileMode.UserWrite);
            var provider=new EphemeralDataProtectionProvider(); var sessions=new MemoryBffSessionStorage(TimeProvider.System);
            fixture.Store=new(sessions,provider,TimeProvider.System);
            var builder=WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName="Testing" }); builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options=>options.Listen(IPAddress.Loopback,0)); builder.Services.AddDataProtection();
            builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options=>
            {
                options.Cookie.Name="whaledeck.session"; options.SessionStore=fixture.Store;
                options.TicketDataFormat=new TicketDataFormat(provider.CreateProtector("container-upload-http-cookie"));
                options.Events.OnRedirectToLogin=context=> { context.Response.StatusCode=401; return Task.CompletedTask; };
                options.Events.OnRedirectToAccessDenied=context=> { context.Response.StatusCode=403; return Task.CompletedTask; };
            });
            builder.Services.AddAuthorization(options=>options.AddPolicy("Administrator",policy=>policy.RequireAuthenticatedUser().RequireClaim("groups","administrators")));
            builder.Services.AddAntiforgery(options=>options.HeaderName="X-CSRF-TOKEN");
            builder.Services.AddControllersWithViews(options=>options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute())).AddApplicationPart(typeof(ContainerUploadsController).Assembly);
            builder.Services.AddOpenApi(options=>options.AddOperationTransformer(FileUploadOpenApi.TransformAsync));
            builder.Services.AddSingleton<TerminalCookieIdentity>();
            var protector=new FilePlanProtector(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["Agent:CapabilityKeyFile"]=fixture._key }).Build());
            var authorization=new FileManagementService(null!,null!,protector,fixture.Directory,TimeProvider.System);
            var uploads=new ContainerFileUploadService(authorization,sessions,fixture.Gateway,fixture.Repository,protector,TimeProvider.System,fixture.History);
            builder.Services.AddSingleton(uploads);
            builder.Services.AddSingleton(new ContainerFileHistoryService(uploads,fixture.History,fixture.History,fixture.History));
            builder.Services.AddSingleton(new ContainerFileRestoreService(uploads,fixture.Gateway,fixture.History,fixture.History,protector,fixture.Repository,TimeProvider.System));
            fixture._app=builder.Build(); fixture._app.UseExceptionHandler(error=>error.Run(TraceAndErrors.WriteProblemAsync));
            fixture._app.UseAuthentication(); fixture._app.Use(SameOriginWriteProtection.InvokeAsync); fixture._app.UseAuthorization(); fixture._app.UseAntiforgery();
            fixture._app.MapControllers(); fixture._app.MapOpenApi(); fixture._app.MapGet("/test/csrf",(HttpContext context,IAntiforgery antiforgery)=>new Token(antiforgery.GetAndStoreTokens(context).RequestToken!));
            await fixture._app.StartAsync(); return fixture;
        }
        public async Task<HttpClient> Client(string subject,bool admin)
        {
            var address=new Uri(Assert.Single(_app.Urls)); var cookies=new CookieContainer();
            if (subject!="anonymous")
            {
                List<Claim> claims=[new("sub",subject),new(ClaimTypes.NameIdentifier,subject)]; if (admin) claims.Add(new("groups","administrators"));
                var ticket=new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims,"Cookies")),new() { ExpiresUtc=DateTimeOffset.UtcNow.AddMinutes(10) },"Cookies");
                var key=await Store.StoreAsync(ticket); if (admin) AdminKey=key;
                var format=_app.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get("Cookies").TicketDataFormat;
                var reference=new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new Claim("Microsoft.AspNetCore.Authentication.Cookies-SessionId",key)],"Cookies")),new(),"Cookies");
                cookies.Add(address,new Cookie("whaledeck.session",format.Protect(reference),"/"));
            }
            return new(new SocketsHttpHandler { UseProxy=false,UseCookies=true,CookieContainer=cookies,AllowAutoRedirect=false }) { BaseAddress=address };
        }
        public async ValueTask DisposeAsync() { await _app.DisposeAsync(); File.Delete(_key); }
    }
    private sealed class Directory : IIdentityDirectory
    {
        public bool Admin { get; set; }=true;
        public Task<CurrentUserDto> ResolveCurrentAsync(string subject,string? name,IReadOnlyCollection<string> groups,CancellationToken cancellationToken)=>
            Task.FromResult(new CurrentUserDto(subject,name,groups,Admin&&subject=="admin-subject",DateTimeOffset.UtcNow.AddHours(1)));
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListUsersAsync(CancellationToken cancellationToken)=>throw new NotSupportedException();
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListGroupsAsync(CancellationToken cancellationToken)=>throw new NotSupportedException();
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListSsoApplicationsAsync(CancellationToken cancellationToken)=>throw new NotSupportedException();
    }
    private sealed class Gateway : IContainerFileWriteGateway
    {
        public int Inspections { get; private set; } public int Stages { get; private set; } public int Publications { get; private set; }
        public byte[] Bytes { get; private set; }=[]; public Func<Task>? AfterStage { get; set; }
        public bool RestorationUnavailable { get; set; }
        public async Task RequireContainerRestorationWorkflowAsync(CancellationToken cancellationToken)
        {
            await RequireContainerUploadWorkflowAsync(cancellationToken);
            if (RestorationUnavailable) throw new InvalidOperationException("CONTAINER_RESTORE_CAPABILITY_UNAVAILABLE");
        }
        public Task RequireContainerUploadWorkflowAsync(CancellationToken cancellationToken)=>Task.CompletedTask;
        public Task<ContainerFileUploadTargetDto> InspectContainerUploadTargetAsync(ContainerFileLocationDto location,CancellationToken cancellationToken)
        { Inspections++; return Task.FromResult(new ContainerFileUploadTargetDto(location,new('B',64),new('C',64),"ABSENT",0)); }
        public async Task<ContainerFileUploadReceiptDto> StageContainerFileAsync(ContainerFileStageDto request,IAsyncEnumerable<ReadOnlyMemory<byte>> bytes,CancellationToken cancellationToken)
        {
            Stages++; using var stream=new MemoryStream(); await foreach(var chunk in bytes.WithCancellation(cancellationToken)) await stream.WriteAsync(chunk,cancellationToken);
            Bytes=stream.ToArray(); Assert.Equal(request.SizeBytes,Bytes.LongLength); Assert.Equal(request.Sha256,Convert.ToHexString(SHA256.HashData(Bytes)));
            if (AfterStage is not null) await AfterStage();
            return new(request.Location.ContainerId,request.Location.InstanceId,request.OperationId,request.ExpectedTargetProof,new("Staged",request.SizeBytes,request.Sha256,"",false,false,false),false);
        }
        public Task<ContainerFileUploadReceiptDto> PublishContainerFileAsync(ContainerFileUploadReferenceDto request,CancellationToken cancellationToken)
        { Publications++; throw new NotSupportedException(); }
        public Task<ContainerFileUploadReceiptDto?> InspectContainerUploadAsync(ContainerFileUploadReferenceDto request,CancellationToken cancellationToken)=>throw new NotSupportedException();
        public Task<ContainerFileUploadReceiptDto> DiscardContainerUploadAsync(ContainerFileUploadReferenceDto request,CancellationToken cancellationToken)=>throw new NotSupportedException();
        public Task<ContainerFileUploadReceiptDto> PreventContainerUploadAsync(ContainerFileUploadReferenceDto request,CancellationToken cancellationToken)=>throw new NotSupportedException();
    }
    private sealed class Repository : IContainerFileUploadRepository
    {
        public ContainerFileUploadOperation? Row => _row;
        private ContainerFileUploadOperation? _row; public bool Canceled { get; private set; } public int Enqueued { get; private set; } public int ReadyCalls { get; private set; }
        public Task<ContainerFileUploadOperation?> OwnedAsync(TerminalActor actor,Guid id,CancellationToken cancellationToken)=>
            Task.FromResult(_row is { } row&&row.Id==id&&row.ActorSubject==actor.Subject&&row.LoginBinding==actor.LoginBinding?row:null);
        public Task<ContainerFileUploadOperation> EnqueueAsync(ContainerFileUploadOperation row,CancellationToken cancellationToken)
        { Enqueued++; _row=row; return Task.FromResult(row); }
        public Task<ContainerFileUploadOperation> BeginReceiveAsync(TerminalActor actor,Guid id,DateTimeOffset now,CancellationToken cancellationToken)
        { Assert.Equal("AwaitingUpload",_row!.State); _row.State="Receiving"; _row.ReceiveToken=Guid.NewGuid(); return Task.FromResult(_row); }
        public Task<bool> BeginRestoreReceiveAsync(ContainerFileUploadOperation row, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> MayReceiveAsync(Guid id,Guid token,DateTimeOffset now,CancellationToken cancellationToken)=>Task.FromResult(!Canceled);
        public Task<bool> ReadyAsync(ContainerFileUploadOperation row,CancellationToken cancellationToken)
        { ReadyCalls++; _row!.State="Ready"; return Task.FromResult(true); }
        public Task CancelAsync(TerminalActor actor,Guid id,CancellationToken cancellationToken) { Canceled=true; return Task.CompletedTask; }
        public Task<ContainerFileUploadOperation?> ClaimAsync(DateTimeOffset now,CancellationToken cancellationToken)=>throw new NotSupportedException();
        public Task<bool> RenewAsync(ContainerFileUploadOperation row,DateTimeOffset now,CancellationToken cancellationToken)=>throw new NotSupportedException();
        public Task<bool> CancellationRequestedAsync(Guid id,CancellationToken cancellationToken)=>Task.FromResult(Canceled);
        public Task<ContainerFileUploadStartResult> StartedAsync(ContainerFileUploadOperation row,DateTimeOffset now,CancellationToken cancellationToken)=>throw new NotSupportedException();
        public Task<bool> FinishAsync(ContainerFileUploadOperation row,string state,string code,ContainerFileUploadReceiptDto? receipt,CancellationToken cancellationToken)=>throw new NotSupportedException();
        public Task DeferAsync(ContainerFileUploadOperation row,CancellationToken cancellationToken)=>throw new NotSupportedException();
    }
}
