using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Services;

public sealed class ContainerRecycleRestoreSourcesService(ContainerRecycleRestoreWorkflowService authorization, IContainerRecycleRestoreSourceCatalog catalog,
    IContainerRecycleProtector sourceProtector, IContainerRecycleRestoreProtector protector)
{
    public async Task<ContainerRecycleRestoreSourcePage> ListAsync(TerminalActor actor, string containerId, int take, Guid? after, CancellationToken cancellationToken)
    {
        await authorization.AuthorizeAsync(actor,cancellationToken);ContainerFileValidation.Id(containerId);
        if(take is < 1 or > 50 || after==Guid.Empty)throw new ArgumentException("CONTAINER_RECYCLE_RESTORE_PAGE_INVALID");
        var rows=await catalog.ListAsync(containerId,take+1,after,cancellationToken);
        if(rows.Count>take+1||rows.Select(r=>r.Id).Distinct().Count()!=rows.Count)throw new InvalidOperationException("CONTAINER_RECYCLE_RESTORE_SOURCE_CHANGED");
        var result=rows.Select(row=>
        {
            var value=ContainerRecycleRestoreSourceResolver.FromRow(row,sourceProtector,protector);var p=value.Command.Source.Plan;
            if(p.Command.Location.ContainerId!=containerId)throw new InvalidOperationException("CONTAINER_RECYCLE_RESTORE_SOURCE_CHANGED");
            return new ContainerRecycleRestoreSourceSummary(row.Id,value.Command.Source.OperationId,p.Command.Location,p.SourceIdentity.OwnerUid,p.SourceIdentity.OwnerGid,p.SourceIdentity.Mode,
                p.SourceIdentity.SizeBytes,p.Sha256,p.Attributes.Count,value.Command.HistoryDigest,row.CompletedAtUtc!.Value);
        }).ToArray();
        await authorization.AuthorizeAsync(actor,cancellationToken);
        return new(result.Take(take).ToArray(),result.Length>take?result[take-1].SourceIntentId:null);
    }
}
