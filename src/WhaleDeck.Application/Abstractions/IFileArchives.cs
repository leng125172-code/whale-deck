using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Abstractions;

public interface IFileArchiveGateway
{
    Task<FileHistoryPage> ListHistoryAsync(FileHistoryCommand command, CancellationToken cancellationToken);
    Task<FileArchivePlan> PlanArchiveAsync(TerminalActor actor, FileArchiveCommand command, CancellationToken cancellationToken);
    Task<FileArchiveReceipt?> InspectArchiveAsync(FileArchiveOperation operation, CancellationToken cancellationToken);
    Task<FileArchiveReceipt> ApplyArchiveAsync(FileArchiveOperation operation, CancellationToken cancellationToken);
    Task<FileArchiveReceipt> DiscardArchiveAsync(FileArchiveOperation operation, CancellationToken cancellationToken);
}
public interface IFileArchiveProtector
{
    string Sign(FileArchivePlanClaims claims);
    FileArchivePlanClaims VerifyArchive(string plan);
    string Fingerprint(FileArchiveBinding binding);
}
public interface IFileArchiveRepository
{
    Task ObserveAsync(IReadOnlyCollection<FileVersionDto> versions, CancellationToken cancellationToken);
    Task<FileArchiveOperation?> OwnedAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken);
    Task<FileArchiveOperation?> ResolveExpiredRegistrationAsync(TerminalActor actor, Guid id, DateTimeOffset expiry, CancellationToken cancellationToken);
    Task<FileArchiveOperation> EnqueueAsync(TerminalActor actor, Guid id, string key, string requestHash, FileArchivePlanClaims claims, DateTimeOffset expires, CancellationToken cancellationToken);
    Task CancelAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken);
    Task<FileArchiveOperation?> ClaimAsync(DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> CancellationRequestedAsync(Guid id, CancellationToken cancellationToken);
    Task<FileUploadStartResult> StartedAsync(FileArchiveOperation operation, CancellationToken cancellationToken);
    Task<bool> RenewAsync(FileArchiveOperation operation, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> FinishAsync(FileArchiveOperation operation, string state, string code, FileArchiveReceipt? receipt, CancellationToken cancellationToken);
    Task DeferAsync(FileArchiveOperation operation, CancellationToken cancellationToken);
    Task AcknowledgeAsync(TerminalActor actor, Guid id, long version, FileArchiveReceipt receipt, CancellationToken cancellationToken);
}
