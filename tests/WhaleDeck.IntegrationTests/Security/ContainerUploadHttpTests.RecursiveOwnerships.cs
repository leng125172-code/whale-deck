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
    private static string RecursiveOwnerBase => $"/api/v1/containers/{Cid}/files/recursive-ownerships";
    private static readonly string[] RecursiveOwnerSuffixes = ["", "/by-key/aa0a8865-343e-49bd-b01a-72680131bf39", "/aa0a8865-343e-49bd-b01a-72680131bf39", "/aa0a8865-343e-49bd-b01a-72680131bf39/confirm", "/aa0a8865-343e-49bd-b01a-72680131bf39/cancel"];
    private static object RecursiveOwnerBody => new { instanceId = Instance, path = "work/tree", uid = 2200, gid = 3200, expectedVersion = new string('A',64) };
    [Theory][InlineData("anonymous", false, HttpStatusCode.Unauthorized)][InlineData("ordinary-subject", false, HttpStatusCode.Forbidden)]
    public async Task RecursiveOwnerEveryRouteRejectsUnprivilegedCookiesBeforeService(string subject, bool admin, HttpStatusCode expected)
    {
        await using var fixture = await Fixture.Start(); using var client = await fixture.Client(subject, admin); var token = await Csrf(client);
        foreach (var suffix in RecursiveOwnerSuffixes)
        {
            var write = suffix == "" || suffix.EndsWith("confirm", StringComparison.Ordinal) || suffix.EndsWith("cancel", StringComparison.Ordinal);
            using var request = write ? Write(client,HttpMethod.Post,RecursiveOwnerBase+suffix,token,JsonContent.Create(RecursiveOwnerBody)) : new HttpRequestMessage(HttpMethod.Get,RecursiveOwnerBase+suffix);
            using var response = await client.SendAsync(request); Assert.Equal(expected,response.StatusCode);
        }
        const string recovery = "/api/v1/files/container-recursive-ownerships/recovery";
        foreach (var suffix in new[] { "", "/aa0a8865-343e-49bd-b01a-72680131bf39", "/aa0a8865-343e-49bd-b01a-72680131bf39/acknowledge" })
        {
            using var request = suffix.EndsWith("acknowledge",StringComparison.Ordinal) ? Write(client,HttpMethod.Post,recovery+suffix,token,JsonContent.Create(new { version=0,confirmed=true })) : new HttpRequestMessage(HttpMethod.Get,recovery+suffix);
            using var response = await client.SendAsync(request); Assert.Equal(expected,response.StatusCode);
        }
        Assert.Equal(0,fixture.RecursiveOwner.Requires); Assert.Equal(0,fixture.RecursiveOwner.Enqueued);
    }
    [Theory][InlineData("csrf")][InlineData("origin")][InlineData("missing-origin")]
    public async Task RecursiveOwnerWritesRejectInvalidCsrfOrOrigin(string variant)
    {
        await using var fixture = await Fixture.Start(); using var client = await fixture.Client("admin-subject",true); var token = await Csrf(client);
        foreach (var suffix in new[] { "", "/aa0a8865-343e-49bd-b01a-72680131bf39/confirm", "/aa0a8865-343e-49bd-b01a-72680131bf39/cancel" })
        {
            using var request = Write(client,HttpMethod.Post,RecursiveOwnerBase+suffix,variant=="csrf" ? null:token,JsonContent.Create(RecursiveOwnerBody));
            if (variant != "csrf") request.Headers.Remove("Origin");
            if (variant == "origin") request.Headers.Add("Origin","http://unapproved.invalid");
            using var response = await client.SendAsync(request); Assert.Contains(response.StatusCode,new[] { HttpStatusCode.BadRequest,HttpStatusCode.Forbidden });
        }
        using var ack = Write(client,HttpMethod.Post,"/api/v1/files/container-recursive-ownerships/recovery/aa0a8865-343e-49bd-b01a-72680131bf39/acknowledge",null,JsonContent.Create(new {version=0,confirmed=true}));
        using var denied = await client.SendAsync(ack); Assert.Equal(HttpStatusCode.BadRequest,denied.StatusCode);
        Assert.Equal(0,fixture.RecursiveOwner.Requires); Assert.Equal(0,fixture.RecursiveOwner.Enqueued);
    }
    [Fact]
    public async Task RecursiveOwnerRealMvcQueuesOnlyMetadataAndResolvesSameOriginalLoginKey()
    {
        await using var fixture = await Fixture.Start(); using var client = await fixture.Client("admin-subject",true); var token = await Csrf(client); var key = Guid.NewGuid().ToString("D");
        async Task<ContainerRecursiveOwnershipIntentDto> Queue()
        {
            using var request = Write(client,HttpMethod.Post,RecursiveOwnerBase,token,JsonContent.Create(RecursiveOwnerBody),key); using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Accepted,response.StatusCode); Assert.Contains("no-store",response.Headers.CacheControl!.ToString(),StringComparison.OrdinalIgnoreCase);
            var row = (await response.Content.ReadFromJsonAsync<ContainerRecursiveOwnershipIntentDto>())!;
            Assert.Equal($"{RecursiveOwnerBase}/{row.Id:D}",response.Headers.Location!.OriginalString); return row;
        }
        var first = await Queue(); var replay = await Queue(); Assert.Equal(first.Id,replay.Id); Assert.Equal("Queued",first.State); Assert.Null(first.Receipt); Assert.Null(first.Confirmation);
        Assert.Equal(1,fixture.RecursiveOwner.Enqueued); Assert.Equal(1,fixture.RecursiveOwner.Requires);
        using var query = await client.GetAsync(RecursiveOwnerBase+"/by-key/"+key); Assert.Equal(HttpStatusCode.OK,query.StatusCode);
        Assert.Equal(first.Id,(await query.Content.ReadFromJsonAsync<ContainerRecursiveOwnershipIntentDto>())!.Id);
        using var otherLogin = await fixture.Client("admin-subject",true);
        using var foreign = await otherLogin.GetAsync($"{RecursiveOwnerBase}/{first.Id:D}"); Assert.Equal(HttpStatusCode.NotFound,foreign.StatusCode);
        fixture.RecursiveOwner.Allowed = false;
        using var revoked = await client.GetAsync($"{RecursiveOwnerBase}/{first.Id:D}"); Assert.Equal(HttpStatusCode.Forbidden,revoked.StatusCode);
        fixture.RecursiveOwner.Allowed = true;
        using var cancel = Write(client,HttpMethod.Post,$"{RecursiveOwnerBase}/{first.Id:D}/cancel",token,null,first.Id.ToString("D")); using var canceled = await client.SendAsync(cancel);
        Assert.Equal(HttpStatusCode.Accepted,canceled.StatusCode); Assert.Equal("Canceled",(await canceled.Content.ReadFromJsonAsync<ContainerRecursiveOwnershipIntentDto>())!.State);
        Assert.Equal(0,fixture.RecursiveOwner.Mutations);
    }
    [Fact]
    public async Task RecursiveOwnerMvcMalformedBodyNeverEchoesInputOrCallsDependencies()
    {
        await using var fixture = await Fixture.Start(); using var client = await fixture.Client("admin-subject",true); var token = await Csrf(client);
        using var body = new StringContent("{\"uid\":0,\"uid\":\"private-must-not-leak\"}",System.Text.Encoding.UTF8,"application/json");
        using var request = Write(client,HttpMethod.Post,RecursiveOwnerBase,token,body,Guid.NewGuid().ToString("D")); using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode); var content = await response.Content.ReadAsStringAsync(); Assert.Contains("CONTAINER_RECURSIVE_OWNERSHIP_INPUT_INVALID",content,StringComparison.Ordinal); Assert.DoesNotContain("private-must-not-leak",content,StringComparison.Ordinal);
        Assert.Equal(0,fixture.RecursiveOwner.Requires); Assert.Equal(0,fixture.RecursiveOwner.Enqueued);
    }
    [Theory][InlineData("uid")][InlineData("gid")]
    public async Task RecursiveOwnerMvcOmittedOwnerCannotBecomeRoot(string field)
    {
        await using var fixture=await Fixture.Start();using var client=await fixture.Client("admin-subject",true);var token=await Csrf(client);
        var body=System.Text.Json.JsonSerializer.SerializeToNode(RecursiveOwnerBody)!.AsObject();body.Remove(field);
        using var request=Write(client,HttpMethod.Post,RecursiveOwnerBase,token,JsonContent.Create(body),Guid.NewGuid().ToString("D"));using var response=await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);Assert.Equal(0,fixture.RecursiveOwner.Enqueued);Assert.Equal(0,fixture.RecursiveOwner.Requires);
    }
    [Fact]
    public async Task RecursiveOwnerMvcExplicitRootIsRetainedInOriginalCommand()
    {
        await using var fixture=await Fixture.Start();using var client=await fixture.Client("admin-subject",true);var token=await Csrf(client);
        var body=System.Text.Json.JsonSerializer.SerializeToNode(RecursiveOwnerBody)!.AsObject();body["uid"]=0;body["gid"]=0;
        using var request=Write(client,HttpMethod.Post,RecursiveOwnerBase,token,JsonContent.Create(body),Guid.NewGuid().ToString("D"));using var response=await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Accepted,response.StatusCode);var row=(await response.Content.ReadFromJsonAsync<ContainerRecursiveOwnershipIntentDto>())!;
        Assert.Equal((0U,0U),(row.Command.Uid,row.Command.Gid));Assert.Equal(0,fixture.RecursiveOwner.Mutations);
    }
    private sealed class RecursiveOwnerHttpFixture : IContainerRecursiveOwnershipRepository, IContainerRecursiveOwnershipGateway, IPrivilegedIdentityDirectory
    {
        private ContainerRecursiveOwnershipIntent? _row;
        public bool Allowed { get; set; } = true; public int Requires { get; private set; } public int Enqueued { get; private set; } public int Mutations { get; private set; }
        public Task<CurrentUserDto> ResolveForPrivilegedActionAsync(string subject,string? name,CancellationToken cancellationToken) => Task.FromResult(new CurrentUserDto(subject,name,[],Allowed,DateTimeOffset.UtcNow.AddMinutes(10)));
        public Task RequireContainerRecursiveOwnershipWorkflowAsync(CancellationToken cancellationToken) { Requires++; return Task.CompletedTask; }
        public Task<ContainerRecursiveOwnershipIntent?> OwnedAsync(TerminalActor actor,Guid id,CancellationToken cancellationToken) => Task.FromResult(_row is { } row && row.Id==id && row.ActorSubject==actor.Subject && row.LoginBinding==actor.LoginBinding ? row:null);
        public Task<ContainerRecursiveOwnershipIntent?> FindAsync(Guid id,CancellationToken cancellationToken) => Task.FromResult(_row?.Id==id ? _row:null);
        public Task<IReadOnlyList<ContainerRecursiveOwnershipIntent>> UnresolvedAsync(int take,CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ContainerRecursiveOwnershipIntent>>([]);
        public Task<ContainerRecursiveOwnershipIntent> EnqueueAsync(ContainerRecursiveOwnershipIntent row,CancellationToken cancellationToken) { Enqueued++; _row=row; return Task.FromResult(row); }
        public Task CancelAsync(TerminalActor actor,Guid id,CancellationToken cancellationToken) { _row!.State="Canceled"; _row.ResultCode="CONTAINER_RECURSIVE_OWNERSHIP_CANCELED"; _row.Version++; return Task.CompletedTask; }
        public Task<ContainerRecursiveOwnershipPlanDto> PlanContainerRecursiveOwnershipAsync(TerminalActor actor,ContainerRecursiveOwnershipCommandDto command,CancellationToken cancellationToken) { Mutations++; throw new NotSupportedException(); }
        public Task<ContainerRecursiveOwnershipReceiptDto> ApplyContainerRecursiveOwnershipAsync(ContainerRecursiveOwnershipOperationDto operation,CancellationToken cancellationToken) { Mutations++; throw new NotSupportedException(); }
        public Task<ContainerRecursiveOwnershipReceiptDto?> InspectContainerRecursiveOwnershipAsync(ContainerRecursiveOwnershipOperationDto operation,CancellationToken cancellationToken) { Mutations++; throw new NotSupportedException(); }
        public Task<ContainerRecursiveOwnershipReceiptDto> DiscardContainerRecursiveOwnershipAsync(ContainerRecursiveOwnershipOperationDto operation,CancellationToken cancellationToken) { Mutations++; throw new NotSupportedException(); }
        public Task<ContainerRecursiveOwnershipIntent> ConfirmAsync(TerminalActor actor,ContainerRecursiveOwnershipConfirmation claims,long version,CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ContainerRecursiveOwnershipIntent?> ClaimAsync(DateTimeOffset now,CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> CancellationRequestedAsync(Guid id,CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> MarkPlanningAsync(ContainerRecursiveOwnershipIntent claimed,CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task FinishPlanningAsync(ContainerRecursiveOwnershipIntent claimed,ContainerRecursiveOwnershipPlanDto? plan,string code,CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ContainerFileUploadStartResult> StartedAsync(ContainerRecursiveOwnershipIntent claimed,DateTimeOffset now,CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> RenewAsync(ContainerRecursiveOwnershipIntent claimed,DateTimeOffset now,CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> FinishAsync(ContainerRecursiveOwnershipIntent claimed,string state,string code,ContainerRecursiveOwnershipReceiptDto? receipt,CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeferAsync(ContainerRecursiveOwnershipIntent claimed,CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AcknowledgeAsync(TerminalActor actor,Guid id,long version,ContainerRecursiveOwnershipReceiptDto receipt,CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
