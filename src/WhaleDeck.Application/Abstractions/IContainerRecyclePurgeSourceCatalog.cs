using WhaleDeck.Domain.Entities;
namespace WhaleDeck.Application.Abstractions;

public interface IContainerRecyclePurgeSourceCatalog
{
    Task<IReadOnlyList<ContainerRecycleIntent>> ListAsync(string containerId, int take, Guid? after, CancellationToken cancellationToken);
}
