using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Abstractions;

public interface IContainerRecycleArchiveProtector
{
    string FingerprintRecycleArchiveSource(ContainerRecycleArchiveSourceBinding binding);
    string FingerprintRecycleArchiveRequest(ContainerRecycleArchiveRequestBinding binding);
    string FingerprintRecycleArchivePlan(ContainerRecycleArchivePlanBinding binding);
    string FingerprintRecycleArchiveExecution(ContainerRecycleArchiveExecutionBinding binding);
    string FingerprintRecycleArchiveReceipt(ContainerRecycleArchiveReceiptBinding binding);
    string SignRecycleArchiveConfirmation(ContainerRecycleArchiveConfirmation claims);
    ContainerRecycleArchiveConfirmation VerifyRecycleArchiveConfirmation(string token);
}

/// <summary>
/// Atomic persistence contract only; no implementation or in-memory repository is registered.
/// Lock the immutable completed purge AND original recycle source, verify both source MACs,
/// and share exclusion with every file writer and cross-container host-directory alias.
/// Enqueue/confirm/job/outbox/audit must be transactional. Confirmation, Started and Applying
/// recheck source eligibility, lease, cancel/expiry, current binding and source-copy claims.
/// Claim excludes unexpired Ready, terminal and unresolved Unknown rows from normal dispatch.
/// Started commits the reservation entry/barrier; Reserved stores its proof; Applying separately
/// commits the copy entry. A reclaimed operation only inspects/discards its original ID.
/// Unknown retains protection until explicit versioned administrator acknowledgement commits
/// original proof, audit and completion time. Preserve copied/new-inode facts, including when
/// current binding later becomes false. A prior successful copy must not be offered as a fresh
/// source until a separately defined retention/claim release policy explicitly allows it.
/// </summary>
public interface IContainerRecycleArchiveRepository
{
    Task<ContainerRecycleArchiveIntent?> OwnedAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken);
    Task<ContainerRecycleArchiveIntent?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ContainerRecycleArchiveIntent>> UnresolvedAsync(int take, CancellationToken cancellationToken);
    Task<ContainerRecycleArchiveIntent> EnqueueAsync(ContainerRecycleArchiveIntent row, CancellationToken cancellationToken);
    Task<ContainerRecycleArchiveIntent> ConfirmAsync(TerminalActor actor, ContainerRecycleArchiveConfirmation claims, long version, CancellationToken cancellationToken);
    Task CancelAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken);
    Task<ContainerRecycleArchiveIntent?> ClaimAsync(DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> CancellationRequestedAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> MarkPlanningAsync(ContainerRecycleArchiveIntent claimed, CancellationToken cancellationToken);
    Task FinishPlanningAsync(ContainerRecycleArchiveIntent claimed, ContainerRecycleArchivePlanDto? plan, string code, CancellationToken cancellationToken);
    Task<ContainerFileUploadStartResult> StartedAsync(ContainerRecycleArchiveIntent claimed, DateTimeOffset now, CancellationToken cancellationToken);
    /// <summary>Store the Reserved receipt/MAC under the original lease and barrier; never finalize or unlock.</summary>
    Task<bool> ReservedAsync(ContainerRecycleArchiveIntent claimed, ContainerRecycleArchiveReceiptDto receipt, CancellationToken cancellationToken);
    /// <summary>Atomically recheck locked source, current lease/cancel/expiry, immutable binding and Reserved proof, then persist ApplyStartedAt before copy RPC.</summary>
    Task<ContainerFileUploadStartResult> ApplyingAsync(ContainerRecycleArchiveIntent claimed, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> RenewAsync(ContainerRecycleArchiveIntent claimed, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> FinishAsync(ContainerRecycleArchiveIntent claimed, string state, string code, ContainerRecycleArchiveReceiptDto? receipt, CancellationToken cancellationToken);
    Task DeferAsync(ContainerRecycleArchiveIntent claimed, CancellationToken cancellationToken);
    Task AcknowledgeAsync(TerminalActor actor, Guid id, long version, ContainerRecycleArchiveReceiptDto receipt, CancellationToken cancellationToken);
}
