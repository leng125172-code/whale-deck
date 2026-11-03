using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.IntegrationTests.Security;

/// <summary>Actual loopback MVC/Cookie/CSRF. Identity, Agent and repository are explicit fixtures, not Authentik/SQL/disk evidence.</summary>
public sealed partial class ContainerUploadHttpTests
{
    private static string AclBase => $"/api/v1/containers/{Cid}/files/acls";
    private static readonly string[] AclSuffixes = ["", "/by-key/aa0a8865-343e-49bd-b01a-72680131bf39", "/aa0a8865-343e-49bd-b01a-72680131bf39", "/aa0a8865-343e-49bd-b01a-72680131bf39/confirm", "/aa0a8865-343e-49bd-b01a-72680131bf39/cancel"];
    private static object AclBody => new { instanceId = Instance, path = "work/tree", aclType="access", entries=new[] {new ContainerAclEntryDto("user_obj",null,6),new("user",0,0),new("group_obj",null,4),new("mask",null,6),new("other",null,0)}, expectedVersion = new string('A',64) };
    [Theory][InlineData("anonymous", false, HttpStatusCode.Unauthorized)][InlineData("ordinary-subject", false, HttpStatusCode.Forbidden)]
    public async Task AclEveryRouteRejectsUnprivilegedCookiesBeforeService(string subject, bool admin, HttpStatusCode expected)
    {
        await using var fixture = await Fixture.Start(); using var client = await fixture.Client(subject, admin); var token = await Csrf(client);
        foreach (var suffix in AclSuffixes)
        {
            var write = suffix == "" || suffix.EndsWith("confirm", StringComparison.Ordinal) || suffix.EndsWith("cancel", StringComparison.Ordinal);
            using var request = write ? Write(client,HttpMethod.Post,AclBase+suffix,token,JsonContent.Create(AclBody)) : new HttpRequestMessage(HttpMethod.Get,AclBase+suffix);
            using var response = await client.SendAsync(request); Assert.Equal(expected,response.StatusCode);
        }
        const string recovery = "/api/v1/files/container-acls/recovery";
        foreach (var suffix in new[] { "", "/aa0a8865-343e-49bd-b01a-72680131bf39", "/aa0a8865-343e-49bd-b01a-72680131bf39/acknowledge", "/aa0a8865-343e-49bd-b01a-72680131bf39/restore-source" })
        {
            using var request = suffix.EndsWith("acknowledge",StringComparison.Ordinal) ? Write(client,HttpMethod.Post,recovery+suffix,token,JsonContent.Create(new { version=0,confirmed=true })) : new HttpRequestMessage(HttpMethod.Get,recovery+suffix);
            using var response = await client.SendAsync(request); Assert.Equal(expected,response.StatusCode);
        }
        Assert.Equal(0,fixture.Acl.Requires); Assert.Equal(0,fixture.Acl.Enqueued);
    }
    [Theory][InlineData("csrf")][InlineData("origin")][InlineData("missing-origin")]
    public async Task AclWritesRejectInvalidCsrfOrOrigin(string variant)
    {
        await using var fixture = await Fixture.Start(); using var client = await fixture.Client("admin-subject",true); var token = await Csrf(client);
        foreach (var suffix in new[] { "", "/aa0a8865-343e-49bd-b01a-72680131bf39/confirm", "/aa0a8865-343e-49bd-b01a-72680131bf39/cancel" })
        {
            using var request = Write(client,HttpMethod.Post,AclBase+suffix,variant=="csrf" ? null:token,JsonContent.Create(AclBody));
            if (variant != "csrf") request.Headers.Remove("Origin");
            if (variant == "origin") request.Headers.Add("Origin","http://unapproved.invalid");
            using var response = await client.SendAsync(request); Assert.Contains(response.StatusCode,new[] { HttpStatusCode.BadRequest,HttpStatusCode.Forbidden });
        }
        using var ack = Write(client,HttpMethod.Post,"/api/v1/files/container-acls/recovery/aa0a8865-343e-49bd-b01a-72680131bf39/acknowledge",null,JsonContent.Create(new {version=0,confirmed=true}));
        using var denied = await client.SendAsync(ack); Assert.Equal(HttpStatusCode.BadRequest,denied.StatusCode);
        Assert.Equal(0,fixture.Acl.Requires); Assert.Equal(0,fixture.Acl.Enqueued);
    }
    [Fact]
    public async Task AclRealMvcQueuesOnlyMetadataAndResolvesSameOriginalLoginKey()
    {
        await using var fixture = await Fixture.Start(); using var client = await fixture.Client("admin-subject",true); var token = await Csrf(client); var key = Guid.NewGuid().ToString("D");
        async Task<ContainerAclIntentDto> Queue()
        {
            using var request = Write(client,HttpMethod.Post,AclBase,token,JsonContent.Create(AclBody),key); using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Accepted,response.StatusCode); Assert.Contains("no-store",response.Headers.CacheControl!.ToString(),StringComparison.OrdinalIgnoreCase);
            var row = (await response.Content.ReadFromJsonAsync<ContainerAclIntentDto>())!;
            Assert.Equal($"{AclBase}/{row.Id:D}",response.Headers.Location!.OriginalString); return row;
        }
        var first = await Queue(); var replay = await Queue(); Assert.Equal(first.Id,replay.Id); Assert.Equal("Queued",first.State); Assert.Null(first.Receipt); Assert.Null(first.Confirmation);
        Assert.Equal(1,fixture.Acl.Enqueued); Assert.Equal(1,fixture.Acl.Requires);
        using var query = await client.GetAsync(AclBase+"/by-key/"+key); Assert.Equal(HttpStatusCode.OK,query.StatusCode);
        Assert.Equal(first.Id,(await query.Content.ReadFromJsonAsync<ContainerAclIntentDto>())!.Id);
        using var otherLogin = await fixture.Client("admin-subject",true);
        using var foreign = await otherLogin.GetAsync($"{AclBase}/{first.Id:D}"); Assert.Equal(HttpStatusCode.NotFound,foreign.StatusCode);
        fixture.Acl.Allowed = false;
        using var revoked = await client.GetAsync($"{AclBase}/{first.Id:D}"); Assert.Equal(HttpStatusCode.Forbidden,revoked.StatusCode);
        fixture.Acl.Allowed = true;
        using var cancel = Write(client,HttpMethod.Post,$"{AclBase}/{first.Id:D}/cancel",token,null,first.Id.ToString("D")); using var canceled = await client.SendAsync(cancel);
        Assert.Equal(HttpStatusCode.Accepted,canceled.StatusCode); Assert.Equal("Canceled",(await canceled.Content.ReadFromJsonAsync<ContainerAclIntentDto>())!.State);
        Assert.Equal(0,fixture.Acl.Mutations);
    }
    [Fact]
    public async Task AclMvcMalformedBodyNeverEchoesInputOrCallsDependencies()
    {
        await using var fixture = await Fixture.Start(); using var client = await fixture.Client("admin-subject",true); var token = await Csrf(client);
        using var body = new StringContent("{\"uid\":0,\"uid\":\"private-must-not-leak\"}",System.Text.Encoding.UTF8,"application/json");
        using var request = Write(client,HttpMethod.Post,AclBase,token,body,Guid.NewGuid().ToString("D")); using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode); var content = await response.Content.ReadAsStringAsync(); Assert.Contains("CONTAINER_ACL_INPUT_INVALID",content,StringComparison.Ordinal); Assert.DoesNotContain("private-must-not-leak",content,StringComparison.Ordinal);
        Assert.Equal(0,fixture.Acl.Requires); Assert.Equal(0,fixture.Acl.Enqueued);
    }
    [Theory][InlineData("id")][InlineData("permissions")]
    public async Task AclMvcOmittedEntryFieldCannotBecomeImplicitZero(string field)
    {
        await using var fixture=await Fixture.Start();using var client=await fixture.Client("admin-subject",true);var token=await Csrf(client);
        var body=System.Text.Json.JsonSerializer.SerializeToNode(AclBody,System.Text.Json.JsonSerializerOptions.Web)!.AsObject();body["entries"]![1]!.AsObject().Remove(field);
        using var request=Write(client,HttpMethod.Post,AclBase,token,JsonContent.Create(body),Guid.NewGuid().ToString("D"));using var response=await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);Assert.Equal(0,fixture.Acl.Enqueued);Assert.Equal(0,fixture.Acl.Requires);
    }
    [Fact]
    public async Task AclMvcExplicitIdZeroAndNoPermissionsAreRetainedExactly()
    {
        await using var fixture=await Fixture.Start();using var client=await fixture.Client("admin-subject",true);var token=await Csrf(client);
        using var request=Write(client,HttpMethod.Post,AclBase,token,JsonContent.Create(AclBody),Guid.NewGuid().ToString("D"));using var response=await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Accepted,response.StatusCode);var row=(await response.Content.ReadFromJsonAsync<ContainerAclIntentDto>())!;
        Assert.Equal((0U,0U),(row.Command.Entries[1].Id,row.Command.Entries[1].Permissions));Assert.Equal(0,fixture.Acl.Mutations);
    }
    [Theory][InlineData("zero")][InlineData("uppercase")][InlineData("compact")][InlineData("duplicate")]
    public async Task AclMvcRequiresOneCanonicalRegistrationKeyBeforeCapabilityAccess(string variant)
    {
        await using var fixture=await Fixture.Start();using var client=await fixture.Client("admin-subject",true);var token=await Csrf(client);var id=Guid.Parse("ac12ac12-ac12-4c12-ac12-ac12ac12ac12");
        var key=variant switch {"zero"=>Guid.Empty.ToString("D"),"uppercase"=>id.ToString("D").ToUpperInvariant(),"compact"=>id.ToString("N"),_=>id.ToString("D")};
        using var request=Write(client,HttpMethod.Post,AclBase,token,JsonContent.Create(AclBody),key);
        if(variant=="duplicate")request.Headers.Add("Idempotency-Key",key);
        using var response=await client.SendAsync(request);Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);Assert.Equal(0,fixture.Acl.Requires);Assert.Equal(0,fixture.Acl.Enqueued);
    }
    [Fact]
    public async Task AclMvcConfirmCancelAndRecoveryAcknowledgementRejectAnotherIntentKey()
    {
        await using var fixture=await Fixture.Start();using var client=await fixture.Client("admin-subject",true);var token=await Csrf(client);var id=Guid.NewGuid();var wrong=Guid.NewGuid().ToString("D");
        foreach(var endpoint in new[] {$"{AclBase}/{id:D}/confirm",$"{AclBase}/{id:D}/cancel",$"/api/v1/files/container-acls/recovery/{id:D}/acknowledge"})
        {object body=endpoint.EndsWith("/confirm",StringComparison.Ordinal)?new{planHash="x",version=0,confirmed=true}:new{version=0,confirmed=true};using var request=Write(client,HttpMethod.Post,endpoint,token,JsonContent.Create(body),wrong);using var response=await client.SendAsync(request);Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);}
        Assert.Equal(0,fixture.Acl.Requires);Assert.Equal(0,fixture.Acl.Mutations);
    }
    private sealed class AclHttpFixture : IContainerAclRepository, IContainerAclGateway, IPrivilegedIdentityDirectory
    {
        private ContainerAclIntent? _row;
        public bool Allowed { get; set; } = true; public int Requires { get; private set; } public int Enqueued { get; private set; } public int Mutations { get; private set; }
        public Task<CurrentUserDto> ResolveForPrivilegedActionAsync(string subject,string? name,CancellationToken cancellationToken) => Task.FromResult(new CurrentUserDto(subject,name,[],Allowed,DateTimeOffset.UtcNow.AddMinutes(10)));
        public Task RequireContainerAclWorkflowAsync(CancellationToken cancellationToken) { Requires++; return Task.CompletedTask; }
        public Task<ContainerAclIntent?> OwnedAsync(TerminalActor actor,Guid id,CancellationToken cancellationToken) => Task.FromResult(_row is { } row && row.Id==id && row.ActorSubject==actor.Subject && row.LoginBinding==actor.LoginBinding ? row:null);
        public Task<ContainerAclIntent?> FindAsync(Guid id,CancellationToken cancellationToken) => Task.FromResult(_row?.Id==id ? _row:null);
        public Task<IReadOnlyList<ContainerAclIntent>> UnknownAsync(int take,CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ContainerAclIntent>>([]);
        public Task<ContainerAclIntent> EnqueueAsync(ContainerAclIntent row,CancellationToken cancellationToken) { Enqueued++; _row=row; return Task.FromResult(row); }
        public Task CancelAsync(TerminalActor actor,Guid id,CancellationToken cancellationToken) { _row!.State="Canceled"; _row.ResultCode="CONTAINER_ACL_CANCELED"; _row.Version++; return Task.CompletedTask; }
        public Task<ContainerAclPlanDto> PlanContainerAclAsync(TerminalActor actor,ContainerAclCommandDto command,CancellationToken cancellationToken) { Mutations++; throw new NotSupportedException(); }
        public Task<ContainerAclReceiptDto> ApplyContainerAclAsync(ContainerAclOperationDto operation,CancellationToken cancellationToken) { Mutations++; throw new NotSupportedException(); }
        public Task<ContainerAclReceiptDto?> InspectContainerAclAsync(ContainerAclOperationDto operation,CancellationToken cancellationToken) { Mutations++; throw new NotSupportedException(); }
        public Task<ContainerAclReceiptDto> DiscardContainerAclAsync(ContainerAclOperationDto operation,CancellationToken cancellationToken) { Mutations++; throw new NotSupportedException(); }
        public Task<ContainerAclIntent> ConfirmAsync(TerminalActor actor,ContainerAclConfirmation claims,long version,CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ContainerAclIntent?> ClaimAsync(DateTimeOffset now,CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> CancellationRequestedAsync(Guid id,CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> MarkPlanningAsync(ContainerAclIntent claimed,CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task FinishPlanningAsync(ContainerAclIntent claimed,ContainerAclPlanDto? plan,string code,CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ContainerFileUploadStartResult> StartedAsync(ContainerAclIntent claimed,DateTimeOffset now,CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> RenewAsync(ContainerAclIntent claimed,DateTimeOffset now,CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> FinishAsync(ContainerAclIntent claimed,string state,string code,ContainerAclReceiptDto? receipt,CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeferAsync(ContainerAclIntent claimed,CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AcknowledgeAsync(TerminalActor actor,Guid id,long version,ContainerAclReceiptDto receipt,CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
