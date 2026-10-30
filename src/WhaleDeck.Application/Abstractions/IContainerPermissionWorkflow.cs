using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Abstractions;

public interface IContainerPermissionProtector
{
    string FingerprintPermissionRequest(ContainerPermissionRequestBinding binding);
    string FingerprintPermissionPlan(ContainerPermissionPlanBinding binding);
    string FingerprintPermissionExecution(ContainerPermissionExecutionBinding binding);
    string SignPermissionConfirmation(ContainerPermissionConfirmation claims);
    ContainerPermissionConfirmation VerifyPermissionConfirmation(string token);
}

/// <summary>Atomic persistent transitions; implementations must share publication exclusion with all file writers.</summary>
public interface IContainerPermissionRepository
{
    Task<ContainerPermissionIntent?> OwnedAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken);
    Task<ContainerPermissionIntent?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ContainerPermissionIntent>> UnknownAsync(int take, CancellationToken cancellationToken);
    Task<ContainerPermissionIntent> EnqueueAsync(ContainerPermissionIntent row, CancellationToken cancellationToken);
    Task<ContainerPermissionIntent> ConfirmAsync(TerminalActor actor, ContainerPermissionConfirmation claims, long version, CancellationToken cancellationToken);
    Task CancelAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken);
    Task<ContainerPermissionIntent?> ClaimAsync(DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> CancellationRequestedAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> MarkPlanningAsync(ContainerPermissionIntent claimed, CancellationToken cancellationToken);
    Task FinishPlanningAsync(ContainerPermissionIntent claimed, ContainerPermissionPlanDto? plan, string code, CancellationToken cancellationToken);
    Task<ContainerFileUploadStartResult> StartedAsync(ContainerPermissionIntent claimed, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> RenewAsync(ContainerPermissionIntent claimed, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> FinishAsync(ContainerPermissionIntent claimed, string state, string code, ContainerPermissionReceiptDto? receipt, CancellationToken cancellationToken);
    Task DeferAsync(ContainerPermissionIntent claimed, CancellationToken cancellationToken);
    Task AcknowledgeAsync(TerminalActor actor, Guid id, long version, ContainerPermissionReceiptDto receipt, CancellationToken cancellationToken);
}
