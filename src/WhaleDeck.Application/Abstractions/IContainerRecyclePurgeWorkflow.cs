using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Abstractions;

public interface IContainerRecyclePurgeProtector
{
    string FingerprintRecyclePurgeSource(ContainerRecyclePurgeSourceBinding binding);
    string FingerprintRecyclePurgeRequest(ContainerRecyclePurgeRequestBinding binding);
    string FingerprintRecyclePurgePlan(ContainerRecyclePurgePlanBinding binding);
    string FingerprintRecyclePurgeExecution(ContainerRecyclePurgeExecutionBinding binding);
    string FingerprintRecyclePurgeReceipt(ContainerRecyclePurgeReceiptBinding binding);
    string SignRecyclePurgeConfirmation(ContainerRecyclePurgeConfirmation claims);
    ContainerRecyclePurgeConfirmation VerifyRecyclePurgeConfirmation(string token);
}

/// <summary>
/// Atomic persistent transitions; implementations must share exclusion with ALL file writers,
/// including restore/purge/source operations and other containers resolving to the same host directory.
/// Enqueue/confirmation/job/outbox/audit are one transaction. Confirmation checks login,
/// version and expiry again under lock. Claim/Renew/Finish must fence the original lease.
/// Started persists reservation entry and barrier before any Root RPC. Applying persists a separate irreversible entry only after the Reserved proof is stored. Reclaimed executions never Reserve/Apply again.
/// Unknown holds protection until explicit acknowledgement; acknowledgement atomically records its terminal completion time, original execution proof and current administrator audit before releasing the business barrier. Finalization preserves receipt MAC/facts.
/// Claim must not return unexpired Ready, terminal or unresolved Unknown rows for normal dispatch.
/// SourceIntentId/SourceProofHash/complete command must be immutable. A prior completed purge permanently disqualifies this source; source selection is not authority to reuse an already consumed inode. Confirmation and Started must recheck the locked eligible source.
/// Purge must share exclusion with original source claims, recycle, all file writers and host aliases.
/// This interface alone provides no database protection; no in-memory implementation is registered.
/// </summary>
public interface IContainerRecyclePurgeRepository
{
    Task<ContainerRecyclePurgeIntent?> OwnedAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken);
    Task<ContainerRecyclePurgeIntent?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ContainerRecyclePurgeIntent>> UnresolvedAsync(int take, CancellationToken cancellationToken);
    Task<ContainerRecyclePurgeIntent> EnqueueAsync(ContainerRecyclePurgeIntent row, CancellationToken cancellationToken);
    Task<ContainerRecyclePurgeIntent> ConfirmAsync(TerminalActor actor, ContainerRecyclePurgeConfirmation claims, long version, CancellationToken cancellationToken);
    Task CancelAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken);
    Task<ContainerRecyclePurgeIntent?> ClaimAsync(DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> CancellationRequestedAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> MarkPlanningAsync(ContainerRecyclePurgeIntent claimed, CancellationToken cancellationToken);
    Task FinishPlanningAsync(ContainerRecyclePurgeIntent claimed, ContainerRecyclePurgePlanDto? plan, string code, CancellationToken cancellationToken);
    Task<ContainerFileUploadStartResult> StartedAsync(ContainerRecyclePurgeIntent claimed, DateTimeOffset now, CancellationToken cancellationToken);
    /// <summary>Store the Reserved receipt/MAC under the original lease and barrier; never finalize or unlock.</summary>
    Task<bool> ReservedAsync(ContainerRecyclePurgeIntent claimed, ContainerRecyclePurgeReceiptDto receipt, CancellationToken cancellationToken);
    /// <summary>Atomically recheck locked source, current lease/cancel/expiry, immutable binding and Reserved proof, then persist ApplyStartedAt before delete RPC.</summary>
    Task<ContainerFileUploadStartResult> ApplyingAsync(ContainerRecyclePurgeIntent claimed, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> RenewAsync(ContainerRecyclePurgeIntent claimed, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> FinishAsync(ContainerRecyclePurgeIntent claimed, string state, string code, ContainerRecyclePurgeReceiptDto? receipt, CancellationToken cancellationToken);
    Task DeferAsync(ContainerRecyclePurgeIntent claimed, CancellationToken cancellationToken);
    Task AcknowledgeAsync(TerminalActor actor, Guid id, long version, ContainerRecyclePurgeReceiptDto receipt, CancellationToken cancellationToken);
}
