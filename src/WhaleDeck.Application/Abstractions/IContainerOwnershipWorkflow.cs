using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Abstractions;

public interface IContainerOwnershipProtector
{
    string FingerprintOwnershipRequest(ContainerOwnershipRequestBinding binding);
    string FingerprintOwnershipPlan(ContainerOwnershipPlanBinding binding);
    string FingerprintOwnershipExecution(ContainerOwnershipExecutionBinding binding);
    string SignOwnershipConfirmation(ContainerOwnershipConfirmation claims);
    ContainerOwnershipConfirmation VerifyOwnershipConfirmation(string token);
}

/// <summary>Atomic persistent transitions; implementations must share publication exclusion with all file writers.</summary>
public interface IContainerOwnershipRepository
{
    Task<ContainerOwnershipIntent?> OwnedAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken);
    Task<ContainerOwnershipIntent?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ContainerOwnershipIntent>> UnknownAsync(int take, CancellationToken cancellationToken);
    Task<ContainerOwnershipIntent> EnqueueAsync(ContainerOwnershipIntent row, CancellationToken cancellationToken);
    Task<ContainerOwnershipIntent> ConfirmAsync(TerminalActor actor, ContainerOwnershipConfirmation claims, long version, CancellationToken cancellationToken);
    Task CancelAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken);
    Task<ContainerOwnershipIntent?> ClaimAsync(DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> CancellationRequestedAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> MarkPlanningAsync(ContainerOwnershipIntent claimed, CancellationToken cancellationToken);
    Task FinishPlanningAsync(ContainerOwnershipIntent claimed, ContainerOwnershipPlanDto? plan, string code, CancellationToken cancellationToken);
    Task<ContainerFileUploadStartResult> StartedAsync(ContainerOwnershipIntent claimed, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> RenewAsync(ContainerOwnershipIntent claimed, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> FinishAsync(ContainerOwnershipIntent claimed, string state, string code, ContainerOwnershipReceiptDto? receipt, CancellationToken cancellationToken);
    Task DeferAsync(ContainerOwnershipIntent claimed, CancellationToken cancellationToken);
    Task AcknowledgeAsync(TerminalActor actor, Guid id, long version, ContainerOwnershipReceiptDto receipt, CancellationToken cancellationToken);
}
