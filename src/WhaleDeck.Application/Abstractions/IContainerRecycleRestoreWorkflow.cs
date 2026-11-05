using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Abstractions;

public interface IContainerRecycleRestoreProtector
{
    string FingerprintRecycleRestoreSource(ContainerRecycleRestoreSourceBinding binding);
    string FingerprintRecycleRestoreRequest(ContainerRecycleRestoreRequestBinding binding);
    string FingerprintRecycleRestorePlan(ContainerRecycleRestorePlanBinding binding);
    string FingerprintRecycleRestoreExecution(ContainerRecycleRestoreExecutionBinding binding);
    string FingerprintRecycleRestoreReceipt(ContainerRecycleRestoreReceiptBinding binding);
    string SignRecycleRestoreConfirmation(ContainerRecycleRestoreConfirmation claims);
    ContainerRecycleRestoreConfirmation VerifyRecycleRestoreConfirmation(string token);
}

/// <summary>
/// Atomic persistent transitions; implementations must share exclusion with ALL file writers,
/// including operations in other containers that resolve to the same host directory.
/// Enqueue/confirmation/job/outbox/audit are one transaction. Confirmation checks login,
/// version and expiry again under lock. Claim/Renew/Finish must fence the original lease.
/// Started persists the execution marker and barrier before RPC. Reclaimed executions never Apply again.
/// Unknown holds protection until explicit acknowledgement; finalization preserves receipt MAC/facts.
/// Claim must not return unexpired Ready, terminal or unresolved Unknown rows for normal dispatch.
/// SourceIntentId/SourceProofHash/complete command must be immutable. Confirmation and Started must recheck the locked eligible source.
/// Restoration must share exclusion with original source claims, recycle, all file writers and host aliases.
/// This interface alone provides no database protection; no in-memory implementation is registered.
/// </summary>
public interface IContainerRecycleRestoreRepository
{
    Task<ContainerRecycleRestoreIntent?> OwnedAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken);
    Task<ContainerRecycleRestoreIntent?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ContainerRecycleRestoreIntent>> UnresolvedAsync(int take, CancellationToken cancellationToken);
    Task<ContainerRecycleRestoreIntent> EnqueueAsync(ContainerRecycleRestoreIntent row, CancellationToken cancellationToken);
    Task<ContainerRecycleRestoreIntent> ConfirmAsync(TerminalActor actor, ContainerRecycleRestoreConfirmation claims, long version, CancellationToken cancellationToken);
    Task CancelAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken);
    Task<ContainerRecycleRestoreIntent?> ClaimAsync(DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> CancellationRequestedAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> MarkPlanningAsync(ContainerRecycleRestoreIntent claimed, CancellationToken cancellationToken);
    Task FinishPlanningAsync(ContainerRecycleRestoreIntent claimed, ContainerRecycleRestorePlanDto? plan, string code, CancellationToken cancellationToken);
    Task<ContainerFileUploadStartResult> StartedAsync(ContainerRecycleRestoreIntent claimed, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> RenewAsync(ContainerRecycleRestoreIntent claimed, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> FinishAsync(ContainerRecycleRestoreIntent claimed, string state, string code, ContainerRecycleRestoreReceiptDto? receipt, CancellationToken cancellationToken);
    Task DeferAsync(ContainerRecycleRestoreIntent claimed, CancellationToken cancellationToken);
    Task AcknowledgeAsync(TerminalActor actor, Guid id, long version, ContainerRecycleRestoreReceiptDto receipt, CancellationToken cancellationToken);
}
