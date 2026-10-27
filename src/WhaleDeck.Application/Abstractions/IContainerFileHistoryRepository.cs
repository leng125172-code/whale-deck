using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Abstractions;

public interface IContainerFileHistoryRepository
{
    Task ObserveAsync(TerminalActor actor, ContainerHistoryPageDto page, CancellationToken cancellationToken);
    Task<ContainerHistoryEntryDto?> FindAsync(string containerId, string captureId, CancellationToken cancellationToken);
}

public interface IContainerFileRestoreProtector
{
    string SignContainerRestore(ContainerFileRestorePlanClaims claims);
    ContainerFileRestorePlanClaims VerifyContainerRestore(string token);
}
