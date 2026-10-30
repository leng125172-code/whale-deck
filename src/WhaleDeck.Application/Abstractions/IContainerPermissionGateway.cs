using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Abstractions;

/// <summary>Trusted, typed Agent transport. Public writes require an authorized permanent business intent.</summary>
public interface IContainerPermissionGateway
{
    Task RequireContainerPermissionWorkflowAsync(CancellationToken cancellationToken);
    Task<ContainerPermissionPlanDto> PlanContainerPermissionAsync(TerminalActor actor, ContainerPermissionCommandDto command, CancellationToken cancellationToken);
    Task<ContainerPermissionReceiptDto> ApplyContainerPermissionAsync(ContainerPermissionOperationDto operation, CancellationToken cancellationToken);
    Task<ContainerPermissionReceiptDto?> InspectContainerPermissionAsync(ContainerPermissionOperationDto operation, CancellationToken cancellationToken);
    Task<ContainerPermissionReceiptDto> DiscardContainerPermissionAsync(ContainerPermissionOperationDto operation, CancellationToken cancellationToken);
}
