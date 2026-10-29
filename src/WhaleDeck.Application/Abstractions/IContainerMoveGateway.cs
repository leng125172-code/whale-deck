using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Abstractions;

/// <summary>Trusted Agent transport. Public writes require permanent authorized business intents.</summary>
public interface IContainerMoveGateway
{
    Task RequireContainerMoveWorkflowAsync(CancellationToken cancellationToken);
    Task<ContainerMovePlanDto> PlanContainerMoveAsync(TerminalActor actor, ContainerMoveCommandDto command, CancellationToken cancellationToken);
    Task<ContainerMoveReceiptDto> ApplyContainerMoveAsync(ContainerMoveOperationDto operation, CancellationToken cancellationToken);
    Task<ContainerMoveReceiptDto?> InspectContainerMoveAsync(ContainerMoveOperationDto operation, CancellationToken cancellationToken);
    Task<ContainerMoveReceiptDto> DiscardContainerMoveAsync(ContainerMoveOperationDto operation, CancellationToken cancellationToken);
}
