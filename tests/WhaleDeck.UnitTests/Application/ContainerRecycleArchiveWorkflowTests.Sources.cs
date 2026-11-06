using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.UnitTests.Application;

public sealed partial class ContainerRecycleArchiveWorkflowTests
{
    [Fact]
    public async Task BrowserProjectionPreservesWideIdentityWithoutRounding()
    {
        using var f=new Fixture();await f.Create();f.Gateway.ApplyHandler=op=>Gateway.ReceiptFor(op,"Completed") with{PublishedChangedAtUnixNs=long.MaxValue};await f.Dispatcher.TickAsync(default);var value=await f.Get();
        Assert.Equal("998",value.Receipt!.PublishedIdentity!.Inode);Assert.Equal(f.Gateway.PlanValue.ParentIdentity.Device.ToString(System.Globalization.CultureInfo.InvariantCulture),value.Receipt.PublishedIdentity.Device);
        var text=System.Text.Json.JsonSerializer.Serialize(value,System.Text.Json.JsonSerializerOptions.Web);
        Assert.Contains("\"inode\":\"998\"",text,StringComparison.Ordinal);
        Assert.Contains("\"publishedChangedAtUnixNs\":\"9223372036854775807\"",text,StringComparison.Ordinal);
    }
    [Fact]
    public async Task JobSnapshotContainsOnlyNineteenHistoricalFields()
    {
        using var f=new Fixture();await f.Ready();var row=f.Repository.Row!;
        var text=ContainerRecycleArchiveJobResults.Snapshot(row,false,f.Original.Row.Id);
        Assert.Equal(19,System.Text.Json.JsonDocument.Parse(text).RootElement.EnumerateObject().Count());
        foreach(var secret in new[]{row.ActorSubject,row.LoginBinding,row.TraceId,row.OwnerHash,row.ContainerId,row.InstanceId,row.RelativePath,row.SourceProofHash,row.RequestHash,row.PlanHash!,f.Command.HistoryDigest})Assert.DoesNotContain(secret,text,StringComparison.Ordinal);
        Assert.NotNull(ContainerRecycleArchiveJobResults.ForJob(new(){Id=row.Id,JobType="containers.files.recycle-archive-plan",State="Succeeded",ActorSubject=row.ActorSubject,IdempotencyKey=row.IdempotencyKey.ToString("D"),ResultJson=text}));
    }
    private sealed class Catalog(IReadOnlyList<ContainerRecycleArchiveSourceRows> rows) : IContainerRecycleArchiveSourceCatalog
    {
        public int Calls { get; private set; }
        public Action? AfterRead { get; set; }
        public Task<IReadOnlyList<ContainerRecycleArchiveSourceRows>> ListAsync(string containerId,int take,Guid? after,CancellationToken cancellationToken)
        {Calls++;AfterRead?.Invoke();return Task.FromResult(rows);}
    }
    [Fact]
    public async Task SourcePageOnlyReturnsValidatedSummaryAndOpaqueCursor()
    {
        using var f=new Fixture();var second=MakePurge(f.Protector,f.Original.Row,f.Clock.Now);var catalog=new Catalog([new(f.Source.Row,f.Original.Row),new(second,f.Original.Row)]);
        var service=new ContainerRecycleArchiveSourcesService(f.Service,catalog,f.Protector,f.Protector,f.Protector);var page=await service.ListAsync(f.Actor,f.Source.Row.ContainerId,1,null,default);
        var row=Assert.Single(page.Items);Assert.Equal(f.Source.Row.Id,row.SourceIntentId);Assert.Equal(row.SourceIntentId,page.Next);Assert.Equal(f.Source.Row.ExecutionJobId,row.PurgeOperationId);Assert.Equal(f.Original.Row.ExecutionJobId,row.SourceOperationId);Assert.Equal(f.Original.Row.HistoryDigest,row.HistoryDigest);
        var text=System.Text.Json.JsonSerializer.Serialize(page);Assert.DoesNotContain(f.Source.Row.OwnerHash,text,StringComparison.Ordinal);Assert.DoesNotContain(f.Source.Row.LoginBinding,text,StringComparison.Ordinal);
    }
    [Theory][InlineData("ordinary")][InlineData("logout")][InlineData("revoked-after-read")]
    public async Task SourceCatalogDoesNotBypassCurrentAuthority(string reason)
    {
        using var f=new Fixture();var catalog=new Catalog([new(f.Source.Row,f.Original.Row)]);var service=new ContainerRecycleArchiveSourcesService(f.Service,catalog,f.Protector,f.Protector,f.Protector);
        if(reason=="ordinary")f.Identity.Admin=false;if(reason=="logout")f.Logins.Active=false;if(reason=="revoked-after-read")catalog.AfterRead=()=>f.Identity.LiveAdmin=false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>service.ListAsync(f.Actor,f.Source.Row.ContainerId,25,null,default));Assert.Equal(reason=="revoked-after-read"?1:0,catalog.Calls);
    }
    [Theory][InlineData("duplicate")][InlineData("mac")][InlineData("container")]
    public async Task CatalogCorruptionCannotBecomeSelectableSource(string reason)
    {
        using var f=new Fixture();if(reason=="mac")f.Source.Row.ReceiptHash=new('F',64);
        var catalog=new Catalog(reason=="duplicate"?[new(f.Source.Row,f.Original.Row),new(f.Source.Row,f.Original.Row)]:[new(f.Source.Row,f.Original.Row)]);var service=new ContainerRecycleArchiveSourcesService(f.Service,catalog,f.Protector,f.Protector,f.Protector);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>service.ListAsync(f.Actor,reason=="container"?new('b',64):f.Source.Row.ContainerId,25,null,default));
    }
}
