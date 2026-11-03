using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Grpc.Core;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;

namespace WhaleDeck.IntegrationTests.Security;

/// <summary>Real cookie/MVC service boundary with synthetic identity, Agent and audit; no installed evidence.</summary>
public sealed partial class ContainerUploadHttpTests
{
    private static readonly string[] AclHistoryAuditActions = ["acl-history-list", "acl-history-read"];
    private static string AclHistoryBase => $"/api/v1/containers/{Cid}/files/acls/history";
    private static string HistoryQuery => "?instanceId="+Uri.EscapeDataString(Instance)+"&path=work/acl-file.bin";
    private static string SnapshotUrl => AclHistoryBase+"/"+new string('a',32)+HistoryQuery+"&expectedHistoryDigest="+new string('B',64);

    [Theory][InlineData("anonymous",false,401)][InlineData("ordinary",false,403)]
    public async Task AclHistoryAnonymousAndOrdinaryUsersCannotQueryOrRead(string subject,bool admin,int status)
    {
        await using var f=await Fixture.Start();using var client=await f.Client(subject,admin);
        foreach(var path in new[]{AclHistoryBase+HistoryQuery,SnapshotUrl})
        {using var response=await client.GetAsync(path);Assert.Equal(status,(int)response.StatusCode);}
        Assert.Equal(0,f.AclHistory.Lists+f.AclHistory.Reads+f.AclHistory.Requires+f.AclHistory.Audits);
    }
    [Fact]
    public async Task AclHistoryReadsArePrivateAuditedAndPreserveExplicitRootZeroAndEmptyDefault()
    {
        await using var f=await Fixture.Start();using var client=await f.Client("admin-subject",true);
        using var response=await client.GetAsync(AclHistoryBase+HistoryQuery);
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.True(response.Headers.CacheControl!.NoStore);
        var page=(await response.Content.ReadFromJsonAsync<ContainerAclHistoryPageDto>())!;
        Assert.Single(page.Entries);Assert.True(page.Complete);Assert.Equal("",page.NextAfter);Assert.Equal(0U,page.Entries[0].DefaultEntryCount);
        using var snapshot=await client.GetAsync(SnapshotUrl);Assert.Equal(HttpStatusCode.OK,snapshot.StatusCode);Assert.True(snapshot.Headers.CacheControl!.NoStore);
        using var body=JsonDocument.Parse(await snapshot.Content.ReadAsStringAsync());var root=body.RootElement;
        Assert.Equal(0,root.GetProperty("entry").GetProperty("createdAtUnixMs").GetInt64());
        Assert.Equal(0U,root.GetProperty("accessAcl")[1].GetProperty("id").GetUInt32());Assert.Equal(0U,root.GetProperty("accessAcl")[1].GetProperty("permissions").GetUInt32());
        Assert.Empty(root.GetProperty("defaultAcl").EnumerateArray());
        Assert.Equal(AclHistoryAuditActions,f.AclHistory.Actions);
        Assert.Equal("admin-subject",f.AclHistory.Actor!.Subject);Assert.NotEmpty(f.AclHistory.Actor.LoginBinding);
        foreach(var forbidden in new[]{"owner","plan","sourceIdentity","hostPath"})Assert.False(root.TryGetProperty(forbidden,out _));
    }
    [Theory][InlineData("&take=0")][InlineData("&take=51")][InlineData("&take=1.0")][InlineData("&take=true")]
    [InlineData("&take=1&take=2")][InlineData("&path=other")][InlineData("&Path=other")][InlineData("&owner=private-must-not-leak")]
    [InlineData("&after=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task AclHistoryMalformedOrAmbiguousQueriesNeverReachRpc(string suffix)
    {
        await using var f=await Fixture.Start();using var client=await f.Client("admin-subject",true);
        using var response=await client.GetAsync(AclHistoryBase+HistoryQuery+suffix);Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
        Assert.DoesNotContain("private-must-not-leak",await response.Content.ReadAsStringAsync());Assert.Equal(0,f.AclHistory.Requires+f.AclHistory.Lists+f.AclHistory.Audits);
    }
    [Theory][InlineData("?path=work/x")][InlineData("?instanceId=missing")][InlineData("?instanceId=777%3A2026-10-27T01%3A02%3A03.123456789Z&path=")]
    public async Task AclHistoryRequiresCurrentInstanceAndExactNonemptyPath(string query)
    {
        await using var f=await Fixture.Start();using var client=await f.Client("admin-subject",true);
        using var response=await client.GetAsync(AclHistoryBase+query);Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);Assert.Equal(0,f.AclHistory.Lists);
    }
    [Theory][InlineData("group",false)][InlineData("group",true)][InlineData("logout",false)][InlineData("logout",true)]
    public async Task AclHistoryRevocationDuringRpcOrAuditCannotReturnPrivateAcl(string cause,bool afterAudit)
    {
        await using var f=await Fixture.Start();using var client=await f.Client("admin-subject",true);
        async Task Revoke(){if(cause=="group")f.Directory.Admin=false;else await f.Store.RemoveAsync(f.AdminKey!);}
        if(afterAudit)f.AclHistory.AfterAudit=Revoke;else f.AclHistory.AfterQuery=Revoke;
        using var response=await client.GetAsync(SnapshotUrl);Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
        Assert.DoesNotContain("accessAcl",await response.Content.ReadAsStringAsync());Assert.Equal(afterAudit?1:0,f.AclHistory.Audits);
    }
    [Theory][InlineData(StatusCode.NotFound,404)][InlineData(StatusCode.Aborted,409)][InlineData(StatusCode.Unimplemented,503)]
    [InlineData(StatusCode.Unavailable,503)][InlineData(StatusCode.ResourceExhausted,429)][InlineData(StatusCode.PermissionDenied,403)]
    public async Task AclHistoryDependencyFailuresAreNotEmptySuccessOrRawExceptions(StatusCode status,int expected)
    {
        await using var f=await Fixture.Start();using var client=await f.Client("admin-subject",true);f.AclHistory.Failure=status;
        using var response=await client.GetAsync(AclHistoryBase+HistoryQuery);Assert.Equal(expected,(int)response.StatusCode);
        Assert.DoesNotContain("private-must-not-leak",await response.Content.ReadAsStringAsync());Assert.Equal(0,f.AclHistory.Audits);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task AclHistoryInvalidRemoteProofOrFailedAuditCannotBeReturned(bool auditFailure)
    {
        await using var f=await Fixture.Start();using var client=await f.Client("admin-subject",true);
        f.AclHistory.BadOutput=!auditFailure;f.AclHistory.AuditFailure=auditFailure;
        using var response=await client.GetAsync(SnapshotUrl);Assert.Equal(HttpStatusCode.Conflict,response.StatusCode);
        Assert.DoesNotContain("accessAcl",await response.Content.ReadAsStringAsync());Assert.Equal(auditFailure?1:0,f.AclHistory.Audits);
    }
    [Fact]
    public async Task AclHistoryOpenApiOnlyExposesReadMethodsAndRejectsWrites()
    {
        await using var f=await Fixture.Start();using var client=await f.Client("admin-subject",true);
        using var document=JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));var paths=document.RootElement.GetProperty("paths");
        Assert.Single(paths.GetProperty("/api/v1/containers/{containerId}/files/acls/history").EnumerateObject(),p=>p.Name=="get");
        Assert.True(paths.GetProperty("/api/v1/containers/{containerId}/files/acls/history/{captureId}").TryGetProperty("get",out _));
        using var request=Write(client,HttpMethod.Post,AclHistoryBase,await Csrf(client));using var response=await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.MethodNotAllowed,response.StatusCode);Assert.Equal(0,f.AclHistory.Lists+f.AclHistory.Reads);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task AclHistoryFreezesValidatedArraysBeforeAwaitingAudit(bool read)
    {
        await using var f=await Fixture.Start();using var client=await f.Client("admin-subject",true);
        f.AclHistory.AfterAudit=()=>{f.AclHistory.PageEntries[0]=f.AclHistory.PageEntries[0] with{CaptureId=new('f',32)};f.AclHistory.AccessEntries[0]=new("user_obj",null,7);return Task.CompletedTask;};
        using var response=await client.GetAsync(read?SnapshotUrl:AclHistoryBase+HistoryQuery);Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        if(read){var value=(await response.Content.ReadFromJsonAsync<ContainerAclHistorySnapshotDto>())!;Assert.Equal(6U,value.AccessAcl[0].Permissions);}
        else{var value=(await response.Content.ReadFromJsonAsync<ContainerAclHistoryPageDto>())!;Assert.Equal(new string('a',32),value.Entries[0].CaptureId);}
    }
    [Fact]
    public async Task AclHistoryClientAbortPropagatesToGatewayAndDoesNotAuditOrReturnAnAcl()
    {
        await using var f=await Fixture.Start();using var client=await f.Client("admin-subject",true);using var stop=new CancellationTokenSource();
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var canceled=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.AclHistory.DuringQuery=async token=>{entered.SetResult();try{await Task.Delay(Timeout.Infinite,token);}catch(OperationCanceledException){canceled.SetResult();throw;}};
        var response=client.GetAsync(SnapshotUrl,stop.Token);await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));await stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async()=>await response);await canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));Assert.Equal(0,f.AclHistory.Audits);
    }
    private sealed class AclHistoryHttpFixture : IContainerAclHistoryGateway,IContainerFileReadAudit,IContainerAclRestoreSourceGateway
    {
        public int Requires,Lists,Reads,Audits;
        public bool BadOutput,AuditFailure;
        public StatusCode? Failure;
        public Func<Task>? AfterQuery,AfterAudit;
        public Func<CancellationToken,Task>? DuringQuery;
        public ContainerAclHistoryEntryDto[] PageEntries {get;}=[Entry];
        public ContainerAclEntryDto[] AccessEntries {get;}=Access.ToArray();
        public TerminalActor? Actor;
        public List<string> Actions {get;}=[];
        private static readonly ContainerAclEntryDto[] Access=[new("user_obj",null,6),new("user",0,0),new("group_obj",null,4),new("mask",null,4),new("other",null,0)];
        private static ContainerAclHistoryEntryDto Entry=>new(new('a',32),new('B',64),0,"666:2026-10-26T01:02:03Z","access","File",416,5,0);
        public ContainerAclHistoryEntryDto? RestoreEntry;
        public async Task<ContainerAclHistorySnapshotDto> ReadContainerAclRestoreSourceAsync(TerminalActor actor,ContainerFileLocationDto location,string captureId,string expectedHistoryDigest,CancellationToken cancellationToken)
        {
            Requires++;
            if(Failure is StatusCode.NotFound or StatusCode.Aborted) throw new ContainerAclRestoreSourceChangedException();
            if(Failure is {} status)throw new RpcException(new(status,"private-must-not-leak"));
            var result=await ReadContainerAclHistoryAsync(actor,location,captureId,expectedHistoryDigest,cancellationToken);
            return RestoreEntry is null?result:result with {Entry=RestoreEntry};
        }
        public Task RequireContainerAclHistoryAsync(CancellationToken token){Requires++;return Task.CompletedTask;}
        public async Task<ContainerAclHistoryPageDto> ListContainerAclHistoryAsync(TerminalActor actor,ContainerFileLocationDto location,string after,int take,string expectedVersion,CancellationToken cancellationToken)
        {Lists++;Actor=actor;if(Failure is {} status)throw new RpcException(new(status,"private-must-not-leak"));if(AfterQuery is not null)await AfterQuery();return new(location,new('C',64),PageEntries,"",1,0,true);}
        public async Task<ContainerAclHistorySnapshotDto> ReadContainerAclHistoryAsync(TerminalActor actor,ContainerFileLocationDto location,string captureId,string expectedHistoryDigest,CancellationToken cancellationToken)
        {Reads++;Actor=actor;if(DuringQuery is not null)await DuringQuery(cancellationToken);if(AfterQuery is not null)await AfterQuery();return new(location,Entry with{HistoryDigest=BadOutput?new('D',64):Entry.HistoryDigest},AccessEntries,[]);}
        public async Task RecordAsync(TerminalActor actor,ContainerFileLocationDto location,string action,string result,string? version,long? sizeBytes,string? sha256,CancellationToken cancellationToken)
        {Audits++;Actions.Add(action);Assert.Equal("Validated",result);Assert.Null(sizeBytes);Assert.Null(sha256);if(AuditFailure)throw new InvalidOperationException("private-must-not-leak");if(AfterAudit is not null)await AfterAudit();}
    }
}
