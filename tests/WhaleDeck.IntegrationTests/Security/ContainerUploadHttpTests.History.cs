using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;

namespace WhaleDeck.IntegrationTests.Security;

public sealed partial class ContainerUploadHttpTests
{
    private static string FilesBase => $"/api/v1/containers/{Cid}/files";
    [Theory]
    [InlineData("anonymous",false,HttpStatusCode.Unauthorized)]
    [InlineData("ordinary-subject",false,HttpStatusCode.Forbidden)]
    public async Task HistoryAndRestorationRequireAdministratorForEveryRoute(string subject,bool admin,HttpStatusCode expected)
    {
        await using var fixture=await Fixture.Start();using var client=await fixture.Client(subject,admin);var csrf=await Csrf(client);
        foreach(var path in new[]{"history?instanceId="+Uri.EscapeDataString(Instance),"restores/aa0a8865-343e-49bd-b01a-72680131bf39"})
        {using var response=await client.GetAsync(FilesBase+"/"+path);Assert.Equal(expected,response.StatusCode);}
        foreach(var path in new[]{"restores/plan","restores","restores/aa0a8865-343e-49bd-b01a-72680131bf39/cancel"})
        {
            using var request=Write(client,HttpMethod.Post,FilesBase+"/"+path,csrf,JsonContent.Create(new{}),Guid.NewGuid().ToString("D"));
            using var response=await client.SendAsync(request);Assert.Equal(expected,response.StatusCode);
        }
        Assert.Equal(0,fixture.History.Lists);Assert.Equal(0,fixture.Repository.Enqueued);Assert.Equal(0,fixture.Gateway.Stages);
    }
    [Theory]
    [InlineData("private/file.bin", false)]
    [InlineData("recovered/历史副本.bin", true)]
    public async Task HistoryIsObservedAndRestorationRegistersWithoutAnyBrowserByteSurface(string destination, bool attributes)
    {
        await using var fixture=await Fixture.Start();using var client=await fixture.Client("admin-subject",true);var csrf=await Csrf(client);
        using(var response=await client.GetAsync(FilesBase+"/history?instanceId="+Uri.EscapeDataString(Instance)))
        {
            Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.Contains("no-store",response.Headers.CacheControl!.ToString(),StringComparison.OrdinalIgnoreCase);
            Assert.Single((await response.Content.ReadFromJsonAsync<ContainerHistoryPageDto>())!.Entries);Assert.True(fixture.History.Observed);
        }
        var source=fixture.History.Source;
        using var planning=Write(client,HttpMethod.Post,FilesBase+"/restores/plan",csrf,JsonContent.Create(new{instanceId=Instance,path=destination,captureId=source.CaptureId,expectedCaptureHash=source.CaptureHash,expectedVersion="ABSENT",restoreAttributes=attributes}));
        using var planned=await client.SendAsync(planning);Assert.Equal(HttpStatusCode.OK,planned.StatusCode);
        var plan=(await planned.Content.ReadFromJsonAsync<ContainerFileRestorePlanDto>())!;Assert.Equal(attributes,plan.RestoreAttributes);var key=Guid.NewGuid().ToString("D");
        using var create=Write(client,HttpMethod.Post,FilesBase+"/restores",csrf,JsonContent.Create(new SubmitFileUploadCommand(plan.PlanHash,true,key)),key);
        using var accepted=await client.SendAsync(create);Assert.Equal(HttpStatusCode.Accepted,accepted.StatusCode);
        var intent=(await accepted.Content.ReadFromJsonAsync<ContainerFileUploadIntentDto>())!;
        Assert.Equal(FilesBase+"/restores/"+intent.Id.ToString("D"),accepted.Headers.Location!.OriginalString);
        Assert.Equal(destination,intent.Location.Path);Assert.Equal(attributes,intent.RestoreAttributes);
        Assert.Equal(source.Path,WhaleDeck.Application.Services.ContainerFileRestoreValidation.Read(fixture.Repository.Row!.HistorySourceJson!).Path);
        Assert.Equal("AwaitingUpload",intent.State);Assert.Equal(0,fixture.Gateway.Stages);
        using var content=new ByteArrayContent(Data);content.Headers.ContentType=new("application/octet-stream");
        using var upload=Write(client,HttpMethod.Put,Base+"/"+intent.Id.ToString("D")+"/content",csrf,content,intent.Id.ToString("D"));
        using var rejected=await client.SendAsync(upload);Assert.Equal(HttpStatusCode.Conflict,rejected.StatusCode);Assert.Equal(0,fixture.Gateway.Stages);
        using var other=await fixture.Client("admin-subject",true);
        using var hidden=await other.GetAsync(FilesBase+"/restores/"+intent.Id.ToString("D"));Assert.Equal(HttpStatusCode.NotFound,hidden.StatusCode);
        using var cancel=Write(client,HttpMethod.Post,FilesBase+"/restores/"+intent.Id.ToString("D")+"/cancel",csrf,null,intent.Id.ToString("D"));
        using var canceled=await client.SendAsync(cancel);Assert.Equal(HttpStatusCode.Accepted,canceled.StatusCode);Assert.True(fixture.Repository.Canceled);
    }
    [Theory]
    [InlineData("csrf")]
    [InlineData("origin")]
    [InlineData("idempotency")]
    public async Task RestoreWritesCannotBypassCookieWriteProtection(string missing)
    {
        await using var fixture=await Fixture.Start();using var client=await fixture.Client("admin-subject",true);var csrf=await Csrf(client);
        using var request=Write(client,HttpMethod.Post,FilesBase+"/restores",missing=="csrf"?null:csrf,
            JsonContent.Create(new SubmitFileUploadCommand("unused",true,"aa0a8865-343e-49bd-b01a-72680131bf39")),missing=="idempotency"?null:"aa0a8865-343e-49bd-b01a-72680131bf39");
        if(missing=="origin")request.Headers.Remove("Origin");
        using var response=await client.SendAsync(request);Assert.True(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Forbidden);
        Assert.Equal(0,fixture.Repository.Enqueued);Assert.Equal(0,fixture.Gateway.Stages);
    }
    [Fact]
    public async Task RestoreOpenApiExposesPlansAndJobsButNoRawHistorySourceEndpoint()
    {
        await using var fixture=await Fixture.Start();using var client=await fixture.Client("anonymous",false);
        using var document=JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));var paths=document.RootElement.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/v1/containers/{containerId}/files/history",out _));
        var create=paths.GetProperty("/api/v1/containers/{containerId}/files/restores").GetProperty("post");Assert.True(create.GetProperty("responses").TryGetProperty("202",out _));
        Assert.False(create.GetProperty("requestBody").GetProperty("content").TryGetProperty("application/octet-stream",out _));
        Assert.DoesNotContain(paths.EnumerateObject(),item=>item.Name.Contains("history/source",StringComparison.Ordinal)||item.Name.EndsWith("restores/{id}/content",StringComparison.Ordinal));
    }
    private sealed class HistoryFixture : IContainerFileHistoryGateway,IContainerFileHistoryRepository,IContainerFileReadAudit
    {
        public bool Observed {get;private set;}
        public int Lists {get;private set;}
        public ContainerHistoryEntryDto Source {get;}=new(Guid.NewGuid().ToString("N"),Cid,"666:2026-10-26T01:02:03.123456789Z","private/file.bin",new('B',64),Data.Length,Convert.ToHexString(SHA256.HashData(Data)),0,false,"Present",new('C',64));
        public Task RequireContainerHistoryWorkflowAsync(CancellationToken cancellationToken)=>Task.CompletedTask;
        public Task<ContainerHistoryPageDto> ListContainerHistoryAsync(ContainerFileLocationDto location,string after,int take,string expectedVersion,CancellationToken cancellationToken)
        {Lists++;return Task.FromResult(new ContainerHistoryPageDto(location,new('B',64),[Source],new('D',64),"",1,0,true));}
        public IAsyncEnumerable<ReadOnlyMemory<byte>> ReadContainerHistorySourceAsync(ContainerFileLocationDto location,ContainerHistoryEntryDto expected,CancellationToken cancellationToken)=>throw new NotSupportedException();
        public Task ObserveAsync(TerminalActor actor,ContainerHistoryPageDto page,CancellationToken cancellationToken){Observed=true;return Task.CompletedTask;}
        public Task<ContainerHistoryEntryDto?> FindAsync(string containerId,string captureId,CancellationToken cancellationToken)=>Task.FromResult(Observed&&containerId==Cid&&captureId==Source.CaptureId?Source:null);
        public Task RecordAsync(TerminalActor actor,ContainerFileLocationDto location,string action,string result,string? version,long? sizeBytes,string? sha256,CancellationToken cancellationToken)=>Task.CompletedTask;
    }
}
