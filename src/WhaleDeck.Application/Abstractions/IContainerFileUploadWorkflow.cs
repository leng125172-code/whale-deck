using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Abstractions;

public interface IContainerFileUploadProtector
{
    string SignContainerUpload(ContainerFileUploadPlanClaims claims);
    ContainerFileUploadPlanClaims VerifyContainerUpload(string token);
    string FingerprintContainerUpload(ContainerFileUploadBinding binding);
}

public interface IContainerFileUploadRepository
{
    Task<ContainerFileUploadOperation?> OwnedAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken);
    Task<ContainerFileUploadOperation> EnqueueAsync(ContainerFileUploadOperation row, CancellationToken cancellationToken);
    Task<ContainerFileUploadOperation> BeginReceiveAsync(TerminalActor actor, Guid id, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> BeginRestoreReceiveAsync(ContainerFileUploadOperation row, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> MayReceiveAsync(Guid id, Guid token, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> ReadyAsync(ContainerFileUploadOperation row, CancellationToken cancellationToken);
    Task CancelAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken);
    Task<ContainerFileUploadOperation?> ClaimAsync(DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> RenewAsync(ContainerFileUploadOperation row, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> CancellationRequestedAsync(Guid id, CancellationToken cancellationToken);
    Task<ContainerFileUploadStartResult> StartedAsync(ContainerFileUploadOperation row, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> FinishAsync(ContainerFileUploadOperation row, string state, string code,
        ContainerFileUploadReceiptDto? receipt, CancellationToken cancellationToken);
    Task DeferAsync(ContainerFileUploadOperation row, CancellationToken cancellationToken);
}
