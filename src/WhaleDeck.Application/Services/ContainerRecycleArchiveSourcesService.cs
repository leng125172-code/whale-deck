using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
namespace WhaleDeck.Application.Services;

public sealed class ContainerRecycleArchiveSourcesService(ContainerRecycleArchiveWorkflowService authorization,IContainerRecycleArchiveSourceCatalog catalog,
    IContainerRecycleProtector originalProtector,IContainerRecyclePurgeProtector purgeProtector,IContainerRecycleArchiveProtector protector)
{
    public async Task<ContainerRecycleArchiveSourcePage> ListAsync(TerminalActor actor,string containerId,int take,Guid? after,CancellationToken token)
    {
        await authorization.AuthorizeAsync(actor,token);ContainerFileValidation.Id(containerId);
        if(take is <1 or >50||after==Guid.Empty)throw new ArgumentException("CONTAINER_RECYCLE_ARCHIVE_PAGE_INVALID");
        var rows=await catalog.ListAsync(containerId,take+1,after,token);
        if(rows.Count>take+1||rows.Select(r=>r.Purge.Id).Distinct().Count()!=rows.Count)throw new InvalidOperationException("CONTAINER_RECYCLE_ARCHIVE_SOURCE_CHANGED");
        var items=rows.Select(row=>
        {
            var value=ContainerRecycleArchiveSourceResolver.FromRows(row.Purge,row.Original,purgeProtector,originalProtector,protector);
            var source=value.Command.Purge.Plan.Command.Source;var p=source.Plan;
            if(p.Command.Location.ContainerId!=containerId)throw new InvalidOperationException("CONTAINER_RECYCLE_ARCHIVE_SOURCE_CHANGED");
            return new ContainerRecycleArchiveSourceSummary(row.Purge.Id,value.Command.Purge.OperationId,source.OperationId,p.Command.Location,p.SourceIdentity.OwnerUid,p.SourceIdentity.OwnerGid,p.SourceIdentity.Mode,
                p.SourceIdentity.SizeBytes,p.Sha256,p.Attributes.Count,value.Command.HistoryDigest,row.Purge.CompletedAtUtc!.Value);
        }).ToArray();
        await authorization.AuthorizeAsync(actor,token);return new(items.Take(take).ToArray(),items.Length>take?items[take-1].SourceIntentId:null);
    }
}
