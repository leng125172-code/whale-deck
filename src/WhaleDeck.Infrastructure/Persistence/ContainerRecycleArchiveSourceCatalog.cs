using Microsoft.EntityFrameworkCore;
using WhaleDeck.Application.Abstractions;
namespace WhaleDeck.Infrastructure.Persistence;

public sealed class ContainerRecycleArchiveSourceCatalog(PlatformDbContext db):IContainerRecycleArchiveSourceCatalog
{
    public async Task<IReadOnlyList<ContainerRecycleArchiveSourceRows>> ListAsync(string containerId,int take,Guid? after,CancellationToken cancellationToken)
    {
        if(take is <2 or >51)throw new ArgumentException("CONTAINER_RECYCLE_ARCHIVE_PAGE_INVALID");
        var query=from p in db.ContainerRecyclePurgeIntents.AsNoTracking()
            join r in db.ContainerRecycleIntents.AsNoTracking() on p.SourceIntentId equals r.Id
            where p.ContainerId==containerId&&p.State=="Succeeded"&&p.CompletedAtUtc!=null&&!p.PublishBarrier&&p.RootStartedAtUtc!=null&&p.ApplyStartedAtUtc!=null&&p.ReservationJson!=null&&p.RootState=="Completed"&&p.PurgeApplied==true&&p.SourceFenced==true&&p.OwnedFence==true
                &&r.State=="Succeeded"&&r.CompletedAtUtc!=null&&!r.PublishBarrier&&r.CancelRequestedAtUtc==null&&r.RootState=="Trashed"&&r.HistoryVerified&&r.HistoryAvailable==true&&r.BindingCurrent==true&&r.SourceRetained==true&&r.SourceRestored==false
                &&!db.ContainerRecycleArchiveIntents.Any(x=>x.SourceIntentId==p.Id&&(x.CompletedAtUtc==null||x.RootState=="Unknown"||x.ArchiveCopied==true))
            select new { Purge=p,Original=r };
        if(after is {} cursor)query=query.Where(x=>x.Purge.Id.CompareTo(cursor)>0);
        var rows=await query.OrderBy(x=>x.Purge.Id).Take(take).ToArrayAsync(cancellationToken);
        return rows.Select(x=>new ContainerRecycleArchiveSourceRows(x.Purge,x.Original)).ToArray();
    }
}
