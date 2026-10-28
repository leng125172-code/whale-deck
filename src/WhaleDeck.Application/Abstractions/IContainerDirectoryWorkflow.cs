using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Abstractions;

public interface IContainerDirectoryProtector
{
    string SignDirectory(ContainerDirectoryPlanClaims claims);
    ContainerDirectoryPlanClaims VerifyDirectory(string token);
    string FingerprintDirectory(ContainerDirectoryBindingDto binding);
}
public interface IContainerDirectoryRepository
{
    Task<ContainerDirectoryOperation?> OwnedAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken);
    Task<ContainerDirectoryOperation?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ContainerDirectoryOperation>> UnknownAsync(int take, CancellationToken cancellationToken);
    Task<ContainerDirectoryOperation> EnqueueAsync(ContainerDirectoryOperation row, DateTimeOffset planExpiry, CancellationToken cancellationToken);
    Task<ContainerDirectoryOperation?> ResolveAsync(TerminalActor actor, Guid id, DateTimeOffset planExpiry, CancellationToken cancellationToken);
    Task CancelAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken);
    Task<ContainerDirectoryOperation?> ClaimAsync(DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> CancellationRequestedAsync(Guid id, CancellationToken cancellationToken);
    Task<ContainerFileUploadStartResult> StartedAsync(ContainerDirectoryOperation row, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> RenewAsync(ContainerDirectoryOperation row, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> FinishAsync(ContainerDirectoryOperation row, string state, string code, ContainerDirectoryReceiptDto? receipt, CancellationToken cancellationToken);
    Task DeferAsync(ContainerDirectoryOperation row, CancellationToken cancellationToken);
    Task AcknowledgeAsync(TerminalActor actor, Guid id, long version, ContainerDirectoryReceiptDto receipt, CancellationToken cancellationToken);
}
