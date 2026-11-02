using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Abstractions;

/// <summary>Trusted, typed Agent transport. Public writes require an authorized permanent business intent.</summary>
public interface IContainerAclGateway
{
    Task RequireContainerAclWorkflowAsync(CancellationToken cancellationToken);
    Task<ContainerAclPlanDto> PlanContainerAclAsync(TerminalActor actor, ContainerAclCommandDto command, CancellationToken cancellationToken);
    Task<ContainerAclReceiptDto> ApplyContainerAclAsync(ContainerAclOperationDto operation, CancellationToken cancellationToken);
    Task<ContainerAclReceiptDto?> InspectContainerAclAsync(ContainerAclOperationDto operation, CancellationToken cancellationToken);
    Task<ContainerAclReceiptDto> DiscardContainerAclAsync(ContainerAclOperationDto operation, CancellationToken cancellationToken);
}
