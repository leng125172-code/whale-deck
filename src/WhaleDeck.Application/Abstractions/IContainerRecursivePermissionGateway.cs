using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Abstractions;

/// <summary>Trusted, typed Agent transport. Public writes require an authorized permanent business intent.</summary>
public interface IContainerRecursivePermissionGateway
{
    Task RequireContainerRecursivePermissionWorkflowAsync(CancellationToken cancellationToken);
    Task<ContainerRecursivePermissionPlanDto> PlanContainerRecursivePermissionAsync(TerminalActor actor, ContainerRecursivePermissionCommandDto command, CancellationToken cancellationToken);
    Task<ContainerRecursivePermissionReceiptDto> ApplyContainerRecursivePermissionAsync(ContainerRecursivePermissionOperationDto operation, CancellationToken cancellationToken);
    Task<ContainerRecursivePermissionReceiptDto?> InspectContainerRecursivePermissionAsync(ContainerRecursivePermissionOperationDto operation, CancellationToken cancellationToken);
    Task<ContainerRecursivePermissionReceiptDto> DiscardContainerRecursivePermissionAsync(ContainerRecursivePermissionOperationDto operation, CancellationToken cancellationToken);
}
