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
    private static readonly string[] RecursiveAclHistoryAuditActions = ["recursive-acl-history-list", "recursive-acl-history-read"];
    private static string RecursiveAclHistoryBase => $"/api/v1/containers/{Cid}/files/recursive-acls/history";
    private static string RecursiveHistoryQuery => "?instanceId="+Uri.EscapeDataString(Instance)+"&path=work/recursive-tree";
    private static string RecursiveHistorySnapshotUrl => RecursiveAclHistoryBase+"/"+new string('a',32)+RecursiveHistoryQuery+"&expectedHistoryDigest="+new string('B',64)+"&offset=0&take=25";

    [Theory][InlineData("anonymous",false,401)][InlineData("ordinary",false,403)]
    public async Task RecursiveAclHistoryAnonymousAndOrdinaryUsersCannotQueryOrRead(string subject,bool admin,int status)
    {
        await using var f=await Fixture.Start();using var client=await f.Client(subject,admin);
        foreach(var path in new[]{RecursiveAclHistoryBase+RecursiveHistoryQuery,RecursiveHistorySnapshotUrl})
        {using var response=await client.GetAsync(path);Assert.Equal(status,(int)response.StatusCode);}
        Assert.Equal(0,f.RecursiveAclHistory.Lists+f.RecursiveAclHistory.Reads+f.RecursiveAclHistory.Requires+f.RecursiveAclHistory.Audits);
    }
    [Fact]
    public async Task RecursiveAclHistoryReadsArePrivateAuditedAndPreserveExplicitRootZeroAndEmptyDefault()
    {
        await using var f=await Fixture.Start();using var client=await f.Client("admin-subject",true);
        using var response=await client.GetAsync(RecursiveAclHistoryBase+RecursiveHistoryQuery);
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.True(response.Headers.CacheControl!.NoStore);
        var page=(await response.Content.ReadFromJsonAsync<ContainerRecursiveAclHistoryPageDto>())!;
        Assert.Single(page.Entries);Assert.True(page.Complete);Assert.Equal("",page.NextAfter);Assert.Equal(0U,page.Entries[0].ChangeCount);
        using var snapshot=await client.GetAsync(RecursiveHistorySnapshotUrl);Assert.Equal(HttpStatusCode.OK,snapshot.StatusCode);Assert.True(snapshot.Headers.CacheControl!.NoStore);
        using var body=JsonDocument.Parse(await snapshot.Content.ReadAsStringAsync());var root=body.RootElement;
        Assert.Equal(0,root.GetProperty("summary").GetProperty("createdAtUnixMs").GetInt64());
        Assert.Equal(0U,root.GetProperty("entries")[0].GetProperty("accessAcl")[1].GetProperty("id").GetUInt32());Assert.Equal(0U,root.GetProperty("entries")[0].GetProperty("accessAcl")[1].GetProperty("permissions").GetUInt32());
        Assert.Empty(root.GetProperty("entries")[0].GetProperty("defaultAcl").EnumerateArray());
        Assert.Equal(RecursiveAclHistoryAuditActions,f.RecursiveAclHistory.Actions);
        Assert.Equal("admin-subject",f.RecursiveAclHistory.Actor!.Subject);Assert.NotEmpty(f.RecursiveAclHistory.Actor.LoginBinding);
        foreach(var forbidden in new[]{"owner","plan","sourceIdentity","hostPath"})Assert.False(root.TryGetProperty(forbidden,out _));
    }
    [Theory][InlineData("&take=0")][InlineData("&take=51")][InlineData("&take=1.0")][InlineData("&take=true")]
    [InlineData("&take=1&take=2")][InlineData("&path=other")][InlineData("&Path=other")][InlineData("&owner=private-must-not-leak")]
    [InlineData("&after=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task RecursiveAclHistoryMalformedOrAmbiguousQueriesNeverReachRpc(string suffix)
    {
        await using var f=await Fixture.Start();using var client=await f.Client("admin-subject",true);
        using var response=await client.GetAsync(RecursiveAclHistoryBase+RecursiveHistoryQuery+suffix);Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
        Assert.DoesNotContain("private-must-not-leak",await response.Content.ReadAsStringAsync());Assert.Equal(0,f.RecursiveAclHistory.Requires+f.RecursiveAclHistory.Lists+f.RecursiveAclHistory.Audits);
    }
    [Theory][InlineData("?path=work/x")][InlineData("?instanceId=missing")][InlineData("?instanceId=777%3A2026-10-27T01%3A02%3A03.123456789Z&path=")]
    public async Task RecursiveAclHistoryRequiresCurrentInstanceAndExactNonemptyPath(string query)
    {
        await using var f=await Fixture.Start();using var client=await f.Client("admin-subject",true);
        using var response=await client.GetAsync(RecursiveAclHistoryBase+query);Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);Assert.Equal(0,f.RecursiveAclHistory.Lists);
    }
    [Theory][InlineData("group",false)][InlineData("group",true)][InlineData("logout",false)][InlineData("logout",true)]
    public async Task RecursiveAclHistoryRevocationDuringRpcOrAuditCannotReturnPrivateAcl(string cause,bool afterAudit)
    {
        await using var f=await Fixture.Start();using var client=await f.Client("admin-subject",true);
        async Task Revoke(){if(cause=="group")f.Directory.Admin=false;else await f.Store.RemoveAsync(f.AdminKey!);}
        if(afterAudit)f.RecursiveAclHistory.AfterAudit=Revoke;else f.RecursiveAclHistory.AfterQuery=Revoke;
        using var response=await client.GetAsync(RecursiveHistorySnapshotUrl);Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
        Assert.DoesNotContain("accessAcl",await response.Content.ReadAsStringAsync());Assert.Equal(afterAudit?1:0,f.RecursiveAclHistory.Audits);
    }
    [Theory][InlineData(StatusCode.NotFound,404)][InlineData(StatusCode.Aborted,409)][InlineData(StatusCode.Unimplemented,503)]
    [InlineData(StatusCode.Unavailable,503)][InlineData(StatusCode.ResourceExhausted,429)][InlineData(StatusCode.PermissionDenied,403)]
    public async Task RecursiveAclHistoryDependencyFailuresAreNotEmptySuccessOrRawExceptions(StatusCode status,int expected)
    {
        await using var f=await Fixture.Start();using var client=await f.Client("admin-subject",true);f.RecursiveAclHistory.Failure=status;
        using var response=await client.GetAsync(RecursiveAclHistoryBase+RecursiveHistoryQuery);Assert.Equal(expected,(int)response.StatusCode);
        Assert.DoesNotContain("private-must-not-leak",await response.Content.ReadAsStringAsync());Assert.Equal(0,f.RecursiveAclHistory.Audits);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task RecursiveAclHistoryInvalidRemoteProofOrFailedAuditCannotBeReturned(bool auditFailure)
    {
        await using var f=await Fixture.Start();using var client=await f.Client("admin-subject",true);
        f.RecursiveAclHistory.BadOutput=!auditFailure;f.RecursiveAclHistory.AuditFailure=auditFailure;
        using var response=await client.GetAsync(RecursiveHistorySnapshotUrl);Assert.Equal(HttpStatusCode.Conflict,response.StatusCode);
        Assert.DoesNotContain("accessAcl",await response.Content.ReadAsStringAsync());Assert.Equal(auditFailure?1:0,f.RecursiveAclHistory.Audits);
    }
    [Fact]
    public async Task RecursiveAclHistoryOpenApiOnlyExposesReadMethodsAndRejectsWrites()
    {
        await using var f=await Fixture.Start();using var client=await f.Client("admin-subject",true);
        using var document=JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));var paths=document.RootElement.GetProperty("paths");
        Assert.Single(paths.GetProperty("/api/v1/containers/{containerId}/files/recursive-acls/history").EnumerateObject(),p=>p.Name=="get");
        Assert.True(paths.GetProperty("/api/v1/containers/{containerId}/files/recursive-acls/history/{captureId}").TryGetProperty("get",out _));
        using var request=Write(client,HttpMethod.Post,RecursiveAclHistoryBase,await Csrf(client));using var response=await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.MethodNotAllowed,response.StatusCode);Assert.Equal(0,f.RecursiveAclHistory.Lists+f.RecursiveAclHistory.Reads);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task RecursiveAclHistoryFreezesValidatedArraysBeforeAwaitingAudit(bool read)
    {
        await using var f=await Fixture.Start();using var client=await f.Client("admin-subject",true);
        f.RecursiveAclHistory.AfterAudit=()=>{f.RecursiveAclHistory.PageEntries[0]=f.RecursiveAclHistory.PageEntries[0] with{CaptureId=new('f',32)};f.RecursiveAclHistory.AccessEntries[0]=new("user_obj",null,7);f.RecursiveAclHistory.DefaultEntries[0]=new("user_obj",null,7);return Task.CompletedTask;};
        using var response=await client.GetAsync(read?RecursiveHistorySnapshotUrl:RecursiveAclHistoryBase+RecursiveHistoryQuery);Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        if(read){var value=(await response.Content.ReadFromJsonAsync<ContainerRecursiveAclHistorySnapshotDto>())!;Assert.Equal(6U,value.Entries[0].AccessAcl[0].Permissions);}
        else{var value=(await response.Content.ReadFromJsonAsync<ContainerRecursiveAclHistoryPageDto>())!;Assert.Equal(new string('a',32),value.Entries[0].CaptureId);}
    }
    [Fact]
    public async Task RecursiveAclHistoryClientAbortPropagatesToGatewayAndDoesNotAuditOrReturnAnAcl()
    {
        await using var f=await Fixture.Start();using var client=await f.Client("admin-subject",true);using var stop=new CancellationTokenSource();
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var canceled=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.RecursiveAclHistory.DuringQuery=async token=>{entered.SetResult();try{await Task.Delay(Timeout.Infinite,token);}catch(OperationCanceledException){canceled.SetResult();throw;}};
        var response=client.GetAsync(RecursiveHistorySnapshotUrl,stop.Token);await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));await stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async()=>await response);await canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));Assert.Equal(0,f.RecursiveAclHistory.Audits);
    }
    [Theory][InlineData("offset")][InlineData("take")][InlineData("expectedHistoryDigest")]
    public async Task RecursiveAclHistoryReadRequiresExplicitPageSelection(string missing)
    {
        await using var f=await Fixture.Start();using var client=await f.Client("admin-subject",true);
        var fields=new Dictionary<string,string>{{"instanceId",Instance},{"path","work/recursive-tree"},{"expectedHistoryDigest",new('B',64)},{"offset","0"},{"take","1"}};
        fields.Remove(missing);var query=string.Join("&",fields.Select(v=>v.Key+"="+Uri.EscapeDataString(v.Value)));
        using var response=await client.GetAsync(RecursiveAclHistoryBase+"/"+new string('a',32)+"?"+query);
        Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);Assert.Equal(0,f.RecursiveAclHistory.Requires+f.RecursiveAclHistory.Reads+f.RecursiveAclHistory.Audits);
    }
    [Theory][InlineData("offset=-1")][InlineData("offset=10000")][InlineData("offset=true")][InlineData("offset=1.0")]
    [InlineData("take=0")][InlineData("take=51")][InlineData("take=false")][InlineData("offset=0&offset=1")]
    public async Task RecursiveAclHistoryReadRejectsMalformedPageBeforeGateway(string replacement)
    {
        await using var f=await Fixture.Start();using var client=await f.Client("admin-subject",true);
        var url=RecursiveHistorySnapshotUrl.Replace(replacement.StartsWith("take",StringComparison.Ordinal)?"take=25":"offset=0",replacement,StringComparison.Ordinal);
        using var response=await client.GetAsync(url);Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
        Assert.Equal(0,f.RecursiveAclHistory.Requires+f.RecursiveAclHistory.Reads+f.RecursiveAclHistory.Audits);
    }
    [Fact]public async Task RecursiveAclHistoryPagesPreserveDigestAndDeepFrozenDirectoryAcl()
    {
        await using var f=await Fixture.Start();using var client=await f.Client("admin-subject",true);
        using var first=await client.GetAsync(RecursiveHistorySnapshotUrl.Replace("take=25","take=1",StringComparison.Ordinal));
        var page=(await first.Content.ReadFromJsonAsync<ContainerRecursiveAclHistorySnapshotDto>())!;
        Assert.False(page.Complete);Assert.Equal(1U,page.NextOffset);Assert.Equal("child.bin",Assert.Single(page.Entries).RelativePath);
        f.RecursiveAclHistory.AfterAudit=()=>{f.RecursiveAclHistory.DefaultEntries[0]=new("user_obj",null,7);f.RecursiveAclHistory.SourceEntries![1]=f.RecursiveAclHistory.SourceEntries[1] with{RelativePath="private"};return Task.CompletedTask;};
        using var last=await client.GetAsync(RecursiveHistorySnapshotUrl.Replace("offset=0","offset=1",StringComparison.Ordinal));
        var result=(await last.Content.ReadFromJsonAsync<ContainerRecursiveAclHistorySnapshotDto>())!;
        Assert.True(result.Complete);Assert.Null(result.NextOffset);Assert.Equal("",Assert.Single(result.Entries).RelativePath);Assert.Equal(6U,result.Entries[0].DefaultAcl[0].Permissions);
        Assert.Equal(2,f.RecursiveAclHistory.Audits);
    }
    private sealed class RecursiveAclHistoryHttpFixture : IContainerRecursiveAclHistoryGateway,IContainerRecursiveAclHistoryAudit
    {
        public int Requires,Lists,Reads,Audits;
        public bool BadOutput,AuditFailure;
        public StatusCode? Failure;
        public Func<Task>? AfterQuery,AfterAudit;
        public Func<CancellationToken,Task>? DuringQuery;
        public ContainerRecursiveAclHistorySummaryDto[] PageEntries {get;}=[Entry];
        public ContainerAclEntryDto[] AccessEntries {get;}=[new("user_obj",null,6),new("user",0,0),new("group_obj",null,4),new("mask",null,4),new("other",null,0)];
        public ContainerAclEntryDto[] DefaultEntries {get;}=[new("user_obj",null,6),new("group_obj",null,4),new("other",null,0)];
        public TerminalActor? Actor;
        public List<string> Actions {get;}=[];
        public ContainerRecursiveAclHistoryEntryDto[]? SourceEntries;
        private static ContainerRecursiveAclHistorySummaryDto Entry=>new(new('a',32),new('B',64),0,"666:2026-10-26T01:02:03Z","access",new('D',64),2,1,1,0);
        public Task RequireContainerRecursiveAclHistoryAsync(CancellationToken token){Requires++;return Task.CompletedTask;}
        public async Task<ContainerRecursiveAclHistoryPageDto> ListContainerRecursiveAclHistoryAsync(TerminalActor actor,ContainerFileLocationDto location,string after,int take,string expectedVersion,CancellationToken token)
        {Lists++;Actor=actor;if(Failure is {} status)throw new RpcException(new(status,"private-must-not-leak"));if(AfterQuery is not null)await AfterQuery();return new(location,new('C',64),PageEntries,"",1,0,true);}
        public async Task<ContainerRecursiveAclHistorySnapshotDto> ReadContainerRecursiveAclHistoryAsync(TerminalActor actor,ContainerFileLocationDto location,string captureId,string digest,int offset,int take,CancellationToken token)
        {
            Reads++;Actor=actor;if(DuringQuery is not null)await DuringQuery(token);if(AfterQuery is not null)await AfterQuery();
            SourceEntries=[new("child.bin","File",416,AccessEntries,[]),new("","Directory",416,AccessEntries,DefaultEntries)];
            var entries=SourceEntries.Skip(offset).Take(take).ToArray();var end=offset+entries.Length;
            return new(location,Entry with{HistoryDigest=BadOutput?new('E',64):Entry.HistoryDigest},(uint)offset,entries,end==2,end==2?null:(uint)end);
        }
        public Task ListAsync(TerminalActor actor,ContainerFileLocationDto location,string after,int take,string version,ContainerRecursiveAclHistoryPageDto result,CancellationToken token)=>Record("recursive-acl-history-list");
        public Task ReadAsync(TerminalActor actor,ContainerFileLocationDto location,string capture,string digest,int offset,int take,ContainerRecursiveAclHistorySnapshotDto result,CancellationToken token)=>Record("recursive-acl-history-read");
        private async Task Record(string action){Audits++;Actions.Add(action);if(AuditFailure)throw new InvalidOperationException("private-must-not-leak");if(AfterAudit is not null)await AfterAudit();}
    }
}
