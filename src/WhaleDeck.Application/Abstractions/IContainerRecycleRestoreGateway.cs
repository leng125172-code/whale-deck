using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Abstractions;

public interface IContainerRecycleRestoreGateway
{
    Task RequireContainerRecycleRestoreAsync(CancellationToken cancellationToken);
    Task<ContainerRecycleRestorePlanDto> PlanContainerRecycleRestoreAsync(TerminalActor actor, ContainerRecycleRestoreCommandDto command, CancellationToken cancellationToken);
    Task<ContainerRecycleRestoreReceiptDto> ApplyContainerRecycleRestoreAsync(ContainerRecycleRestoreOperationDto operation, CancellationToken cancellationToken);
    Task<ContainerRecycleRestoreReceiptDto?> InspectContainerRecycleRestoreAsync(ContainerRecycleRestoreOperationDto operation, CancellationToken cancellationToken);
    Task<ContainerRecycleRestoreReceiptDto> DiscardContainerRecycleRestoreAsync(ContainerRecycleRestoreOperationDto operation, CancellationToken cancellationToken);
}
