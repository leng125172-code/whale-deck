using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Abstractions;

public interface IContainerRecycleGateway
{
    Task RequireContainerRecycleAsync(CancellationToken cancellationToken);
    Task<ContainerRecyclePlanDto> PlanContainerRecycleAsync(TerminalActor actor, ContainerRecycleCommandDto command, CancellationToken cancellationToken);
    Task<ContainerRecycleReceiptDto> ApplyContainerRecycleAsync(ContainerRecycleOperationDto operation, CancellationToken cancellationToken);
    Task<ContainerRecycleReceiptDto?> InspectContainerRecycleAsync(ContainerRecycleOperationDto operation, CancellationToken cancellationToken);
    Task<ContainerRecycleReceiptDto> DiscardContainerRecycleAsync(ContainerRecycleOperationDto operation, CancellationToken cancellationToken);
    Task<ContainerRecycleReceiptDto> RestoreContainerRecycleAsync(ContainerRecycleOperationDto operation, CancellationToken cancellationToken);
}
