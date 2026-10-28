using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Abstractions;

public interface IContainerCopyProtector
{
    string FingerprintCopyRequest(ContainerCopyRequestBinding binding);
    string FingerprintCopyPlan(ContainerCopyPlanBinding binding);
    string FingerprintCopyExecution(ContainerCopyExecutionBinding binding);
    string SignCopyConfirmation(ContainerCopyConfirmation claims);
    ContainerCopyConfirmation VerifyCopyConfirmation(string token);
}

/// <summary>Atomic persistent transitions; implementations must share publication exclusion with all file writers.</summary>
public interface IContainerCopyRepository
{
    Task<ContainerCopyIntent?> OwnedAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken);
    Task<ContainerCopyIntent?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ContainerCopyIntent>> UnknownAsync(int take, CancellationToken cancellationToken);
    Task<ContainerCopyIntent> EnqueueAsync(ContainerCopyIntent row, CancellationToken cancellationToken);
    Task<ContainerCopyIntent> ConfirmAsync(TerminalActor actor, ContainerCopyConfirmation claims, long version, CancellationToken cancellationToken);
    Task CancelAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken);
    Task<ContainerCopyIntent?> ClaimAsync(DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> CancellationRequestedAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> MarkPlanningAsync(ContainerCopyIntent row, CancellationToken cancellationToken);
    Task FinishPlanningAsync(ContainerCopyIntent row, ContainerCopyPlanDto? plan, string code, CancellationToken cancellationToken);
    Task<ContainerFileUploadStartResult> StartedAsync(ContainerCopyIntent row, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> RenewAsync(ContainerCopyIntent row, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> FinishAsync(ContainerCopyIntent row, string state, string code, ContainerCopyReceiptDto? receipt, CancellationToken cancellationToken);
    Task DeferAsync(ContainerCopyIntent row, CancellationToken cancellationToken);
    Task AcknowledgeAsync(TerminalActor actor, Guid id, long version, ContainerCopyReceiptDto receipt, CancellationToken cancellationToken);
}
