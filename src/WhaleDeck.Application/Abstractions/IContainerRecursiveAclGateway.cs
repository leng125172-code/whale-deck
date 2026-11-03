using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Abstractions;

/// <summary>Trusted, typed Agent transport. Public writes require an authorized permanent business intent.</summary>
public interface IContainerRecursiveAclGateway
{
    Task RequireContainerRecursiveAclWorkflowAsync(CancellationToken cancellationToken);
    Task<ContainerRecursiveAclPlanDto> PlanContainerRecursiveAclAsync(TerminalActor actor, ContainerRecursiveAclCommandDto command, CancellationToken cancellationToken);
    Task<ContainerRecursiveAclReceiptDto> ApplyContainerRecursiveAclAsync(ContainerRecursiveAclOperationDto operation, CancellationToken cancellationToken);
    Task<ContainerRecursiveAclReceiptDto?> InspectContainerRecursiveAclAsync(ContainerRecursiveAclOperationDto operation, CancellationToken cancellationToken);
    Task<ContainerRecursiveAclReceiptDto> DiscardContainerRecursiveAclAsync(ContainerRecursiveAclOperationDto operation, CancellationToken cancellationToken);
}
