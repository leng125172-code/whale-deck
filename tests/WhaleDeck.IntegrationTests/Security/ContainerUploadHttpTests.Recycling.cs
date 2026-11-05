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
    private static string RecycleBase => $"/api/v1/containers/{Cid}/files/recycles";
    private static readonly string[] RecycleSuffixes = ["", "/by-key/aa0a8865-343e-49bd-b01a-72680131bf39", "/aa0a8865-343e-49bd-b01a-72680131bf39", "/aa0a8865-343e-49bd-b01a-72680131bf39/confirm", "/aa0a8865-343e-49bd-b01a-72680131bf39/cancel"];
    private static object RecycleBody => new { instanceId=Instance,path="work/tree",expectedVersion=new string('A',64) };
    [Theory][InlineData("anonymous", false, HttpStatusCode.Unauthorized)][InlineData("ordinary-subject", false, HttpStatusCode.Forbidden)]
    public async Task RecycleEveryRouteRejectsUnprivilegedCookiesBeforeService(string subject, bool admin, HttpStatusCode expected)
    {
        await using var fixture = await Fixture.Start(); using var client = await fixture.Client(subject, admin); var token = await Csrf(client);
        foreach (var suffix in RecycleSuffixes)
        {
            var write = suffix == "" || suffix.EndsWith("confirm", StringComparison.Ordinal) || suffix.EndsWith("cancel", StringComparison.Ordinal);
            using var request = write ? Write(client,HttpMethod.Post,RecycleBase+suffix,token,JsonContent.Create(RecycleBody)) : new HttpRequestMessage(HttpMethod.Get,RecycleBase+suffix);
            using var response = await client.SendAsync(request); Assert.Equal(expected,response.StatusCode);
        }
        const string recovery = "/api/v1/files/container-recycles/recovery";
        foreach (var suffix in new[] { "", "/aa0a8865-343e-49bd-b01a-72680131bf39", "/aa0a8865-343e-49bd-b01a-72680131bf39/acknowledge" })
        {
            using var request = suffix.EndsWith("acknowledge",StringComparison.Ordinal) ? Write(client,HttpMethod.Post,recovery+suffix,token,JsonContent.Create(new { version=0,confirmed=true })) : new HttpRequestMessage(HttpMethod.Get,recovery+suffix);
            using var response = await client.SendAsync(request); Assert.Equal(expected,response.StatusCode);
        }
        Assert.Equal(0,fixture.Recycle.Requires); Assert.Equal(0,fixture.Recycle.Enqueued);
    }
    [Theory][InlineData("csrf")][InlineData("origin")][InlineData("missing-origin")]
    public async Task RecycleWritesRejectInvalidCsrfOrOrigin(string variant)
    {
        await using var fixture = await Fixture.Start(); using var client = await fixture.Client("admin-subject",true); var token = await Csrf(client);
        foreach (var suffix in new[] { "", "/aa0a8865-343e-49bd-b01a-72680131bf39/confirm", "/aa0a8865-343e-49bd-b01a-72680131bf39/cancel" })
        {
            using var request = Write(client,HttpMethod.Post,RecycleBase+suffix,variant=="csrf" ? null:token,JsonContent.Create(RecycleBody));
            if (variant != "csrf") request.Headers.Remove("Origin");
            if (variant == "origin") request.Headers.Add("Origin","http://unapproved.invalid");
            using var response = await client.SendAsync(request); Assert.Contains(response.StatusCode,new[] { HttpStatusCode.BadRequest,HttpStatusCode.Forbidden });
        }
        using var ack = Write(client,HttpMethod.Post,"/api/v1/files/container-recycles/recovery/aa0a8865-343e-49bd-b01a-72680131bf39/acknowledge",null,JsonContent.Create(new {version=0,confirmed=true}));
        using var denied = await client.SendAsync(ack); Assert.Equal(HttpStatusCode.BadRequest,denied.StatusCode);
        Assert.Equal(0,fixture.Recycle.Requires); Assert.Equal(0,fixture.Recycle.Enqueued);
    }
    [Fact]
    public async Task RecycleRealMvcQueuesOnlyMetadataAndResolvesSameOriginalLoginKey()
    {
        await using var fixture = await Fixture.Start(); using var client = await fixture.Client("admin-subject",true); var token = await Csrf(client); var key = Guid.NewGuid().ToString("D");
        async Task<ContainerRecycleIntentDto> Queue()
        {
            using var request = Write(client,HttpMethod.Post,RecycleBase,token,JsonContent.Create(RecycleBody),key); using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Accepted,response.StatusCode); Assert.Contains("no-store",response.Headers.CacheControl!.ToString(),StringComparison.OrdinalIgnoreCase);
            var row = (await response.Content.ReadFromJsonAsync<ContainerRecycleIntentDto>())!;
            Assert.Equal($"{RecycleBase}/{row.Id:D}",response.Headers.Location!.OriginalString); return row;
        }
        var first = await Queue(); var replay = await Queue(); Assert.Equal(first.Id,replay.Id); Assert.Equal("Queued",first.State); Assert.Null(first.Receipt); Assert.Null(first.Confirmation);
        Assert.Equal(1,fixture.Recycle.Enqueued); Assert.Equal(1,fixture.Recycle.Requires);
        using var query = await client.GetAsync(RecycleBase+"/by-key/"+key); Assert.Equal(HttpStatusCode.OK,query.StatusCode);
        Assert.Equal(first.Id,(await query.Content.ReadFromJsonAsync<ContainerRecycleIntentDto>())!.Id);
        using var otherLogin = await fixture.Client("admin-subject",true);
        using var foreign = await otherLogin.GetAsync($"{RecycleBase}/{first.Id:D}"); Assert.Equal(HttpStatusCode.NotFound,foreign.StatusCode);
        fixture.Recycle.Allowed = false;
        using var revoked = await client.GetAsync($"{RecycleBase}/{first.Id:D}"); Assert.Equal(HttpStatusCode.Forbidden,revoked.StatusCode);
        fixture.Recycle.Allowed = true;
        using var cancel = Write(client,HttpMethod.Post,$"{RecycleBase}/{first.Id:D}/cancel",token,null,first.Id.ToString("D")); using var canceled = await client.SendAsync(cancel);
        Assert.Equal(HttpStatusCode.Accepted,canceled.StatusCode); Assert.Equal("Canceled",(await canceled.Content.ReadFromJsonAsync<ContainerRecycleIntentDto>())!.State);
        Assert.Equal(0,fixture.Recycle.Mutations);
    }
    [Fact]
    public async Task RecycleMvcMalformedBodyNeverEchoesInputOrCallsDependencies()
    {
        await using var fixture = await Fixture.Start(); using var client = await fixture.Client("admin-subject",true); var token = await Csrf(client);
        using var body = new StringContent("{\"uid\":0,\"uid\":\"private-must-not-leak\"}",System.Text.Encoding.UTF8,"application/json");
        using var request = Write(client,HttpMethod.Post,RecycleBase,token,body,Guid.NewGuid().ToString("D")); using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode); var content = await response.Content.ReadAsStringAsync(); Assert.Contains("CONTAINER_RECYCLE_INPUT_INVALID",content,StringComparison.Ordinal); Assert.DoesNotContain("private-must-not-leak",content,StringComparison.Ordinal);
        Assert.Equal(0,fixture.Recycle.Requires); Assert.Equal(0,fixture.Recycle.Enqueued);
    }
    [Theory][InlineData("instanceId")][InlineData("path")][InlineData("expectedVersion")]
    public async Task RecycleMvcOmittedSourceFieldsCannotDefault(string field)
    {
        await using var fixture=await Fixture.Start();using var client=await fixture.Client("admin-subject",true);var token=await Csrf(client);
        var body=System.Text.Json.JsonSerializer.SerializeToNode(RecycleBody,System.Text.Json.JsonSerializerOptions.Web)!.AsObject();body.Remove(field);
        using var request=Write(client,HttpMethod.Post,RecycleBase,token,JsonContent.Create(body),Guid.NewGuid().ToString("D"));using var response=await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);Assert.Equal(0,fixture.Recycle.Enqueued);Assert.Equal(0,fixture.Recycle.Requires);
    }
    [Fact]
    public async Task RecycleMvcConfirmationUsesOriginalSignedPlanAndNeverCallsApply()
    {
        await using var fixture=await Fixture.Start();using var client=await fixture.Client("admin-subject",true);var token=await Csrf(client);
        using var queue=Write(client,HttpMethod.Post,RecycleBase,token,JsonContent.Create(RecycleBody),Guid.NewGuid().ToString("D"));using var accepted=await client.SendAsync(queue);Assert.Equal(HttpStatusCode.Accepted,accepted.StatusCode);
        var initial=(await accepted.Content.ReadFromJsonAsync<ContainerRecycleIntentDto>())!;fixture.Recycle.MakeReady();
        var ready=(await client.GetFromJsonAsync<ContainerRecycleIntentDto>($"{RecycleBase}/{initial.Id:D}"))!;Assert.Equal(7,ready.SizeBytes);Assert.NotNull(ready.Confirmation);
        Guid? execution=null;
        for(var n=0;n<2;n++)
        {
            using var confirm=Write(client,HttpMethod.Post,$"{RecycleBase}/{ready.Id:D}/confirm",token,JsonContent.Create(new{planHash=ready.Confirmation.PlanHash,confirmed=true,version=ready.Version}),ready.Id.ToString("D"));
            using var response=await client.SendAsync(confirm);Assert.Equal(HttpStatusCode.Accepted,response.StatusCode);var row=(await response.Content.ReadFromJsonAsync<ContainerRecycleIntentDto>())!;
            Assert.Equal("Pending",row.State);Assert.Null(row.Receipt);Assert.Null(row.Confirmation);if(execution is not null)Assert.Equal(execution,row.ExecutionJobId);execution=row.ExecutionJobId;
        }
        Assert.Equal(0,fixture.Recycle.Mutations);
    }
    [Theory][InlineData("take=0")][InlineData("take=51")][InlineData("take=01")][InlineData("take=1&take=1")][InlineData("Take=1")][InlineData("extra=private-value")]
    public async Task RecycleRecoveryQueryRejectsAmbiguousPaging(string query)
    {
        await using var fixture=await Fixture.Start();using var client=await fixture.Client("admin-subject",true);
        using var response=await client.GetAsync("/api/v1/files/container-recycles/recovery?"+query);Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
        var body=await response.Content.ReadAsStringAsync();Assert.Contains("CONTAINER_RECYCLE_QUERY_INVALID",body,StringComparison.Ordinal);Assert.DoesNotContain("private-value",body,StringComparison.Ordinal);Assert.Equal(0,fixture.Recycle.Enqueued);
    }
    [Fact]
    public async Task RecycleMvcOtherAdministratorExplicitlyAcknowledgesWithoutInventingSourceFacts()
    {
        await using var f=await Fixture.Start();using var owner=await f.Client("admin-subject",true);var token=await Csrf(owner);
        using var queue=Write(owner,HttpMethod.Post,RecycleBase,token,JsonContent.Create(RecycleBody),Guid.NewGuid().ToString("D"));
        using var accepted=await owner.SendAsync(queue);Assert.Equal(HttpStatusCode.Accepted,accepted.StatusCode);var initial=(await accepted.Content.ReadFromJsonAsync<ContainerRecycleIntentDto>())!;
        f.Recycle.MakeReady();var ready=(await owner.GetFromJsonAsync<ContainerRecycleIntentDto>($"{RecycleBase}/{initial.Id:D}"))!;
        using var confirm=Write(owner,HttpMethod.Post,$"{RecycleBase}/{ready.Id:D}/confirm",token,JsonContent.Create(new{planHash=ready.Confirmation!.PlanHash,confirmed=true,version=ready.Version}),ready.Id.ToString("D"));
        using var confirmed=await owner.SendAsync(confirm);Assert.Equal(HttpStatusCode.Accepted,confirmed.StatusCode);f.Recycle.MakeUnknown();
        using var admin=await f.Client("another-admin",true);var csrf=await Csrf(admin);var url=$"/api/v1/files/container-recycles/recovery/{ready.Id:D}";
        var unknown=(await admin.GetFromJsonAsync<ContainerRecycleIntentDto>(url))!;Assert.Null(unknown.Receipt!.SourceRetained);Assert.Null(unknown.Receipt.SourceRestored);
        using var refuse=Write(admin,HttpMethod.Post,url+"/acknowledge",csrf,JsonContent.Create(new{version=unknown.Version,confirmed=false}),ready.Id.ToString("D"));
        using var refused=await admin.SendAsync(refuse);Assert.Equal(HttpStatusCode.BadRequest,refused.StatusCode);Assert.Equal(0,f.Recycle.Mutations);
        using var ack=Write(admin,HttpMethod.Post,url+"/acknowledge",csrf,JsonContent.Create(new{version=unknown.Version,confirmed=true}),ready.Id.ToString("D"));
        using var done=await admin.SendAsync(ack);Assert.Equal(HttpStatusCode.Accepted,done.StatusCode);Assert.Equal(1,f.Recycle.Mutations);
        var final=(await admin.GetFromJsonAsync<ContainerRecycleIntentDto>(url))!;Assert.Equal("Acknowledged",final.State);Assert.Equal("Unknown",final.Receipt!.State);
        Assert.Null(final.Receipt.SourceRetained);Assert.Null(final.Receipt.SourceRestored);
    }
    private sealed class RecycleHttpFixture : IContainerRecycleRepository, IContainerRecycleGateway, IPrivilegedIdentityDirectory
    {
        private ContainerRecycleIntent? _row;
        public WhaleDeck.Infrastructure.Agent.FilePlanProtector? Protector {get;set;}
        public void MakeReady()
        {
            var row=_row!;var command=ContainerRecycleWorkflowValidation.ReadCommand(row);
            var plan=new ContainerRecyclePlanDto(1,command,new('C',64),new(1,2),new('D',64),5100,5101,new('E',64),new(1,3,33184,1000,1001,1,7,1),2,new('F',64),[new("user.note",[0,255])]);
            row.PlanJson=ContainerRecycleWorkflowValidation.Serialize(plan);row.ReadyExpiresAtUtc=ContainerRecycleWorkflowValidation.Time(DateTimeOffset.UtcNow).AddMinutes(5);
            row.PlanHash=Protector!.FingerprintRecyclePlan(ContainerRecycleWorkflowValidation.PlanBinding(row));row.State="Ready";row.ResultCode="CONTAINER_RECYCLE_READY";row.Version++;
        }
        public void MakeUnknown()
        {
            var row=_row!;row.State="Unknown";row.RootStartedAtUtc=DateTimeOffset.UtcNow;row.PublishBarrier=true;row.HistoryMayExist=true;
            var op=ContainerRecycleWorkflowValidation.Reference(row);
            var receipt=new ContainerRecycleReceiptDto(op.Id,row.ContainerId,row.InstanceId,op.Plan.Fingerprint,op.Plan.TargetProof,"Unknown",false,false,true,false,"",false,null,null);
            row.ReceiptJson=ContainerRecycleWorkflowValidation.Serialize(receipt);row.ReceiptHash=Protector!.FingerprintRecycleReceipt(new(op,receipt));
            row.RootState="Unknown";row.BindingCurrent=false;row.HistoryAvailable=false;row.Version++;
        }
        public bool Allowed { get; set; } = true; public int Requires { get; private set; } public int Enqueued { get; private set; } public int Mutations { get; private set; }
        public Task<CurrentUserDto> ResolveForPrivilegedActionAsync(string subject,string? name,CancellationToken cancellationToken) => Task.FromResult(new CurrentUserDto(subject,name,[],Allowed,DateTimeOffset.UtcNow.AddMinutes(10)));
        public Task RequireContainerRecycleAsync(CancellationToken cancellationToken) { Requires++; return Task.CompletedTask; }
        public Task<ContainerRecycleIntent?> OwnedAsync(TerminalActor actor,Guid id,CancellationToken cancellationToken) => Task.FromResult(_row is { } row && row.Id==id && row.ActorSubject==actor.Subject && row.LoginBinding==actor.LoginBinding ? row:null);
        public Task<ContainerRecycleIntent?> FindAsync(Guid id,CancellationToken cancellationToken) => Task.FromResult(_row?.Id==id ? _row:null);
        public Task<IReadOnlyList<ContainerRecycleIntent>> UnresolvedAsync(int take,CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ContainerRecycleIntent>>([]);
        public Task<ContainerRecycleIntent> EnqueueAsync(ContainerRecycleIntent row,CancellationToken cancellationToken) { Enqueued++; _row=row; return Task.FromResult(row); }
        public Task CancelAsync(TerminalActor actor,Guid id,CancellationToken cancellationToken) { _row!.State="Canceled"; _row.ResultCode="CONTAINER_RECYCLE_CANCELED"; _row.Version++; return Task.CompletedTask; }
        public Task<ContainerRecyclePlanDto> PlanContainerRecycleAsync(TerminalActor actor,ContainerRecycleCommandDto command,CancellationToken cancellationToken) { Mutations++; throw new NotSupportedException(); }
        public Task<ContainerRecycleReceiptDto> ApplyContainerRecycleAsync(ContainerRecycleOperationDto operation,CancellationToken cancellationToken) { Mutations++; throw new NotSupportedException(); }
        public Task<ContainerRecycleReceiptDto?> InspectContainerRecycleAsync(ContainerRecycleOperationDto operation,CancellationToken cancellationToken) { Mutations++; throw new NotSupportedException(); }
        public Task<ContainerRecycleReceiptDto> DiscardContainerRecycleAsync(ContainerRecycleOperationDto operation,CancellationToken cancellationToken) { Mutations++; Assert.Equal(_row!.ExecutionJobId,operation.Id); return Task.FromResult(ContainerRecycleWorkflowValidation.ReadReceipt(_row)!); }
        public Task<ContainerRecycleReceiptDto> RestoreContainerRecycleAsync(ContainerRecycleOperationDto operation,CancellationToken cancellationToken) { Mutations++; throw new NotSupportedException(); }
        public Task<ContainerRecycleIntent> ConfirmAsync(TerminalActor actor,ContainerRecycleConfirmation claims,long version,CancellationToken cancellationToken)
        {
            var row=_row!;if(row.ExecutionJobId is not null)return Task.FromResult(row);Assert.Equal(row.Version,version);
            row.ExecutionJobId=ContainerRecycleWorkflowValidation.ExecutionId(row.Id);row.ConfirmedAtUtc=ContainerRecycleWorkflowValidation.Time(DateTimeOffset.UtcNow);row.ExecutionExpiresAtUtc=row.ConfirmedAtUtc.Value.AddMinutes(15);
            row.ExecutionHash=Protector!.FingerprintRecycleExecution(ContainerRecycleWorkflowValidation.ExecutionBinding(row));row.State="Pending";row.ResultCode="CONTAINER_RECYCLE_ACCEPTED";row.Version++;return Task.FromResult(row);
        }
        public Task<ContainerRecycleIntent?> ClaimAsync(DateTimeOffset now,CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> CancellationRequestedAsync(Guid id,CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> MarkPlanningAsync(ContainerRecycleIntent claimed,CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task FinishPlanningAsync(ContainerRecycleIntent claimed,ContainerRecyclePlanDto? plan,string code,CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ContainerFileUploadStartResult> StartedAsync(ContainerRecycleIntent claimed,DateTimeOffset now,CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> RenewAsync(ContainerRecycleIntent claimed,DateTimeOffset now,CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> FinishAsync(ContainerRecycleIntent claimed,string state,string code,ContainerRecycleReceiptDto? receipt,CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeferAsync(ContainerRecycleIntent claimed,CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AcknowledgeAsync(TerminalActor actor,Guid id,long version,ContainerRecycleReceiptDto receipt,CancellationToken cancellationToken)
        { Assert.Equal(_row!.Version,version);ContainerRecycleWorkflowValidation.RequireCompatibleReceipt(_row,receipt);_row.State="Acknowledged";_row.CompletedAtUtc=DateTimeOffset.UtcNow;_row.PublishBarrier=false;_row.AcknowledgedBy=actor.Subject;_row.AcknowledgementJson=ContainerRecycleWorkflowValidation.Serialize(receipt);_row.Version++;return Task.CompletedTask; }
    }
}
