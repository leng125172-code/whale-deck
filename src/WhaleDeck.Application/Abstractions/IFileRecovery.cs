using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Abstractions;

public interface IFileRecoveryInventory
{
    Task<FileRecoveryPageDto> ListAsync(FileRecoveryCursor? after, int take, CancellationToken cancellationToken);
    Task<FileRecoveryCandidateDto?> GetAsync(string kind, Guid id, CancellationToken cancellationToken);
}
public interface IFileRecoveryProtector
{
    string SignRecovery(FileRecoveryPlanClaims claims);
    FileRecoveryPlanClaims VerifyRecovery(string token);
    string FingerprintRecoveryRequest(FileRecoveryRequestBinding binding);
    string FingerprintRecoveryReady(FileRecoveryPlanClaims claims);
}
public interface IFileRecoveryGateway
{
    Task<FileRecoveryObservation> InspectForRecoveryAsync(FileRecoverySnapshot original, TerminalActor currentActor, CancellationToken cancellationToken);
    Task<FileRecoveryObservation> FenceForRecoveryAsync(FileRecoverySnapshot original, TerminalActor currentActor, CancellationToken cancellationToken);
}
public interface IFileRecoveryRepository
{
    Task<FileRecoveryOperation> QueueAsync(TerminalActor actor, string key, QueueFileRecoveryCommand command, CancellationToken cancellationToken);
    Task<FileRecoveryOperation?> OwnedAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken);
    Task<FileRecoveryOperation> ConfirmAsync(TerminalActor actor, FileRecoveryPlanClaims claims, long version, CancellationToken cancellationToken);
    Task CancelAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken);
    Task<FileRecoveryOperation?> ClaimAsync(DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> RenewAsync(FileRecoveryOperation claimed, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> FinishPlanningAsync(FileRecoveryOperation claimed, FileRecoveryObservation? observation, string code, CancellationToken cancellationToken);
    Task<bool> BeginFenceAsync(FileRecoveryOperation claimed, CancellationToken cancellationToken);
    Task<bool> FinishAsync(FileRecoveryOperation claimed, FileRecoveryObservation? observation, string code, CancellationToken cancellationToken);
    Task DeferAsync(FileRecoveryOperation claimed, CancellationToken cancellationToken);
}
