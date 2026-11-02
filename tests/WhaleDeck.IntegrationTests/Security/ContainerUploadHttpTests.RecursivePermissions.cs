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
    private static string RecursiveBase => $"/api/v1/containers/{Cid}/files/recursive-permissions";
    private static readonly string[] RecursiveSuffixes = ["", "/by-key/aa0a8865-343e-49bd-b01a-72680131bf39", "/aa0a8865-343e-49bd-b01a-72680131bf39", "/aa0a8865-343e-49bd-b01a-72680131bf39/confirm", "/aa0a8865-343e-49bd-b01a-72680131bf39/cancel"];
    private static object RecursiveBody => new { instanceId = Instance, path = "work/tree", fileMode = "0640", directoryMode = "0750", expectedVersion = new string('A',64) };
    [Theory][InlineData("anonymous", false, HttpStatusCode.Unauthorized)][InlineData("ordinary-subject", false, HttpStatusCode.Forbidden)]
    public async Task RecursiveEveryRouteRejectsUnprivilegedCookiesBeforeService(string subject, bool admin, HttpStatusCode expected)
    {
        await using var fixture = await Fixture.Start(); using var client = await fixture.Client(subject, admin); var token = await Csrf(client);
        foreach (var suffix in RecursiveSuffixes)
        {
            var write = suffix == "" || suffix.EndsWith("confirm", StringComparison.Ordinal) || suffix.EndsWith("cancel", StringComparison.Ordinal);
            using var request = write ? Write(client,HttpMethod.Post,RecursiveBase+suffix,token,JsonContent.Create(RecursiveBody)) : new HttpRequestMessage(HttpMethod.Get,RecursiveBase+suffix);
            using var response = await client.SendAsync(request); Assert.Equal(expected,response.StatusCode);
        }
        const string recovery = "/api/v1/files/container-recursive-permissions/recovery";
        foreach (var suffix in new[] { "", "/aa0a8865-343e-49bd-b01a-72680131bf39", "/aa0a8865-343e-49bd-b01a-72680131bf39/acknowledge" })
        {
            using var request = suffix.EndsWith("acknowledge",StringComparison.Ordinal) ? Write(client,HttpMethod.Post,recovery+suffix,token,JsonContent.Create(new { version=0,confirmed=true })) : new HttpRequestMessage(HttpMethod.Get,recovery+suffix);
            using var response = await client.SendAsync(request); Assert.Equal(expected,response.StatusCode);
        }
        Assert.Equal(0,fixture.Recursive.Requires); Assert.Equal(0,fixture.Recursive.Enqueued);
    }
    [Theory][InlineData("csrf")][InlineData("origin")][InlineData("missing-origin")]
    public async Task RecursiveWritesRejectInvalidCsrfOrOrigin(string variant)
    {
        await using var fixture = await Fixture.Start(); using var client = await fixture.Client("admin-subject",true); var token = await Csrf(client);
        foreach (var suffix in new[] { "", "/aa0a8865-343e-49bd-b01a-72680131bf39/confirm", "/aa0a8865-343e-49bd-b01a-72680131bf39/cancel" })
        {
            using var request = Write(client,HttpMethod.Post,RecursiveBase+suffix,variant=="csrf" ? null:token,JsonContent.Create(RecursiveBody));
            if (variant != "csrf") request.Headers.Remove("Origin");
            if (variant == "origin") request.Headers.Add("Origin","http://unapproved.invalid");
            using var response = await client.SendAsync(request); Assert.Contains(response.StatusCode,new[] { HttpStatusCode.BadRequest,HttpStatusCode.Forbidden });
        }
        using var ack = Write(client,HttpMethod.Post,"/api/v1/files/container-recursive-permissions/recovery/aa0a8865-343e-49bd-b01a-72680131bf39/acknowledge",null,JsonContent.Create(new {version=0,confirmed=true}));
        using var denied = await client.SendAsync(ack); Assert.Equal(HttpStatusCode.BadRequest,denied.StatusCode);
        Assert.Equal(0,fixture.Recursive.Requires); Assert.Equal(0,fixture.Recursive.Enqueued);
    }
    [Fact]
    public async Task RecursiveRealMvcQueuesOnlyMetadataAndResolvesSameOriginalLoginKey()
    {
        await using var fixture = await Fixture.Start(); using var client = await fixture.Client("admin-subject",true); var token = await Csrf(client); var key = Guid.NewGuid().ToString("D");
        async Task<ContainerRecursivePermissionIntentDto> Queue()
        {
            using var request = Write(client,HttpMethod.Post,RecursiveBase,token,JsonContent.Create(RecursiveBody),key); using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Accepted,response.StatusCode); Assert.Contains("no-store",response.Headers.CacheControl!.ToString(),StringComparison.OrdinalIgnoreCase);
            var row = (await response.Content.ReadFromJsonAsync<ContainerRecursivePermissionIntentDto>())!;
            Assert.Equal($"{RecursiveBase}/{row.Id:D}",response.Headers.Location!.OriginalString); return row;
        }
        var first = await Queue(); var replay = await Queue(); Assert.Equal(first.Id,replay.Id); Assert.Equal("Queued",first.State); Assert.Null(first.Receipt); Assert.Null(first.Confirmation);
        Assert.Equal(1,fixture.Recursive.Enqueued); Assert.Equal(1,fixture.Recursive.Requires);
        using var query = await client.GetAsync(RecursiveBase+"/by-key/"+key); Assert.Equal(HttpStatusCode.OK,query.StatusCode);
        Assert.Equal(first.Id,(await query.Content.ReadFromJsonAsync<ContainerRecursivePermissionIntentDto>())!.Id);
        using var otherLogin = await fixture.Client("admin-subject",true);
        using var foreign = await otherLogin.GetAsync($"{RecursiveBase}/{first.Id:D}"); Assert.Equal(HttpStatusCode.NotFound,foreign.StatusCode);
        fixture.Recursive.Allowed = false;
        using var revoked = await client.GetAsync($"{RecursiveBase}/{first.Id:D}"); Assert.Equal(HttpStatusCode.Forbidden,revoked.StatusCode);
        fixture.Recursive.Allowed = true;
        using var cancel = Write(client,HttpMethod.Post,$"{RecursiveBase}/{first.Id:D}/cancel",token,null,first.Id.ToString("D")); using var canceled = await client.SendAsync(cancel);
        Assert.Equal(HttpStatusCode.Accepted,canceled.StatusCode); Assert.Equal("Canceled",(await canceled.Content.ReadFromJsonAsync<ContainerRecursivePermissionIntentDto>())!.State);
        Assert.Equal(0,fixture.Recursive.Mutations);
    }
    [Fact]
    public async Task RecursiveMvcMalformedBodyNeverEchoesInputOrCallsDependencies()
    {
        await using var fixture = await Fixture.Start(); using var client = await fixture.Client("admin-subject",true); var token = await Csrf(client);
        using var body = new StringContent("{\"fileMode\":\"0640\",\"fileMode\":\"private-must-not-leak\"}",System.Text.Encoding.UTF8,"application/json");
        using var request = Write(client,HttpMethod.Post,RecursiveBase,token,body,Guid.NewGuid().ToString("D")); using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode); var content = await response.Content.ReadAsStringAsync(); Assert.Contains("CONTAINER_RECURSIVE_PERMISSION_INPUT_INVALID",content,StringComparison.Ordinal); Assert.DoesNotContain("private-must-not-leak",content,StringComparison.Ordinal);
        Assert.Equal(0,fixture.Recursive.Requires); Assert.Equal(0,fixture.Recursive.Enqueued);
    }
    private sealed class RecursiveHttpFixture : IContainerRecursivePermissionRepository, IContainerRecursivePermissionGateway, IPrivilegedIdentityDirectory
    {
        private ContainerRecursivePermissionIntent? _row;
        public bool Allowed { get; set; } = true; public int Requires { get; private set; } public int Enqueued { get; private set; } public int Mutations { get; private set; }
        public Task<CurrentUserDto> ResolveForPrivilegedActionAsync(string subject,string? name,CancellationToken cancellationToken) => Task.FromResult(new CurrentUserDto(subject,name,[],Allowed,DateTimeOffset.UtcNow.AddMinutes(10)));
        public Task RequireContainerRecursivePermissionWorkflowAsync(CancellationToken cancellationToken) { Requires++; return Task.CompletedTask; }
        public Task<ContainerRecursivePermissionIntent?> OwnedAsync(TerminalActor actor,Guid id,CancellationToken cancellationToken) => Task.FromResult(_row is { } row && row.Id==id && row.ActorSubject==actor.Subject && row.LoginBinding==actor.LoginBinding ? row:null);
        public Task<ContainerRecursivePermissionIntent?> FindAsync(Guid id,CancellationToken cancellationToken) => Task.FromResult(_row?.Id==id ? _row:null);
        public Task<IReadOnlyList<ContainerRecursivePermissionIntent>> UnresolvedAsync(int take,CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ContainerRecursivePermissionIntent>>([]);
        public Task<ContainerRecursivePermissionIntent> EnqueueAsync(ContainerRecursivePermissionIntent row,CancellationToken cancellationToken) { Enqueued++; _row=row; return Task.FromResult(row); }
        public Task CancelAsync(TerminalActor actor,Guid id,CancellationToken cancellationToken) { _row!.State="Canceled"; _row.ResultCode="CONTAINER_RECURSIVE_PERMISSION_CANCELED"; _row.Version++; return Task.CompletedTask; }
        public Task<ContainerRecursivePermissionPlanDto> PlanContainerRecursivePermissionAsync(TerminalActor actor,ContainerRecursivePermissionCommandDto command,CancellationToken cancellationToken) { Mutations++; throw new NotSupportedException(); }
        public Task<ContainerRecursivePermissionReceiptDto> ApplyContainerRecursivePermissionAsync(ContainerRecursivePermissionOperationDto operation,CancellationToken cancellationToken) { Mutations++; throw new NotSupportedException(); }
        public Task<ContainerRecursivePermissionReceiptDto?> InspectContainerRecursivePermissionAsync(ContainerRecursivePermissionOperationDto operation,CancellationToken cancellationToken) { Mutations++; throw new NotSupportedException(); }
        public Task<ContainerRecursivePermissionReceiptDto> DiscardContainerRecursivePermissionAsync(ContainerRecursivePermissionOperationDto operation,CancellationToken cancellationToken) { Mutations++; throw new NotSupportedException(); }
        public Task<ContainerRecursivePermissionIntent> ConfirmAsync(TerminalActor actor,ContainerRecursivePermissionConfirmation claims,long version,CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ContainerRecursivePermissionIntent?> ClaimAsync(DateTimeOffset now,CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> CancellationRequestedAsync(Guid id,CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> MarkPlanningAsync(ContainerRecursivePermissionIntent claimed,CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task FinishPlanningAsync(ContainerRecursivePermissionIntent claimed,ContainerRecursivePermissionPlanDto? plan,string code,CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ContainerFileUploadStartResult> StartedAsync(ContainerRecursivePermissionIntent claimed,DateTimeOffset now,CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> RenewAsync(ContainerRecursivePermissionIntent claimed,DateTimeOffset now,CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> FinishAsync(ContainerRecursivePermissionIntent claimed,string state,string code,ContainerRecursivePermissionReceiptDto? receipt,CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeferAsync(ContainerRecursivePermissionIntent claimed,CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AcknowledgeAsync(TerminalActor actor,Guid id,long version,ContainerRecursivePermissionReceiptDto receipt,CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
