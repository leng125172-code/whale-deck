using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Abstractions;

public interface IContainerTerminalGateway
{
    Task<ContainerTerminalTargetDto> InspectContainerTerminalAsync(string containerId, CancellationToken cancellationToken);
    Task<IHostTerminalConnection> OpenContainerAsync(Guid id, TerminalActor actor, string containerId, string instanceId, string shell, CancellationToken cancellationToken);
}
