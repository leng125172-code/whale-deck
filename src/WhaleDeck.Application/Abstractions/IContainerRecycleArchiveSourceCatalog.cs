using WhaleDeck.Domain.Entities;
namespace WhaleDeck.Application.Abstractions;

public sealed record ContainerRecycleArchiveSourceRows(ContainerRecyclePurgeIntent Purge,ContainerRecycleIntent Original);
public interface IContainerRecycleArchiveSourceCatalog
{
    Task<IReadOnlyList<ContainerRecycleArchiveSourceRows>> ListAsync(string containerId,int take,Guid? after,CancellationToken cancellationToken);
}
