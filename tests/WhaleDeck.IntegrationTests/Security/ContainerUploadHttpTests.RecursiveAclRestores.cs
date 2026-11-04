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
    private static string RecursiveAclRestoreBase => $"/api/v1/containers/{Cid}/files/recursive-acl-restores";
    private static readonly string[] RecursiveAclRestoreSuffixes = ["", "/by-key/aa0a8865-343e-49bd-b01a-72680131bf39", "/aa0a8865-343e-49bd-b01a-72680131bf39", "/aa0a8865-343e-49bd-b01a-72680131bf39/confirm", "/aa0a8865-343e-49bd-b01a-72680131bf39/cancel"];
    private static object RecursiveAclRestoreBody => new { instanceId=Instance,path="work/tree",captureId=new string('a',32),expectedHistoryDigest=new string('B',64),expectedVersion=new string('A',64) };
    [Theory][InlineData("anonymous", false, HttpStatusCode.Unauthorized)][InlineData("ordinary-subject", false, HttpStatusCode.Forbidden)]
    public async Task RecursiveAclRestoreEveryRouteRejectsUnprivilegedCookiesBeforeService(string subject, bool admin, HttpStatusCode expected)
    {
        await using var fixture = await Fixture.Start(); using var client = await fixture.Client(subject, admin); var token = await Csrf(client);
        foreach (var suffix in RecursiveAclRestoreSuffixes)
        {
            var write = suffix == "" || suffix.EndsWith("confirm", StringComparison.Ordinal) || suffix.EndsWith("cancel", StringComparison.Ordinal);
            using var request = write ? Write(client,HttpMethod.Post,RecursiveAclRestoreBase+suffix,token,JsonContent.Create(RecursiveAclRestoreBody)) : new HttpRequestMessage(HttpMethod.Get,RecursiveAclRestoreBase+suffix);
            using var response = await client.SendAsync(request); Assert.Equal(expected,response.StatusCode);
        }
        const string recovery = "/api/v1/files/container-recursive-acl-restores/recovery";
        foreach (var suffix in new[] { "", "/aa0a8865-343e-49bd-b01a-72680131bf39", "/aa0a8865-343e-49bd-b01a-72680131bf39/acknowledge" })
        {
            using var request = suffix.EndsWith("acknowledge",StringComparison.Ordinal) ? Write(client,HttpMethod.Post,recovery+suffix,token,JsonContent.Create(new { version=0,confirmed=true })) : new HttpRequestMessage(HttpMethod.Get,recovery+suffix);
            using var response = await client.SendAsync(request); Assert.Equal(expected,response.StatusCode);
        }
        Assert.Equal(0,fixture.RecursiveAclRestore.Requires); Assert.Equal(0,fixture.RecursiveAclRestore.Enqueued);
    }
    [Theory][InlineData("csrf")][InlineData("origin")][InlineData("missing-origin")]
    public async Task RecursiveAclRestoreWritesRejectInvalidCsrfOrOrigin(string variant)
    {
        await using var fixture = await Fixture.Start(); using var client = await fixture.Client("admin-subject",true); var token = await Csrf(client);
        foreach (var suffix in new[] { "", "/aa0a8865-343e-49bd-b01a-72680131bf39/confirm", "/aa0a8865-343e-49bd-b01a-72680131bf39/cancel" })
        {
            using var request = Write(client,HttpMethod.Post,RecursiveAclRestoreBase+suffix,variant=="csrf" ? null:token,JsonContent.Create(RecursiveAclRestoreBody));
            if (variant != "csrf") request.Headers.Remove("Origin");
            if (variant == "origin") request.Headers.Add("Origin","http://unapproved.invalid");
            using var response = await client.SendAsync(request); Assert.Contains(response.StatusCode,new[] { HttpStatusCode.BadRequest,HttpStatusCode.Forbidden });
        }
        using var ack = Write(client,HttpMethod.Post,"/api/v1/files/container-recursive-acl-restores/recovery/aa0a8865-343e-49bd-b01a-72680131bf39/acknowledge",null,JsonContent.Create(new {version=0,confirmed=true}));
        using var denied = await client.SendAsync(ack); Assert.Equal(HttpStatusCode.BadRequest,denied.StatusCode);
        Assert.Equal(0,fixture.RecursiveAclRestore.Requires); Assert.Equal(0,fixture.RecursiveAclRestore.Enqueued);
    }
    [Fact]
    public async Task RecursiveAclRestoreRealMvcQueuesOnlyMetadataAndResolvesSameOriginalLoginKey()
    {
        await using var fixture = await Fixture.Start(); using var client = await fixture.Client("admin-subject",true); var token = await Csrf(client); var key = Guid.NewGuid().ToString("D");
        async Task<ContainerRecursiveAclRestoreIntentDto> Queue()
        {
            using var request = Write(client,HttpMethod.Post,RecursiveAclRestoreBase,token,JsonContent.Create(RecursiveAclRestoreBody),key); using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Accepted,response.StatusCode); Assert.Contains("no-store",response.Headers.CacheControl!.ToString(),StringComparison.OrdinalIgnoreCase);
            var row = (await response.Content.ReadFromJsonAsync<ContainerRecursiveAclRestoreIntentDto>())!;
            Assert.Equal($"{RecursiveAclRestoreBase}/{row.Id:D}",response.Headers.Location!.OriginalString); return row;
        }
        var first = await Queue(); var replay = await Queue(); Assert.Equal(first.Id,replay.Id); Assert.Equal("Queued",first.State); Assert.Null(first.Receipt); Assert.Null(first.Confirmation);
        Assert.Equal(1,fixture.RecursiveAclRestore.Enqueued); Assert.Equal(1,fixture.RecursiveAclRestore.Requires);
        using var query = await client.GetAsync(RecursiveAclRestoreBase+"/by-key/"+key); Assert.Equal(HttpStatusCode.OK,query.StatusCode);
        Assert.Equal(first.Id,(await query.Content.ReadFromJsonAsync<ContainerRecursiveAclRestoreIntentDto>())!.Id);
        using var otherLogin = await fixture.Client("admin-subject",true);
        using var foreign = await otherLogin.GetAsync($"{RecursiveAclRestoreBase}/{first.Id:D}"); Assert.Equal(HttpStatusCode.NotFound,foreign.StatusCode);
        fixture.RecursiveAclRestore.Allowed = false;
        using var revoked = await client.GetAsync($"{RecursiveAclRestoreBase}/{first.Id:D}"); Assert.Equal(HttpStatusCode.Forbidden,revoked.StatusCode);
        fixture.RecursiveAclRestore.Allowed = true;
        using var cancel = Write(client,HttpMethod.Post,$"{RecursiveAclRestoreBase}/{first.Id:D}/cancel",token,null,first.Id.ToString("D")); using var canceled = await client.SendAsync(cancel);
        Assert.Equal(HttpStatusCode.Accepted,canceled.StatusCode); Assert.Equal("Canceled",(await canceled.Content.ReadFromJsonAsync<ContainerRecursiveAclRestoreIntentDto>())!.State);
        Assert.Equal(0,fixture.RecursiveAclRestore.Mutations);
    }
    [Fact]
    public async Task RecursiveAclRestoreMvcMalformedBodyNeverEchoesInputOrCallsDependencies()
    {
        await using var fixture = await Fixture.Start(); using var client = await fixture.Client("admin-subject",true); var token = await Csrf(client);
        using var body = new StringContent("{\"uid\":0,\"uid\":\"private-must-not-leak\"}",System.Text.Encoding.UTF8,"application/json");
        using var request = Write(client,HttpMethod.Post,RecursiveAclRestoreBase,token,body,Guid.NewGuid().ToString("D")); using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode); var content = await response.Content.ReadAsStringAsync(); Assert.Contains("CONTAINER_RECURSIVE_ACL_RESTORE_INPUT_INVALID",content,StringComparison.Ordinal); Assert.DoesNotContain("private-must-not-leak",content,StringComparison.Ordinal);
        Assert.Equal(0,fixture.RecursiveAclRestore.Requires); Assert.Equal(0,fixture.RecursiveAclRestore.Enqueued);
    }
    [Theory][InlineData("instanceId")][InlineData("path")][InlineData("captureId")][InlineData("expectedHistoryDigest")][InlineData("expectedVersion")]
    public async Task RecursiveAclRestoreMvcOmittedSourceFieldsCannotDefault(string field)
    {
        await using var fixture=await Fixture.Start();using var client=await fixture.Client("admin-subject",true);var token=await Csrf(client);
        var body=System.Text.Json.JsonSerializer.SerializeToNode(RecursiveAclRestoreBody,System.Text.Json.JsonSerializerOptions.Web)!.AsObject();body.Remove(field);
        using var request=Write(client,HttpMethod.Post,RecursiveAclRestoreBase,token,JsonContent.Create(body),Guid.NewGuid().ToString("D"));using var response=await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);Assert.Equal(0,fixture.RecursiveAclRestore.Enqueued);Assert.Equal(0,fixture.RecursiveAclRestore.Requires);
    }
    [Fact]
    public async Task RecursiveAclRestoreMvcConfirmationUsesOriginalSignedPlanAndNeverCallsApply()
    {
        await using var fixture=await Fixture.Start();using var client=await fixture.Client("admin-subject",true);var token=await Csrf(client);
        using var queue=Write(client,HttpMethod.Post,RecursiveAclRestoreBase,token,JsonContent.Create(RecursiveAclRestoreBody),Guid.NewGuid().ToString("D"));using var accepted=await client.SendAsync(queue);Assert.Equal(HttpStatusCode.Accepted,accepted.StatusCode);
        var initial=(await accepted.Content.ReadFromJsonAsync<ContainerRecursiveAclRestoreIntentDto>())!;fixture.RecursiveAclRestore.MakeReady();
        var ready=(await client.GetFromJsonAsync<ContainerRecursiveAclRestoreIntentDto>($"{RecursiveAclRestoreBase}/{initial.Id:D}"))!;Assert.NotNull(ready.RestoreSource);Assert.NotNull(ready.Confirmation);
        Assert.Equal(1U,ready.RestoreSource.ChangeCount);Assert.Equal(2U,ready.ChangeCount);
        Guid? execution=null;
        for(var n=0;n<2;n++)
        {
            using var confirm=Write(client,HttpMethod.Post,$"{RecursiveAclRestoreBase}/{ready.Id:D}/confirm",token,JsonContent.Create(new{planHash=ready.Confirmation.PlanHash,confirmed=true,version=ready.Version}),ready.Id.ToString("D"));
            using var response=await client.SendAsync(confirm);Assert.Equal(HttpStatusCode.Accepted,response.StatusCode);var row=(await response.Content.ReadFromJsonAsync<ContainerRecursiveAclRestoreIntentDto>())!;
            Assert.Equal("Pending",row.State);Assert.Null(row.Receipt);Assert.Null(row.Confirmation);if(execution is not null)Assert.Equal(execution,row.ExecutionJobId);execution=row.ExecutionJobId;
        }
        Assert.Equal(0,fixture.RecursiveAclRestore.Mutations);
    }
    [Theory][InlineData("take=0")][InlineData("take=51")][InlineData("take=01")][InlineData("take=1&take=1")][InlineData("Take=1")][InlineData("extra=private-value")]
    public async Task RecursiveAclRestoreRecoveryQueryRejectsAmbiguousPaging(string query)
    {
        await using var fixture=await Fixture.Start();using var client=await fixture.Client("admin-subject",true);
        using var response=await client.GetAsync("/api/v1/files/container-recursive-acl-restores/recovery?"+query);Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
        var body=await response.Content.ReadAsStringAsync();Assert.Contains("CONTAINER_RECURSIVE_ACL_RESTORE_QUERY_INVALID",body,StringComparison.Ordinal);Assert.DoesNotContain("private-value",body,StringComparison.Ordinal);Assert.Equal(0,fixture.RecursiveAclRestore.Enqueued);
    }
    private sealed class RecursiveAclRestoreHttpFixture : IContainerRecursiveAclRestoreRepository, IContainerRecursiveAclRestoreGateway, IPrivilegedIdentityDirectory
    {
        private ContainerRecursiveAclRestoreIntent? _row;
        public WhaleDeck.Infrastructure.Agent.FilePlanProtector? Protector {get;set;}
        public void MakeReady()
        {
            var row=_row!;var command=ContainerRecursiveAclRestoreWorkflowValidation.ReadCommand(row);
            var plan=new ContainerRecursiveAclRestorePlanDto(1,command,new('C',64),new(1,2),new('D',64),5100,5101,new('E',64),new(1,3,16872,1000,1001,2,4096,1),2,new('F',64),4,2,2,2,
                new(command.CaptureId,command.ExpectedHistoryDigest,0,"666:2026-11-03T01:02:03Z","access",new('7',64),4,2,2,1),new(1,4,0x8180,0,0,1,1024,1,2),new('6',64));
            row.PlanJson=ContainerRecursiveAclRestoreWorkflowValidation.Serialize(plan);row.ReadyExpiresAtUtc=ContainerRecursiveAclRestoreWorkflowValidation.Time(DateTimeOffset.UtcNow).AddMinutes(5);
            row.PlanHash=Protector!.FingerprintRecursiveAclRestorePlan(ContainerRecursiveAclRestoreWorkflowValidation.PlanBinding(row));row.State="Ready";row.ResultCode="CONTAINER_RECURSIVE_ACL_RESTORE_READY";row.Version++;
        }
        public bool Allowed { get; set; } = true; public int Requires { get; private set; } public int Enqueued { get; private set; } public int Mutations { get; private set; }
        public Task<CurrentUserDto> ResolveForPrivilegedActionAsync(string subject,string? name,CancellationToken cancellationToken) => Task.FromResult(new CurrentUserDto(subject,name,[],Allowed,DateTimeOffset.UtcNow.AddMinutes(10)));
        public Task RequireContainerRecursiveAclRestoreWorkflowAsync(CancellationToken cancellationToken) { Requires++; return Task.CompletedTask; }
        public Task<ContainerRecursiveAclRestoreIntent?> OwnedAsync(TerminalActor actor,Guid id,CancellationToken cancellationToken) => Task.FromResult(_row is { } row && row.Id==id && row.ActorSubject==actor.Subject && row.LoginBinding==actor.LoginBinding ? row:null);
        public Task<ContainerRecursiveAclRestoreIntent?> FindAsync(Guid id,CancellationToken cancellationToken) => Task.FromResult(_row?.Id==id ? _row:null);
        public Task<IReadOnlyList<ContainerRecursiveAclRestoreIntent>> UnresolvedAsync(int take,CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ContainerRecursiveAclRestoreIntent>>([]);
        public Task<ContainerRecursiveAclRestoreIntent> EnqueueAsync(ContainerRecursiveAclRestoreIntent row,CancellationToken cancellationToken) { Enqueued++; _row=row; return Task.FromResult(row); }
        public Task CancelAsync(TerminalActor actor,Guid id,CancellationToken cancellationToken) { _row!.State="Canceled"; _row.ResultCode="CONTAINER_RECURSIVE_ACL_RESTORE_CANCELED"; _row.Version++; return Task.CompletedTask; }
        public Task<ContainerRecursiveAclRestorePlanDto> PlanContainerRecursiveAclRestoreAsync(TerminalActor actor,ContainerRecursiveAclRestoreCommandDto command,CancellationToken cancellationToken) { Mutations++; throw new NotSupportedException(); }
        public Task<ContainerRecursiveAclRestoreReceiptDto> ApplyContainerRecursiveAclRestoreAsync(ContainerRecursiveAclRestoreOperationDto operation,CancellationToken cancellationToken) { Mutations++; throw new NotSupportedException(); }
        public Task<ContainerRecursiveAclRestoreReceiptDto?> InspectContainerRecursiveAclRestoreAsync(ContainerRecursiveAclRestoreOperationDto operation,CancellationToken cancellationToken) { Mutations++; throw new NotSupportedException(); }
        public Task<ContainerRecursiveAclRestoreReceiptDto> DiscardContainerRecursiveAclRestoreAsync(ContainerRecursiveAclRestoreOperationDto operation,CancellationToken cancellationToken) { Mutations++; throw new NotSupportedException(); }
        public Task<ContainerRecursiveAclRestoreIntent> ConfirmAsync(TerminalActor actor,ContainerRecursiveAclRestoreConfirmation claims,long version,CancellationToken cancellationToken)
        {
            var row=_row!;if(row.ExecutionJobId is not null)return Task.FromResult(row);Assert.Equal(row.Version,version);
            row.ExecutionJobId=ContainerRecursiveAclRestoreWorkflowValidation.ExecutionId(row.Id);row.ConfirmedAtUtc=ContainerRecursiveAclRestoreWorkflowValidation.Time(DateTimeOffset.UtcNow);row.ExecutionExpiresAtUtc=row.ConfirmedAtUtc.Value.AddMinutes(15);
            row.ExecutionHash=Protector!.FingerprintRecursiveAclRestoreExecution(ContainerRecursiveAclRestoreWorkflowValidation.ExecutionBinding(row));row.State="Pending";row.ResultCode="CONTAINER_RECURSIVE_ACL_RESTORE_ACCEPTED";row.Version++;return Task.FromResult(row);
        }
        public Task<ContainerRecursiveAclRestoreIntent?> ClaimAsync(DateTimeOffset now,CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> CancellationRequestedAsync(Guid id,CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> MarkPlanningAsync(ContainerRecursiveAclRestoreIntent claimed,CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task FinishPlanningAsync(ContainerRecursiveAclRestoreIntent claimed,ContainerRecursiveAclRestorePlanDto? plan,string code,CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ContainerFileUploadStartResult> StartedAsync(ContainerRecursiveAclRestoreIntent claimed,DateTimeOffset now,CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> RenewAsync(ContainerRecursiveAclRestoreIntent claimed,DateTimeOffset now,CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> FinishAsync(ContainerRecursiveAclRestoreIntent claimed,string state,string code,ContainerRecursiveAclRestoreReceiptDto? receipt,CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeferAsync(ContainerRecursiveAclRestoreIntent claimed,CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AcknowledgeAsync(TerminalActor actor,Guid id,long version,ContainerRecursiveAclRestoreReceiptDto receipt,CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
