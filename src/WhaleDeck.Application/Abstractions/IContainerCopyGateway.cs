using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Abstractions;

/// <summary>Trusted Agent transport. Public writes require permanent authorized business intents.</summary>
public interface IContainerCopyGateway
{
    Task RequireContainerCopyWorkflowAsync(CancellationToken cancellationToken);
    Task<ContainerCopyPlanDto> PlanContainerCopyAsync(TerminalActor actor, ContainerCopyCommandDto command, CancellationToken cancellationToken);
    Task<ContainerCopyReceiptDto> ApplyContainerCopyAsync(ContainerCopyOperationDto operation, CancellationToken cancellationToken);
    Task<ContainerCopyReceiptDto?> InspectContainerCopyAsync(ContainerCopyOperationDto operation, CancellationToken cancellationToken);
    Task<ContainerCopyReceiptDto> DiscardContainerCopyAsync(ContainerCopyOperationDto operation, CancellationToken cancellationToken);
}
