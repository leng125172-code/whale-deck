using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Abstractions;

public interface IContainerRecursiveAclProtector
{
    string FingerprintRecursiveAclRequest(ContainerRecursiveAclRequestBinding binding);
    string FingerprintRecursiveAclPlan(ContainerRecursiveAclPlanBinding binding);
    string FingerprintRecursiveAclExecution(ContainerRecursiveAclExecutionBinding binding);
    string FingerprintRecursiveAclReceipt(ContainerRecursiveAclReceiptBinding binding);
    string SignRecursiveAclConfirmation(ContainerRecursiveAclConfirmation claims);
    ContainerRecursiveAclConfirmation VerifyRecursiveAclConfirmation(string token);
}

/// <summary>Atomic persistent transitions; implementations must share publication exclusion with all file writers.</summary>
public interface IContainerRecursiveAclRepository
{
    Task<ContainerRecursiveAclIntent?> OwnedAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken);
    Task<ContainerRecursiveAclIntent?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ContainerRecursiveAclIntent>> UnresolvedAsync(int take, CancellationToken cancellationToken);
    Task<ContainerRecursiveAclIntent> EnqueueAsync(ContainerRecursiveAclIntent row, CancellationToken cancellationToken);
    Task<ContainerRecursiveAclIntent> ConfirmAsync(TerminalActor actor, ContainerRecursiveAclConfirmation claims, long version, CancellationToken cancellationToken);
    Task CancelAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken);
    Task<ContainerRecursiveAclIntent?> ClaimAsync(DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> CancellationRequestedAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> MarkPlanningAsync(ContainerRecursiveAclIntent claimed, CancellationToken cancellationToken);
    Task FinishPlanningAsync(ContainerRecursiveAclIntent claimed, ContainerRecursiveAclPlanDto? plan, string code, CancellationToken cancellationToken);
    Task<ContainerFileUploadStartResult> StartedAsync(ContainerRecursiveAclIntent claimed, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> RenewAsync(ContainerRecursiveAclIntent claimed, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> FinishAsync(ContainerRecursiveAclIntent claimed, string state, string code, ContainerRecursiveAclReceiptDto? receipt, CancellationToken cancellationToken);
    Task DeferAsync(ContainerRecursiveAclIntent claimed, CancellationToken cancellationToken);
    Task AcknowledgeAsync(TerminalActor actor, Guid id, long version, ContainerRecursiveAclReceiptDto receipt, CancellationToken cancellationToken);
}
