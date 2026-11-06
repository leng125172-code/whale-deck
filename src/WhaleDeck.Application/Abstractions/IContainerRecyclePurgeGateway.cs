using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Abstractions;

public interface IContainerRecyclePurgeGateway
{
    Task RequireContainerRecyclePurgeAsync(CancellationToken cancellationToken);
    Task<ContainerRecyclePurgePlanDto> PlanContainerRecyclePurgeAsync(TerminalActor actor,ContainerRecyclePurgeCommandDto command,CancellationToken cancellationToken);
    Task<ContainerRecyclePurgeReceiptDto> ReserveContainerRecyclePurgeAsync(ContainerRecyclePurgeOperationDto operation,CancellationToken cancellationToken);
    Task<ContainerRecyclePurgeReceiptDto> ApplyContainerRecyclePurgeAsync(ContainerRecyclePurgeOperationDto operation,CancellationToken cancellationToken);
    Task<ContainerRecyclePurgeReceiptDto?> InspectContainerRecyclePurgeAsync(ContainerRecyclePurgeOperationDto operation,CancellationToken cancellationToken);
    Task<ContainerRecyclePurgeReceiptDto> DiscardContainerRecyclePurgeAsync(ContainerRecyclePurgeOperationDto operation,CancellationToken cancellationToken);
}
