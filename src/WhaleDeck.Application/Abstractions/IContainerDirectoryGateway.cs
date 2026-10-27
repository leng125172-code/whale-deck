using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Abstractions;

/// <summary>Trusted, typed Agent transport. Public writes require an authorized permanent business intent.</summary>
public interface IContainerDirectoryGateway
{
    Task RequireContainerDirectoryWorkflowAsync(CancellationToken cancellationToken);
    Task<ContainerDirectoryPlanDto> PlanContainerDirectoryAsync(TerminalActor actor, ContainerDirectoryCommandDto command, CancellationToken cancellationToken);
    Task<ContainerDirectoryReceiptDto> ApplyContainerDirectoryAsync(ContainerDirectoryOperationDto operation, CancellationToken cancellationToken);
    Task<ContainerDirectoryReceiptDto?> InspectContainerDirectoryAsync(ContainerDirectoryOperationDto operation, CancellationToken cancellationToken);
    Task<ContainerDirectoryReceiptDto> DiscardContainerDirectoryAsync(ContainerDirectoryOperationDto operation, CancellationToken cancellationToken);
}
