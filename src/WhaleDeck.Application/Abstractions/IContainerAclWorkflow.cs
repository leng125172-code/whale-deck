using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Abstractions;

public interface IContainerAclProtector
{
    string FingerprintAclRequest(ContainerAclRequestBinding binding);
    string FingerprintAclPlan(ContainerAclPlanBinding binding);
    string FingerprintAclExecution(ContainerAclExecutionBinding binding);
    string FingerprintAclReceipt(ContainerAclReceiptBinding binding);
    string SignAclConfirmation(ContainerAclConfirmation claims);
    ContainerAclConfirmation VerifyAclConfirmation(string token);
}

/// <summary>
/// Atomic persistent transitions. Implementations must share publication exclusion with every file writer,
/// enforce original-login ownership, version/lease CAS and natural expiry, and validate all business HMACs.
/// Unknown retains protection. Finish stores the exact validated receipt plus its independent MAC and
/// scalar facts atomically. Acknowledge only fences the original execution and preserves prior Root facts;
/// it must not erase history, assert success, restore ACL or create another execution.
/// </summary>
public interface IContainerAclRepository
{
    Task<ContainerAclIntent?> OwnedAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken);
    Task<ContainerAclIntent?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ContainerAclIntent>> UnknownAsync(int take, CancellationToken cancellationToken);
    Task<ContainerAclIntent> EnqueueAsync(ContainerAclIntent row, CancellationToken cancellationToken);
    Task<ContainerAclIntent> ConfirmAsync(TerminalActor actor, ContainerAclConfirmation claims, long version, CancellationToken cancellationToken);
    Task CancelAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken);
    Task<ContainerAclIntent?> ClaimAsync(DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> CancellationRequestedAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> MarkPlanningAsync(ContainerAclIntent claimed, CancellationToken cancellationToken);
    Task FinishPlanningAsync(ContainerAclIntent claimed, ContainerAclPlanDto? plan, string code, CancellationToken cancellationToken);
    Task<ContainerFileUploadStartResult> StartedAsync(ContainerAclIntent claimed, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> RenewAsync(ContainerAclIntent claimed, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> FinishAsync(ContainerAclIntent claimed, string state, string code, ContainerAclReceiptDto? receipt, CancellationToken cancellationToken);
    Task DeferAsync(ContainerAclIntent claimed, CancellationToken cancellationToken);
    Task AcknowledgeAsync(TerminalActor actor, Guid id, long version, ContainerAclReceiptDto receipt, CancellationToken cancellationToken);
}
