using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.UnitTests.Application;

public sealed partial class ContainerRecyclePurgeWorkflowTests
{
    private sealed class Catalog(IReadOnlyList<ContainerRecycleIntent> rows) : IContainerRecyclePurgeSourceCatalog
    {
        public int Calls { get; private set; }
        public Action? AfterRead { get; set; }
        public Task<IReadOnlyList<ContainerRecycleIntent>> ListAsync(string containerId,int take,Guid? after,CancellationToken cancellationToken)
        {Calls++;AfterRead?.Invoke();return Task.FromResult(rows);}
    }
    [Fact]
    public async Task SourcePageOnlyReturnsValidatedSummaryAndOpaqueCursor()
    {
        using var f=new Fixture();var second=MakeSource(f.Protector,f.Clock.Now);var catalog=new Catalog([f.Source.Row,second]);
        var service=new ContainerRecyclePurgeSourcesService(f.Service,catalog,f.Protector,f.Protector);var page=await service.ListAsync(f.Actor,f.Source.Row.ContainerId,1,null,default);
        var row=Assert.Single(page.Items);Assert.Equal(f.Source.Row.Id,row.SourceIntentId);Assert.Equal(row.SourceIntentId,page.Next);Assert.Equal(f.Source.Row.ExecutionJobId,row.SourceOperationId);Assert.Equal(f.Source.Row.HistoryDigest,row.HistoryDigest);
        var text=System.Text.Json.JsonSerializer.Serialize(page);Assert.DoesNotContain(f.Source.Row.OwnerHash,text,StringComparison.Ordinal);Assert.DoesNotContain(f.Source.Row.LoginBinding,text,StringComparison.Ordinal);
    }
    [Theory][InlineData("ordinary")][InlineData("logout")][InlineData("revoked-after-read")]
    public async Task SourceCatalogDoesNotBypassCurrentAuthority(string reason)
    {
        using var f=new Fixture();var catalog=new Catalog([f.Source.Row]);var service=new ContainerRecyclePurgeSourcesService(f.Service,catalog,f.Protector,f.Protector);
        if(reason=="ordinary")f.Identity.Admin=false;if(reason=="logout")f.Logins.Active=false;if(reason=="revoked-after-read")catalog.AfterRead=()=>f.Identity.LiveAdmin=false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>service.ListAsync(f.Actor,f.Source.Row.ContainerId,25,null,default));Assert.Equal(reason=="revoked-after-read"?1:0,catalog.Calls);
    }
    [Theory][InlineData("duplicate")][InlineData("mac")][InlineData("container")]
    public async Task CatalogCorruptionCannotBecomeSelectableSource(string reason)
    {
        using var f=new Fixture();if(reason=="mac")f.Source.Row.ReceiptHash=new('F',64);
        var catalog=new Catalog(reason=="duplicate"?[f.Source.Row,f.Source.Row]:[f.Source.Row]);var service=new ContainerRecyclePurgeSourcesService(f.Service,catalog,f.Protector,f.Protector);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>service.ListAsync(f.Actor,reason=="container"?new('b',64):f.Source.Row.ContainerId,25,null,default));
    }
}
