using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Abstractions;

public interface IContainerRecursiveAclRestoreProtector
{
    string FingerprintRecursiveAclRestoreRequest(ContainerRecursiveAclRestoreRequestBinding binding);
    string FingerprintRecursiveAclRestorePlan(ContainerRecursiveAclRestorePlanBinding binding);
    string FingerprintRecursiveAclRestoreExecution(ContainerRecursiveAclRestoreExecutionBinding binding);
    string FingerprintRecursiveAclRestoreReceipt(ContainerRecursiveAclRestoreReceiptBinding binding);
    string SignRecursiveAclRestoreConfirmation(ContainerRecursiveAclRestoreConfirmation claims);
    ContainerRecursiveAclRestoreConfirmation VerifyRecursiveAclRestoreConfirmation(string token);
}

/// <summary>Atomic persistent transitions; implementations must share publication exclusion with all file writers.</summary>
public interface IContainerRecursiveAclRestoreRepository
{
    Task<ContainerRecursiveAclRestoreIntent?> OwnedAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken);
    Task<ContainerRecursiveAclRestoreIntent?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ContainerRecursiveAclRestoreIntent>> UnresolvedAsync(int take, CancellationToken cancellationToken);
    Task<ContainerRecursiveAclRestoreIntent> EnqueueAsync(ContainerRecursiveAclRestoreIntent row, CancellationToken cancellationToken);
    Task<ContainerRecursiveAclRestoreIntent> ConfirmAsync(TerminalActor actor, ContainerRecursiveAclRestoreConfirmation claims, long version, CancellationToken cancellationToken);
    Task CancelAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken);
    Task<ContainerRecursiveAclRestoreIntent?> ClaimAsync(DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> CancellationRequestedAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> MarkPlanningAsync(ContainerRecursiveAclRestoreIntent claimed, CancellationToken cancellationToken);
    Task FinishPlanningAsync(ContainerRecursiveAclRestoreIntent claimed, ContainerRecursiveAclRestorePlanDto? plan, string code, CancellationToken cancellationToken);
    Task<ContainerFileUploadStartResult> StartedAsync(ContainerRecursiveAclRestoreIntent claimed, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> RenewAsync(ContainerRecursiveAclRestoreIntent claimed, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> FinishAsync(ContainerRecursiveAclRestoreIntent claimed, string state, string code, ContainerRecursiveAclRestoreReceiptDto? receipt, CancellationToken cancellationToken);
    Task DeferAsync(ContainerRecursiveAclRestoreIntent claimed, CancellationToken cancellationToken);
    Task AcknowledgeAsync(TerminalActor actor, Guid id, long version, ContainerRecursiveAclRestoreReceiptDto receipt, CancellationToken cancellationToken);
}
