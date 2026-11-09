using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Abstractions;

public interface IContainerRecycleArchiveRetirementProtector : IContainerRecycleArchiveRetirementSourceProtector
{
    string FingerprintRetirementRequest(ContainerRecycleArchiveRetirementIntent row);
    string FingerprintRetirementPlan(ContainerRecycleArchiveRetirementIntent row);
    string FingerprintRetirementExecution(ContainerRecycleArchiveRetirementIntent row);
    string FingerprintRetirementReceipt(ContainerRecycleArchiveRetirementOperationDto operation, ContainerRecycleArchiveRetirementReceiptDto receipt);
    string SignRetirementConfirmation(RetirementConfirmation claims);
    RetirementConfirmation VerifyRetirementConfirmation(string token);
}

public interface IContainerRecycleArchiveRetirementRepository
{
    Task<ContainerRecycleArchiveRetirementIntent?> FindAsync(Guid id, CancellationToken token);
    Task<ContainerRecycleArchiveRetirementIntent> EnqueueAsync(ContainerRecycleArchiveRetirementIntent row, CancellationToken token);
    Task<ContainerRecycleArchiveRetirementIntent> ConfirmAsync(TerminalActor actor, string confirmation, long version, CancellationToken token);
    Task CancelAsync(TerminalActor actor, Guid id, CancellationToken token);
    Task<ContainerRecycleArchiveRetirementIntent?> ClaimAsync(CancellationToken token);
    Task<bool> CancellationRequestedAsync(Guid id, CancellationToken token);
    Task<bool> MarkPlanningAsync(ContainerRecycleArchiveRetirementIntent claimed, CancellationToken token);
    Task FinishPlanningAsync(ContainerRecycleArchiveRetirementIntent claimed, ContainerRecycleArchiveRetirementPlanDto? plan, string code, CancellationToken token);
    Task<ContainerFileUploadStartResult> StartAsync(ContainerRecycleArchiveRetirementIntent claimed, CancellationToken token);
    Task<bool> ReserveAsync(ContainerRecycleArchiveRetirementIntent claimed, ContainerRecycleArchiveRetirementReceiptDto receipt, CancellationToken token);
    Task<ContainerFileUploadStartResult> EnterApplyAsync(ContainerRecycleArchiveRetirementIntent claimed, CancellationToken token);
    Task<bool> RenewAsync(ContainerRecycleArchiveRetirementIntent claimed, CancellationToken token);
    Task DeferAsync(ContainerRecycleArchiveRetirementIntent claimed, CancellationToken token);
    Task<bool> FinishAsync(ContainerRecycleArchiveRetirementIntent claimed, string code, ContainerRecycleArchiveRetirementReceiptDto? receipt, CancellationToken token);
    Task AcknowledgeAsync(TerminalActor actor, Guid id, long version, ContainerRecycleArchiveRetirementReceiptDto observation, CancellationToken token);
}
