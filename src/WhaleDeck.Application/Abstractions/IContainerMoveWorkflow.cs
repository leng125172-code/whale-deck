using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Abstractions;

public interface IContainerMoveProtector
{
    string FingerprintContainerMoveRequest(ContainerMoveRequestBinding binding);
    string FingerprintContainerMovePlan(ContainerMovePlanBinding binding);
    string FingerprintContainerMoveExecution(ContainerMoveExecutionBinding binding);
    string SignContainerMoveConfirmation(ContainerMoveConfirmation claims);
    ContainerMoveConfirmation VerifyContainerMoveConfirmation(string token);
}

/// <summary>Atomic persistent transitions; implementations must share publication exclusion with all file writers.</summary>
public interface IContainerMoveRepository
{
    Task<ContainerMoveIntent?> OwnedAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken);
    Task<ContainerMoveIntent?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ContainerMoveIntent>> UnknownAsync(int take, CancellationToken cancellationToken);
    Task<ContainerMoveIntent> EnqueueAsync(ContainerMoveIntent row, CancellationToken cancellationToken);
    Task<ContainerMoveIntent> ConfirmAsync(TerminalActor actor, ContainerMoveConfirmation claims, long version, CancellationToken cancellationToken);
    Task CancelAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken);
    Task<ContainerMoveIntent?> ClaimAsync(DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> CancellationRequestedAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> MarkPlanningAsync(ContainerMoveIntent claimed, CancellationToken cancellationToken);
    Task FinishPlanningAsync(ContainerMoveIntent claimed, ContainerMovePlanDto? plan, string code, CancellationToken cancellationToken);
    Task<ContainerFileUploadStartResult> StartedAsync(ContainerMoveIntent claimed, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> RenewAsync(ContainerMoveIntent claimed, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> FinishAsync(ContainerMoveIntent claimed, string state, string code, ContainerMoveReceiptDto? receipt, CancellationToken cancellationToken);
    Task DeferAsync(ContainerMoveIntent claimed, CancellationToken cancellationToken);
    Task AcknowledgeAsync(TerminalActor actor, Guid id, long version, ContainerMoveReceiptDto receipt, CancellationToken cancellationToken);
}
