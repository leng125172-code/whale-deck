using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Abstractions;

/// <summary>Trusted, typed Agent transport. Public writes require an authorized permanent business intent.</summary>
public interface IContainerRecursiveOwnershipGateway
{
    Task RequireContainerRecursiveOwnershipWorkflowAsync(CancellationToken cancellationToken);
    Task<ContainerRecursiveOwnershipPlanDto> PlanContainerRecursiveOwnershipAsync(TerminalActor actor, ContainerRecursiveOwnershipCommandDto command, CancellationToken cancellationToken);
    Task<ContainerRecursiveOwnershipReceiptDto> ApplyContainerRecursiveOwnershipAsync(ContainerRecursiveOwnershipOperationDto operation, CancellationToken cancellationToken);
    Task<ContainerRecursiveOwnershipReceiptDto?> InspectContainerRecursiveOwnershipAsync(ContainerRecursiveOwnershipOperationDto operation, CancellationToken cancellationToken);
    Task<ContainerRecursiveOwnershipReceiptDto> DiscardContainerRecursiveOwnershipAsync(ContainerRecursiveOwnershipOperationDto operation, CancellationToken cancellationToken);
}
