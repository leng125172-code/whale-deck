using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Abstractions;

/// <summary>Trusted, typed Agent transport. Public writes require an authorized permanent business intent.</summary>
public interface IContainerOwnershipGateway
{
    Task RequireContainerOwnershipWorkflowAsync(CancellationToken cancellationToken);
    Task<ContainerOwnershipPlanDto> PlanContainerOwnershipAsync(TerminalActor actor, ContainerOwnershipCommandDto command, CancellationToken cancellationToken);
    Task<ContainerOwnershipReceiptDto> ApplyContainerOwnershipAsync(ContainerOwnershipOperationDto operation, CancellationToken cancellationToken);
    Task<ContainerOwnershipReceiptDto?> InspectContainerOwnershipAsync(ContainerOwnershipOperationDto operation, CancellationToken cancellationToken);
    Task<ContainerOwnershipReceiptDto> DiscardContainerOwnershipAsync(ContainerOwnershipOperationDto operation, CancellationToken cancellationToken);
}
