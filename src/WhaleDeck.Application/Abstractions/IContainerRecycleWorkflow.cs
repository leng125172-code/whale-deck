using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Abstractions;

public interface IContainerRecycleProtector
{
    string FingerprintRecycleRequest(ContainerRecycleRequestBinding binding);
    string FingerprintRecyclePlan(ContainerRecyclePlanBinding binding);
    string FingerprintRecycleExecution(ContainerRecycleExecutionBinding binding);
    string FingerprintRecycleReceipt(ContainerRecycleReceiptBinding binding);
    string SignRecycleConfirmation(ContainerRecycleConfirmation claims);
    ContainerRecycleConfirmation VerifyRecycleConfirmation(string token);
}

/// <summary>
/// Atomic persistent transitions; implementations must share exclusion with ALL file writers,
/// including operations in other containers that resolve to the same host directory.
/// Enqueue/confirmation/job/outbox/audit are one transaction. Confirmation checks login,
/// version and expiry again under lock. Claim/Renew/Finish must fence the original lease.
/// Started persists the execution marker and barrier before RPC. Reclaimed executions never Apply again.
/// Unknown holds protection until explicit acknowledgement; finalization preserves receipt MAC/facts.
/// Claim must not return unexpired Ready, terminal or unresolved Unknown rows for normal dispatch.
/// This interface alone provides no database protection; no in-memory implementation is registered.
/// </summary>
public interface IContainerRecycleRepository
{
    Task<ContainerRecycleIntent?> OwnedAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken);
    Task<ContainerRecycleIntent?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ContainerRecycleIntent>> UnresolvedAsync(int take, CancellationToken cancellationToken);
    Task<ContainerRecycleIntent> EnqueueAsync(ContainerRecycleIntent row, CancellationToken cancellationToken);
    Task<ContainerRecycleIntent> ConfirmAsync(TerminalActor actor, ContainerRecycleConfirmation claims, long version, CancellationToken cancellationToken);
    Task CancelAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken);
    Task<ContainerRecycleIntent?> ClaimAsync(DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> CancellationRequestedAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> MarkPlanningAsync(ContainerRecycleIntent claimed, CancellationToken cancellationToken);
    Task FinishPlanningAsync(ContainerRecycleIntent claimed, ContainerRecyclePlanDto? plan, string code, CancellationToken cancellationToken);
    Task<ContainerFileUploadStartResult> StartedAsync(ContainerRecycleIntent claimed, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> RenewAsync(ContainerRecycleIntent claimed, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> FinishAsync(ContainerRecycleIntent claimed, string state, string code, ContainerRecycleReceiptDto? receipt, CancellationToken cancellationToken);
    Task DeferAsync(ContainerRecycleIntent claimed, CancellationToken cancellationToken);
    Task AcknowledgeAsync(TerminalActor actor, Guid id, long version, ContainerRecycleReceiptDto receipt, CancellationToken cancellationToken);
}
