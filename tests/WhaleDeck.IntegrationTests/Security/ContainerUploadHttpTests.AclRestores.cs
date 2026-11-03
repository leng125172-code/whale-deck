using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Grpc.Core;
using WhaleDeck.Application.Models;

namespace WhaleDeck.IntegrationTests.Security;

/// <summary>Actual MVC/Cookie/CSRF, synthetic identity/history/repository; never claims installed recovery.</summary>
public sealed partial class ContainerUploadHttpTests
{
    private static readonly string[] RecoverySourceFields = ["accessEntryCount", "captureId", "changedAclType", "createdAtUnixMs", "defaultEntryCount", "kind", "originalMode"];
    private static string AclRestoreBase => AclBase+"/restores";
    private static object RestoreBody => new {instanceId=Instance,path="work/acl-file.bin",captureId=new string('a',32),expectedHistoryDigest=new string('B',64),expectedVersion=new string('A',64)};
    [Theory][InlineData(false)][InlineData(true)]
    public async Task RecoverySourceIsMinimalNoStoreAndDoesNotExpandOwnerAccess(bool restore)
    {
        await using var f = await Fixture.Start(); using var owner = await f.Client("admin-subject", true);
        using var request = Write(owner, HttpMethod.Post, restore ? AclRestoreBase : AclBase, await Csrf(owner),
            restore ? JsonContent.Create(RestoreBody) : JsonContent.Create(AclBody), Guid.NewGuid().ToString("D"));
        using var accepted = await owner.SendAsync(request); Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        using var body = JsonDocument.Parse(await accepted.Content.ReadAsStringAsync());
        var id = (restore ? body.RootElement.GetProperty("intent") : body.RootElement).GetProperty("id").GetGuid();
        f.Directory.AdditionalAdmin = "another-admin"; using var other = await f.Client("another-admin", true); f.AclHistory.Failure = StatusCode.Unavailable;
        using var response = await other.GetAsync($"/api/v1/files/container-acls/recovery/{id:D}/restore-source");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.True(response.Headers.CacheControl!.NoStore);
        using var summary = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (restore)
        {
            Assert.Equal(RecoverySourceFields,
                summary.RootElement.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
            Assert.Equal(new string('a', 32), summary.RootElement.GetProperty("captureId").GetString());
        }
        else Assert.Equal(JsonValueKind.Null, summary.RootElement.ValueKind);
        using var foreign = await other.GetAsync($"{(restore ? AclRestoreBase : AclBase)}/{id:D}"); Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        f.Acl.Allowed = false;
        using var revoked = await other.GetAsync($"/api/v1/files/container-acls/recovery/{id:D}/restore-source"); Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
        Assert.Equal(restore ? 1 : 0, f.AclHistory.Reads); Assert.Equal(0, f.Acl.Mutations);
    }
    [Theory][InlineData("anonymous",false,401)][InlineData("ordinary",false,403)]
    public async Task RestoreEverySurfaceRequiresAdministratorBeforeSourceAccess(string subject,bool admin,int expected)
    {
        await using var f=await Fixture.Start();using var client=await f.Client(subject,admin);var token=await Csrf(client);var id=Guid.NewGuid().ToString("D");
        foreach(var suffix in new[]{"","/"+id,"/by-key/"+id})
        {using var request=suffix.Length==0?Write(client,HttpMethod.Post,AclRestoreBase,token,JsonContent.Create(RestoreBody),id):new HttpRequestMessage(HttpMethod.Get,AclRestoreBase+suffix);using var response=await client.SendAsync(request);Assert.Equal(expected,(int)response.StatusCode);}
        Assert.Equal(0,f.AclHistory.Reads+f.AclHistory.Requires+f.Acl.Enqueued+f.Acl.Mutations);
    }
    [Theory][InlineData("csrf")][InlineData("origin")][InlineData("missing-origin")]
    public async Task RestoreRegistrationRequiresCsrfAndOrigin(string cause)
    {
        await using var f=await Fixture.Start();using var client=await f.Client("admin-subject",true);var token=await Csrf(client);
        using var request=Write(client,HttpMethod.Post,AclRestoreBase,cause=="csrf"?null:token,JsonContent.Create(RestoreBody),Guid.NewGuid().ToString("D"));
        if(cause!="csrf")request.Headers.Remove("Origin");if(cause=="origin")request.Headers.Add("Origin","http://unapproved.invalid");
        using var response=await client.SendAsync(request);Assert.Contains(response.StatusCode,new[]{HttpStatusCode.BadRequest,HttpStatusCode.Forbidden});Assert.Equal(0,f.Acl.Enqueued+f.AclHistory.Reads);
    }
    [Theory][InlineData("zero")][InlineData("duplicate")][InlineData("uppercase")]
    public async Task RestoreRejectsNoncanonicalKeysBeforeReadingSource(string cause)
    {
        await using var f=await Fixture.Start();using var client=await f.Client("admin-subject",true);var key=cause=="zero"?Guid.Empty.ToString("D"):"ac12ac12-ac12-4c12-ac12-ac12ac12ac12";
        using var request=Write(client,HttpMethod.Post,AclRestoreBase,await Csrf(client),JsonContent.Create(RestoreBody),cause=="uppercase"?key.ToUpperInvariant():key);
        if(cause=="duplicate")request.Headers.Add("Idempotency-Key",key);
        using var response=await client.SendAsync(request);Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);Assert.Equal(0,f.AclHistory.Reads+f.Acl.Enqueued);
    }
    [Theory][InlineData("missing")][InlineData("duplicate")][InlineData("entries")][InlineData("wrong-type")][InlineData("null")]
    public async Task RestoreBodyIsStrictAndNeverAcceptsClientAclOrEchoesPrivateFields(string cause)
    {
        await using var f=await Fixture.Start();using var client=await f.Client("admin-subject",true);var root=JsonSerializer.SerializeToNode(RestoreBody)!.AsObject();
        if(cause=="missing")root.Remove("expectedVersion");if(cause=="entries")root["entries"]="private-must-not-leak";if(cause=="wrong-type")root["captureId"]=true;
        var text=cause=="null"?"null":root.ToJsonString();if(cause=="duplicate")text=text[..^1]+",\"path\":\"private-must-not-leak\"}";
        using var request=Write(client,HttpMethod.Post,AclRestoreBase,await Csrf(client),new StringContent(text,Encoding.UTF8,"application/json"),Guid.NewGuid().ToString("D"));using var response=await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);Assert.DoesNotContain("private-must-not-leak",await response.Content.ReadAsStringAsync());Assert.Equal(0,f.Acl.Enqueued+f.AclHistory.Reads);
    }
    [Fact]
    public async Task RestoreUsesAuthoritativeAclAndOriginalKeyAcrossLostSourceCancelAndAnotherLogin()
    {
        await using var f=await Fixture.Start();using var client=await f.Client("admin-subject",true);var token=await Csrf(client);var key=Guid.NewGuid().ToString("D");
        async Task<ContainerAclRestoreIntentDto> Queue()
        {using var request=Write(client,HttpMethod.Post,AclRestoreBase,token,JsonContent.Create(RestoreBody),key);using var response=await client.SendAsync(request);Assert.Equal(HttpStatusCode.Accepted,response.StatusCode);Assert.True(response.Headers.CacheControl!.NoStore);var row=(await response.Content.ReadFromJsonAsync<ContainerAclRestoreIntentDto>())!;Assert.Equal($"{AclRestoreBase}/{row.Intent.Id:D}",response.Headers.Location!.OriginalString);return row;}
        var first=await Queue();Assert.Equal("Queued",first.Intent.State);Assert.Null(first.Intent.ExecutionJobId);Assert.Null(first.Intent.Receipt);Assert.Equal(0U,first.Intent.Command.Entries[1].Id);Assert.Equal(0U,first.Intent.Command.Entries[1].Permissions);
        Assert.Equal(first.Source.AccessAcl,first.Intent.Command.Entries);f.AclHistory.Failure=StatusCode.Unavailable;
        var replay=await Queue();Assert.Equal(first.Intent.Id,replay.Intent.Id);Assert.Equal(1,f.AclHistory.Reads);Assert.Equal(1,f.Acl.Enqueued);
        foreach(var suffix in new[]{"/by-key/"+key,"/"+first.Intent.Id.ToString("D")}){using var response=await client.GetAsync(AclRestoreBase+suffix);Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.True(response.Headers.CacheControl!.NoStore);Assert.Equal(first.Source.Entry,(await response.Content.ReadFromJsonAsync<ContainerAclRestoreIntentDto>())!.Source.Entry);}
        using var other=await f.Client("admin-subject",true);using var foreign=await other.GetAsync($"{AclRestoreBase}/{first.Intent.Id:D}");Assert.Equal(HttpStatusCode.NotFound,foreign.StatusCode);
        using var plain=Write(client,HttpMethod.Post,AclBase,token,JsonContent.Create(AclBody),key);using var conflict=await client.SendAsync(plain);Assert.Equal(HttpStatusCode.Conflict,conflict.StatusCode);
        using var cancel=Write(client,HttpMethod.Post,$"{AclBase}/{first.Intent.Id:D}/cancel",token,null,first.Intent.Id.ToString("D"));using var canceled=await client.SendAsync(cancel);Assert.Equal(HttpStatusCode.Accepted,canceled.StatusCode);
        using var status=await client.GetAsync($"{AclRestoreBase}/{first.Intent.Id:D}");Assert.Equal("Canceled",(await status.Content.ReadFromJsonAsync<ContainerAclRestoreIntentDto>())!.Intent.State);Assert.Equal(0,f.Acl.Mutations);
    }
    [Theory][InlineData(StatusCode.NotFound,409)][InlineData(StatusCode.Aborted,409)][InlineData(StatusCode.Unavailable,503)][InlineData(StatusCode.PermissionDenied,403)][InlineData(StatusCode.Unimplemented,503)]
    public async Task RestoreSourceFailureNeverRegistersOrReturnsPrivateAcl(StatusCode error,int status)
    {
        await using var f=await Fixture.Start();using var client=await f.Client("admin-subject",true);f.AclHistory.Failure=error;
        using var request=Write(client,HttpMethod.Post,AclRestoreBase,await Csrf(client),JsonContent.Create(RestoreBody),Guid.NewGuid().ToString("D"));using var response=await client.SendAsync(request);
        Assert.Equal(status,(int)response.StatusCode);var text=await response.Content.ReadAsStringAsync();Assert.DoesNotContain("private-must-not-leak",text);Assert.DoesNotContain("accessAcl",text);Assert.Equal(0,f.Acl.Enqueued);
    }
    [Theory][InlineData("group")][InlineData("logout")][InlineData("changed-proof")]
    public async Task RestoreChangedIdentityOrSourceDuringReadCannotRegister(string cause)
    {
        await using var f=await Fixture.Start();using var client=await f.Client("admin-subject",true);
        if(cause=="group")f.AclHistory.AfterQuery=()=>{f.Acl.Allowed=false;return Task.CompletedTask;};
        else if(cause=="logout")f.AclHistory.AfterQuery=()=>f.Store.RemoveAsync(f.AdminKey!);
        else f.AclHistory.BadOutput=true;
        using var request=Write(client,HttpMethod.Post,AclRestoreBase,await Csrf(client),JsonContent.Create(RestoreBody),Guid.NewGuid().ToString("D"));using var response=await client.SendAsync(request);
        Assert.Equal(cause=="changed-proof"?HttpStatusCode.Conflict:HttpStatusCode.Forbidden,response.StatusCode);Assert.Equal(0,f.Acl.Enqueued);Assert.DoesNotContain("accessAcl",await response.Content.ReadAsStringAsync());
    }
    [Fact]
    public async Task RestoreStatusRejectsWrongContainerAndCurrentRevocationWithoutReadingHistory()
    {
        await using var f=await Fixture.Start();using var client=await f.Client("admin-subject",true);var key=Guid.NewGuid().ToString("D");
        using var request=Write(client,HttpMethod.Post,AclRestoreBase,await Csrf(client),JsonContent.Create(RestoreBody),key);using var accepted=await client.SendAsync(request);Assert.Equal(HttpStatusCode.Accepted,accepted.StatusCode);
        var row=(await accepted.Content.ReadFromJsonAsync<ContainerAclRestoreIntentDto>())!;
        using var wrong=await client.GetAsync($"/api/v1/containers/{new string('b',64)}/files/acls/restores/{row.Intent.Id:D}");Assert.Equal(HttpStatusCode.NotFound,wrong.StatusCode);
        f.Acl.Allowed=false;
        foreach(var suffix in new[]{"/"+row.Intent.Id.ToString("D"),"/by-key/"+key})
        {using var denied=await client.GetAsync(AclRestoreBase+suffix);Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);Assert.DoesNotContain("accessAcl",await denied.Content.ReadAsStringAsync());}
        Assert.Equal(1,f.AclHistory.Reads);Assert.Equal(1,f.Acl.Enqueued);Assert.Equal(0,f.Acl.Mutations);
    }
    [Fact]
    public async Task RestoreClientAbortCancelsSourceReadBeforeRegistration()
    {
        await using var f=await Fixture.Start();using var client=await f.Client("admin-subject",true);using var stop=new CancellationTokenSource();
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var canceled=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.AclHistory.DuringQuery=async token=>{entered.SetResult();try{await Task.Delay(Timeout.Infinite,token);}catch(OperationCanceledException){canceled.SetResult();throw;}};
        using var request=Write(client,HttpMethod.Post,AclRestoreBase,await Csrf(client),JsonContent.Create(RestoreBody),Guid.NewGuid().ToString("D"));var response=client.SendAsync(request,stop.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));await stop.CancelAsync();await Assert.ThrowsAnyAsync<OperationCanceledException>(async()=>await response);await canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));Assert.Equal(0,f.Acl.Enqueued+f.Acl.Mutations);
    }
    [Fact]
    public async Task RestoreDefaultEmptyIsExplicitAndOtherAclIsRetainedInSource()
    {
        await using var f=await Fixture.Start();using var client=await f.Client("admin-subject",true);f.AclHistory.RestoreEntry=new(new('a',32),new('B',64),0,"666:2026-10-26T01:02:03Z","default","Directory",416,5,0);
        using var request=Write(client,HttpMethod.Post,AclRestoreBase,await Csrf(client),JsonContent.Create(RestoreBody),Guid.NewGuid().ToString("D"));using var response=await client.SendAsync(request);Assert.Equal(HttpStatusCode.Accepted,response.StatusCode);
        var row=(await response.Content.ReadFromJsonAsync<ContainerAclRestoreIntentDto>())!;Assert.Equal("default",row.Intent.Command.AclType);Assert.Empty(row.Intent.Command.Entries);Assert.Equal(5,row.Source.AccessAcl.Count);Assert.Equal(0,f.Acl.Mutations);
    }
}
