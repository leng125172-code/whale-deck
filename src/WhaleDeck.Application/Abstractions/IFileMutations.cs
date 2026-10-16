using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Abstractions;

public interface IFileMutationGateway
{
    Task<FileMutationPlan> PlanMutationAsync(TerminalActor actor, FileMutationCommand command, CancellationToken cancellationToken);
    Task<FileMutationReceipt?> InspectMutationAsync(FileMutationOperation operation, CancellationToken cancellationToken);
    Task<FileMutationReceipt> ApplyMutationAsync(FileMutationOperation operation, CancellationToken cancellationToken);
    Task<FileMutationReceipt> DiscardMutationAsync(FileMutationOperation operation, CancellationToken cancellationToken);
}
public interface IFileMutationProtector
{
    string Sign(FileMutationPlanClaims claims);
    FileMutationPlanClaims VerifyMutation(string plan);
    string Fingerprint(FileMutationBinding binding);
}
public interface IFileMutationRepository
{
    Task<FileMutationOperation?> OwnedAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken);
    Task<FileMutationOperation?> ResolveExpiredRegistrationAsync(TerminalActor actor, Guid id, DateTimeOffset planExpiry, CancellationToken cancellationToken);
    Task<FileMutationOperation> EnqueueAsync(TerminalActor actor, Guid id, string key, string requestHash, FileMutationPlanClaims claims, DateTimeOffset expires, CancellationToken cancellationToken);
    Task CancelAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken);
    Task<FileMutationOperation?> ClaimAsync(DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> CancellationRequestedAsync(Guid id, CancellationToken cancellationToken);
    Task<FileUploadStartResult> StartedAsync(FileMutationOperation operation, CancellationToken cancellationToken);
    Task<bool> FinishAsync(FileMutationOperation operation, string state, string code, FileMutationReceipt? receipt, CancellationToken cancellationToken);
    Task DeferAsync(FileMutationOperation operation, CancellationToken cancellationToken);
    Task AcknowledgeAsync(TerminalActor actor, Guid id, long version, FileMutationReceipt receipt, CancellationToken cancellationToken);
}
