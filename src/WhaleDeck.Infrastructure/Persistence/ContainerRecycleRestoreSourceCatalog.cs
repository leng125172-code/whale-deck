using Microsoft.EntityFrameworkCore;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Infrastructure.Persistence;

public sealed class ContainerRecycleRestoreSourceCatalog(PlatformDbContext db) : IContainerRecycleRestoreSourceCatalog
{
    public async Task<IReadOnlyList<ContainerRecycleIntent>> ListAsync(string containerId,int take,Guid? after,CancellationToken cancellationToken)
    {
        if(take is < 2 or > 51)throw new ArgumentException("CONTAINER_RECYCLE_RESTORE_PAGE_INVALID");
        var query = db.ContainerRecycleIntents.AsNoTracking().Where(r=>r.ContainerId==containerId&&r.State=="Succeeded"&&r.RootState=="Trashed"&&r.CompletedAtUtc!=null&&r.RootStartedAtUtc!=null&&!r.PublishBarrier&&r.CancelRequestedAtUtc==null&&r.HistoryVerified&&r.HistoryAvailable==true&&r.BindingCurrent==true&&r.SourceRetained==true&&r.SourceRestored==false)
            .Where(r=>!db.ContainerRecycleRestoreIntents.Any(x=>x.SourceIntentId==r.Id&&(x.CompletedAtUtc==null||x.RootState=="Completed"&&x.RestoredByThisOperation==true)))
            .Where(r=>!db.ContainerRecyclePurgeIntents.Any(x=>x.SourceIntentId==r.Id&&(x.CompletedAtUtc==null||x.PurgeApplied==true||x.RootState=="Unknown")))
            .AsQueryable();
        if(after is {} cursor)query=query.Where(r=>r.Id.CompareTo(cursor)>0);
        return await query.OrderBy(r=>r.Id).Take(take).ToArrayAsync(cancellationToken);
    }
}
