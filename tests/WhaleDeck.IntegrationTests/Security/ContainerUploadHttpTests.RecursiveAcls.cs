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
    private static string RecursiveAclBase => $"/api/v1/containers/{Cid}/files/recursive-acls";
    private static readonly string[] RecursiveAclSuffixes = ["", "/by-key/aa0a8865-343e-49bd-b01a-72680131bf39", "/aa0a8865-343e-49bd-b01a-72680131bf39", "/aa0a8865-343e-49bd-b01a-72680131bf39/confirm", "/aa0a8865-343e-49bd-b01a-72680131bf39/cancel"];
    private static object RecursiveAclBody => new { instanceId = Instance, path = "work/tree", aclType = "access", fileEntries = new[]{new ContainerAclEntryDto("user_obj",null,6),new("group_obj",null,4),new("other",null,0)}, directoryEntries = new[]{new ContainerAclEntryDto("user_obj",null,7),new("group_obj",null,5),new("other",null,0)}, expectedVersion = new string('A',64) };
    [Theory][InlineData("anonymous", false, HttpStatusCode.Unauthorized)][InlineData("ordinary-subject", false, HttpStatusCode.Forbidden)]
    public async Task RecursiveAclEveryRouteRejectsUnprivilegedCookiesBeforeService(string subject, bool admin, HttpStatusCode expected)
    {
        await using var fixture = await Fixture.Start(); using var client = await fixture.Client(subject, admin); var token = await Csrf(client);
        foreach (var suffix in RecursiveAclSuffixes)
        {
            var write = suffix == "" || suffix.EndsWith("confirm", StringComparison.Ordinal) || suffix.EndsWith("cancel", StringComparison.Ordinal);
            using var request = write ? Write(client,HttpMethod.Post,RecursiveAclBase+suffix,token,JsonContent.Create(RecursiveAclBody)) : new HttpRequestMessage(HttpMethod.Get,RecursiveAclBase+suffix);
            using var response = await client.SendAsync(request); Assert.Equal(expected,response.StatusCode);
        }
        const string recovery = "/api/v1/files/container-recursive-acls/recovery";
        foreach (var suffix in new[] { "", "/aa0a8865-343e-49bd-b01a-72680131bf39", "/aa0a8865-343e-49bd-b01a-72680131bf39/acknowledge" })
        {
            using var request = suffix.EndsWith("acknowledge",StringComparison.Ordinal) ? Write(client,HttpMethod.Post,recovery+suffix,token,JsonContent.Create(new { version=0,confirmed=true })) : new HttpRequestMessage(HttpMethod.Get,recovery+suffix);
            using var response = await client.SendAsync(request); Assert.Equal(expected,response.StatusCode);
        }
        Assert.Equal(0,fixture.RecursiveAcl.Requires); Assert.Equal(0,fixture.RecursiveAcl.Enqueued);
    }
    [Theory][InlineData("csrf")][InlineData("origin")][InlineData("missing-origin")]
    public async Task RecursiveAclWritesRejectInvalidCsrfOrOrigin(string variant)
    {
        await using var fixture = await Fixture.Start(); using var client = await fixture.Client("admin-subject",true); var token = await Csrf(client);
        foreach (var suffix in new[] { "", "/aa0a8865-343e-49bd-b01a-72680131bf39/confirm", "/aa0a8865-343e-49bd-b01a-72680131bf39/cancel" })
        {
            using var request = Write(client,HttpMethod.Post,RecursiveAclBase+suffix,variant=="csrf" ? null:token,JsonContent.Create(RecursiveAclBody));
            if (variant != "csrf") request.Headers.Remove("Origin");
            if (variant == "origin") request.Headers.Add("Origin","http://unapproved.invalid");
            using var response = await client.SendAsync(request); Assert.Contains(response.StatusCode,new[] { HttpStatusCode.BadRequest,HttpStatusCode.Forbidden });
        }
        using var ack = Write(client,HttpMethod.Post,"/api/v1/files/container-recursive-acls/recovery/aa0a8865-343e-49bd-b01a-72680131bf39/acknowledge",null,JsonContent.Create(new {version=0,confirmed=true}));
        using var denied = await client.SendAsync(ack); Assert.Equal(HttpStatusCode.BadRequest,denied.StatusCode);
        Assert.Equal(0,fixture.RecursiveAcl.Requires); Assert.Equal(0,fixture.RecursiveAcl.Enqueued);
    }
    [Fact]
    public async Task RecursiveAclRealMvcQueuesOnlyMetadataAndResolvesSameOriginalLoginKey()
    {
        await using var fixture = await Fixture.Start(); using var client = await fixture.Client("admin-subject",true); var token = await Csrf(client); var key = Guid.NewGuid().ToString("D");
        async Task<ContainerRecursiveAclIntentDto> Queue()
        {
            using var request = Write(client,HttpMethod.Post,RecursiveAclBase,token,JsonContent.Create(RecursiveAclBody),key); using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Accepted,response.StatusCode); Assert.Contains("no-store",response.Headers.CacheControl!.ToString(),StringComparison.OrdinalIgnoreCase);
            var row = (await response.Content.ReadFromJsonAsync<ContainerRecursiveAclIntentDto>())!;
            Assert.Equal($"{RecursiveAclBase}/{row.Id:D}",response.Headers.Location!.OriginalString); return row;
        }
        var first = await Queue(); var replay = await Queue(); Assert.Equal(first.Id,replay.Id); Assert.Equal("Queued",first.State); Assert.Null(first.Receipt); Assert.Null(first.Confirmation);
        Assert.Equal(1,fixture.RecursiveAcl.Enqueued); Assert.Equal(1,fixture.RecursiveAcl.Requires);
        using var query = await client.GetAsync(RecursiveAclBase+"/by-key/"+key); Assert.Equal(HttpStatusCode.OK,query.StatusCode);
        Assert.Equal(first.Id,(await query.Content.ReadFromJsonAsync<ContainerRecursiveAclIntentDto>())!.Id);
        using var otherLogin = await fixture.Client("admin-subject",true);
        using var foreign = await otherLogin.GetAsync($"{RecursiveAclBase}/{first.Id:D}"); Assert.Equal(HttpStatusCode.NotFound,foreign.StatusCode);
        fixture.RecursiveAcl.Allowed = false;
        using var revoked = await client.GetAsync($"{RecursiveAclBase}/{first.Id:D}"); Assert.Equal(HttpStatusCode.Forbidden,revoked.StatusCode);
        fixture.RecursiveAcl.Allowed = true;
        using var cancel = Write(client,HttpMethod.Post,$"{RecursiveAclBase}/{first.Id:D}/cancel",token,null,first.Id.ToString("D")); using var canceled = await client.SendAsync(cancel);
        Assert.Equal(HttpStatusCode.Accepted,canceled.StatusCode); Assert.Equal("Canceled",(await canceled.Content.ReadFromJsonAsync<ContainerRecursiveAclIntentDto>())!.State);
        Assert.Equal(0,fixture.RecursiveAcl.Mutations);
    }
    [Fact]
    public async Task RecursiveAclMvcMalformedBodyNeverEchoesInputOrCallsDependencies()
    {
        await using var fixture = await Fixture.Start(); using var client = await fixture.Client("admin-subject",true); var token = await Csrf(client);
        using var body = new StringContent("{\"uid\":0,\"uid\":\"private-must-not-leak\"}",System.Text.Encoding.UTF8,"application/json");
        using var request = Write(client,HttpMethod.Post,RecursiveAclBase,token,body,Guid.NewGuid().ToString("D")); using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode); var content = await response.Content.ReadAsStringAsync(); Assert.Contains("CONTAINER_RECURSIVE_ACL_INPUT_INVALID",content,StringComparison.Ordinal); Assert.DoesNotContain("private-must-not-leak",content,StringComparison.Ordinal);
        Assert.Equal(0,fixture.RecursiveAcl.Requires); Assert.Equal(0,fixture.RecursiveAcl.Enqueued);
    }
    [Theory][InlineData("fileEntries")][InlineData("directoryEntries")]
    public async Task RecursiveAclMvcOmittedOwnerCannotBecomeRoot(string field)
    {
        await using var fixture=await Fixture.Start();using var client=await fixture.Client("admin-subject",true);var token=await Csrf(client);
        var body=System.Text.Json.JsonSerializer.SerializeToNode(RecursiveAclBody,System.Text.Json.JsonSerializerOptions.Web)!.AsObject();body.Remove(field);
        using var request=Write(client,HttpMethod.Post,RecursiveAclBase,token,JsonContent.Create(body),Guid.NewGuid().ToString("D"));using var response=await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);Assert.Equal(0,fixture.RecursiveAcl.Enqueued);Assert.Equal(0,fixture.RecursiveAcl.Requires);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task RecursiveAclMvcDefaultRetainsNullFilesAndExplicitClear(bool clear)
    {
        await using var fixture=await Fixture.Start();using var client=await fixture.Client("admin-subject",true);var token=await Csrf(client);
        var body=System.Text.Json.JsonSerializer.SerializeToNode(RecursiveAclBody,System.Text.Json.JsonSerializerOptions.Web)!.AsObject();body["aclType"]="default";body["fileEntries"]=null;
        if(clear)body["directoryEntries"]=new System.Text.Json.Nodes.JsonArray();
        using var request=Write(client,HttpMethod.Post,RecursiveAclBase,token,JsonContent.Create(body),Guid.NewGuid().ToString("D"));using var response=await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Accepted,response.StatusCode);var row=(await response.Content.ReadFromJsonAsync<ContainerRecursiveAclIntentDto>())!;
        Assert.Null(row.Command.FileEntries);Assert.Equal(clear?0:3,row.Command.DirectoryEntries.Count);Assert.Equal(0,fixture.RecursiveAcl.Mutations);
    }
    private sealed class RecursiveAclHttpFixture : IContainerRecursiveAclRepository, IContainerRecursiveAclGateway, IPrivilegedIdentityDirectory
    {
        private ContainerRecursiveAclIntent? _row;
        public bool Allowed { get; set; } = true; public int Requires { get; private set; } public int Enqueued { get; private set; } public int Mutations { get; private set; }
        public Task<CurrentUserDto> ResolveForPrivilegedActionAsync(string subject,string? name,CancellationToken cancellationToken) => Task.FromResult(new CurrentUserDto(subject,name,[],Allowed,DateTimeOffset.UtcNow.AddMinutes(10)));
        public Task RequireContainerRecursiveAclWorkflowAsync(CancellationToken cancellationToken) { Requires++; return Task.CompletedTask; }
        public Task<ContainerRecursiveAclIntent?> OwnedAsync(TerminalActor actor,Guid id,CancellationToken cancellationToken) => Task.FromResult(_row is { } row && row.Id==id && row.ActorSubject==actor.Subject && row.LoginBinding==actor.LoginBinding ? row:null);
        public Task<ContainerRecursiveAclIntent?> FindAsync(Guid id,CancellationToken cancellationToken) => Task.FromResult(_row?.Id==id ? _row:null);
        public Task<IReadOnlyList<ContainerRecursiveAclIntent>> UnresolvedAsync(int take,CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ContainerRecursiveAclIntent>>([]);
        public Task<ContainerRecursiveAclIntent> EnqueueAsync(ContainerRecursiveAclIntent row,CancellationToken cancellationToken) { Enqueued++; _row=row; return Task.FromResult(row); }
        public Task CancelAsync(TerminalActor actor,Guid id,CancellationToken cancellationToken) { _row!.State="Canceled"; _row.ResultCode="CONTAINER_RECURSIVE_ACL_CANCELED"; _row.Version++; return Task.CompletedTask; }
        public Task<ContainerRecursiveAclPlanDto> PlanContainerRecursiveAclAsync(TerminalActor actor,ContainerRecursiveAclCommandDto command,CancellationToken cancellationToken) { Mutations++; throw new NotSupportedException(); }
        public Task<ContainerRecursiveAclReceiptDto> ApplyContainerRecursiveAclAsync(ContainerRecursiveAclOperationDto operation,CancellationToken cancellationToken) { Mutations++; throw new NotSupportedException(); }
        public Task<ContainerRecursiveAclReceiptDto?> InspectContainerRecursiveAclAsync(ContainerRecursiveAclOperationDto operation,CancellationToken cancellationToken) { Mutations++; throw new NotSupportedException(); }
        public Task<ContainerRecursiveAclReceiptDto> DiscardContainerRecursiveAclAsync(ContainerRecursiveAclOperationDto operation,CancellationToken cancellationToken) { Mutations++; throw new NotSupportedException(); }
        public Task<ContainerRecursiveAclIntent> ConfirmAsync(TerminalActor actor,ContainerRecursiveAclConfirmation claims,long version,CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ContainerRecursiveAclIntent?> ClaimAsync(DateTimeOffset now,CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> CancellationRequestedAsync(Guid id,CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> MarkPlanningAsync(ContainerRecursiveAclIntent claimed,CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task FinishPlanningAsync(ContainerRecursiveAclIntent claimed,ContainerRecursiveAclPlanDto? plan,string code,CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ContainerFileUploadStartResult> StartedAsync(ContainerRecursiveAclIntent claimed,DateTimeOffset now,CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> RenewAsync(ContainerRecursiveAclIntent claimed,DateTimeOffset now,CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> FinishAsync(ContainerRecursiveAclIntent claimed,string state,string code,ContainerRecursiveAclReceiptDto? receipt,CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeferAsync(ContainerRecursiveAclIntent claimed,CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AcknowledgeAsync(TerminalActor actor,Guid id,long version,ContainerRecursiveAclReceiptDto receipt,CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
