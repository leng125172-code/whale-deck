using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Abstractions;

public interface IContainerRecursiveOwnershipProtector
{
    string FingerprintRecursiveOwnershipRequest(ContainerRecursiveOwnershipRequestBinding binding);
    string FingerprintRecursiveOwnershipPlan(ContainerRecursiveOwnershipPlanBinding binding);
    string FingerprintRecursiveOwnershipExecution(ContainerRecursiveOwnershipExecutionBinding binding);
    string SignRecursiveOwnershipConfirmation(ContainerRecursiveOwnershipConfirmation claims);
    ContainerRecursiveOwnershipConfirmation VerifyRecursiveOwnershipConfirmation(string token);
}

/// <summary>Atomic persistent transitions; implementations must share publication exclusion with all file writers.</summary>
public interface IContainerRecursiveOwnershipRepository
{
    Task<ContainerRecursiveOwnershipIntent?> OwnedAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken);
    Task<ContainerRecursiveOwnershipIntent?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ContainerRecursiveOwnershipIntent>> UnresolvedAsync(int take, CancellationToken cancellationToken);
    Task<ContainerRecursiveOwnershipIntent> EnqueueAsync(ContainerRecursiveOwnershipIntent row, CancellationToken cancellationToken);
    Task<ContainerRecursiveOwnershipIntent> ConfirmAsync(TerminalActor actor, ContainerRecursiveOwnershipConfirmation claims, long version, CancellationToken cancellationToken);
    Task CancelAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken);
    Task<ContainerRecursiveOwnershipIntent?> ClaimAsync(DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> CancellationRequestedAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> MarkPlanningAsync(ContainerRecursiveOwnershipIntent claimed, CancellationToken cancellationToken);
    Task FinishPlanningAsync(ContainerRecursiveOwnershipIntent claimed, ContainerRecursiveOwnershipPlanDto? plan, string code, CancellationToken cancellationToken);
    Task<ContainerFileUploadStartResult> StartedAsync(ContainerRecursiveOwnershipIntent claimed, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> RenewAsync(ContainerRecursiveOwnershipIntent claimed, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> FinishAsync(ContainerRecursiveOwnershipIntent claimed, string state, string code, ContainerRecursiveOwnershipReceiptDto? receipt, CancellationToken cancellationToken);
    Task DeferAsync(ContainerRecursiveOwnershipIntent claimed, CancellationToken cancellationToken);
    Task AcknowledgeAsync(TerminalActor actor, Guid id, long version, ContainerRecursiveOwnershipReceiptDto receipt, CancellationToken cancellationToken);
}
