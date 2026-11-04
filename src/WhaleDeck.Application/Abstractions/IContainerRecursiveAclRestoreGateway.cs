using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Abstractions;

/// <summary>Trusted, typed Agent transport. Public writes require an authorized permanent business intent.</summary>
public interface IContainerRecursiveAclRestoreGateway
{
    Task RequireContainerRecursiveAclRestoreWorkflowAsync(CancellationToken cancellationToken);
    Task<ContainerRecursiveAclRestorePlanDto> PlanContainerRecursiveAclRestoreAsync(TerminalActor actor, ContainerRecursiveAclRestoreCommandDto command, CancellationToken cancellationToken);
    Task<ContainerRecursiveAclRestoreReceiptDto> ApplyContainerRecursiveAclRestoreAsync(ContainerRecursiveAclRestoreOperationDto operation, CancellationToken cancellationToken);
    Task<ContainerRecursiveAclRestoreReceiptDto?> InspectContainerRecursiveAclRestoreAsync(ContainerRecursiveAclRestoreOperationDto operation, CancellationToken cancellationToken);
    Task<ContainerRecursiveAclRestoreReceiptDto> DiscardContainerRecursiveAclRestoreAsync(ContainerRecursiveAclRestoreOperationDto operation, CancellationToken cancellationToken);
}
