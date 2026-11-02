using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Abstractions;

public interface IContainerRecursivePermissionProtector
{
    string FingerprintRecursivePermissionRequest(ContainerRecursivePermissionRequestBinding binding);
    string FingerprintRecursivePermissionPlan(ContainerRecursivePermissionPlanBinding binding);
    string FingerprintRecursivePermissionExecution(ContainerRecursivePermissionExecutionBinding binding);
    string SignRecursivePermissionConfirmation(ContainerRecursivePermissionConfirmation claims);
    ContainerRecursivePermissionConfirmation VerifyRecursivePermissionConfirmation(string token);
}

/// <summary>Atomic persistent transitions; implementations must share publication exclusion with all file writers.</summary>
public interface IContainerRecursivePermissionRepository
{
    Task<ContainerRecursivePermissionIntent?> OwnedAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken);
    Task<ContainerRecursivePermissionIntent?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ContainerRecursivePermissionIntent>> UnresolvedAsync(int take, CancellationToken cancellationToken);
    Task<ContainerRecursivePermissionIntent> EnqueueAsync(ContainerRecursivePermissionIntent row, CancellationToken cancellationToken);
    Task<ContainerRecursivePermissionIntent> ConfirmAsync(TerminalActor actor, ContainerRecursivePermissionConfirmation claims, long version, CancellationToken cancellationToken);
    Task CancelAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken);
    Task<ContainerRecursivePermissionIntent?> ClaimAsync(DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> CancellationRequestedAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> MarkPlanningAsync(ContainerRecursivePermissionIntent claimed, CancellationToken cancellationToken);
    Task FinishPlanningAsync(ContainerRecursivePermissionIntent claimed, ContainerRecursivePermissionPlanDto? plan, string code, CancellationToken cancellationToken);
    Task<ContainerFileUploadStartResult> StartedAsync(ContainerRecursivePermissionIntent claimed, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> RenewAsync(ContainerRecursivePermissionIntent claimed, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> FinishAsync(ContainerRecursivePermissionIntent claimed, string state, string code, ContainerRecursivePermissionReceiptDto? receipt, CancellationToken cancellationToken);
    Task DeferAsync(ContainerRecursivePermissionIntent claimed, CancellationToken cancellationToken);
    Task AcknowledgeAsync(TerminalActor actor, Guid id, long version, ContainerRecursivePermissionReceiptDto receipt, CancellationToken cancellationToken);
}
