using WhaleDeck.Domain.Entities;
namespace WhaleDeck.Application.Abstractions;

public interface IContainerRecycleRestoreSourceCatalog
{
    Task<IReadOnlyList<ContainerRecycleIntent>> ListAsync(string containerId, int take, Guid? after, CancellationToken cancellationToken);
}
