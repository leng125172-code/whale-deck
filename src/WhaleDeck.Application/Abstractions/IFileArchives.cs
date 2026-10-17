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
