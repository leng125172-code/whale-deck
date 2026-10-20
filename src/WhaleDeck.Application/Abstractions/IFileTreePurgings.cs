using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Abstractions;

public interface IFileTreePurgingGateway
{
    Task<FileTreePurgingPlan> PlanTreePurgingAsync(TerminalActor actor, FileTreePurgingCommand command, CancellationToken cancellationToken);
    Task<FileTreePurgingReceipt?> InspectTreePurgingAsync(FileTreePurgingOperation operation, CancellationToken cancellationToken);
    Task<FileTreePurgingReceipt> ApplyTreePurgingAsync(FileTreePurgingOperation operation, CancellationToken cancellationToken);
    Task<FileTreePurgingReceipt> DiscardTreePurgingAsync(FileTreePurgingOperation operation, CancellationToken cancellationToken);
}
public interface IFileTreePurgingProtector
{
    string FingerprintRequest(FileTreePurgingRequestBinding binding);
    string FingerprintPlan(FileTreePurgingPlanBinding binding);
    string FingerprintReceipt(FileTreePurgingReceiptBinding binding);
    string SignConfirmation(FileTreePurgingConfirmation claims);
    FileTreePurgingConfirmation VerifyPurgingConfirmation(string token);
}
public interface IFileTreePurgingRepository
{
    Task<FileTreePurgingOperation> QueueAsync(TerminalActor actor, string key, CreateFileTreePurgingCommand request, CancellationToken cancellationToken);
    Task<FileTreePurgingOperation?> OwnedAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken);
    Task<FileTreePurgingOperation> ConfirmAsync(TerminalActor actor, FileTreePurgingConfirmation claims, long version, CancellationToken cancellationToken);
    Task CancelAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken);
    Task<FileTreePurgingOperation?> ClaimAsync(DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> CancellationRequestedAsync(FileTreePurgingOperation claimed, CancellationToken cancellationToken);
    Task<bool> MarkPlanningAsync(FileTreePurgingOperation claimed, CancellationToken cancellationToken);
    Task<bool> FinishPlanningAsync(FileTreePurgingOperation claimed, FileTreePurgingPlan? plan, string code, CancellationToken cancellationToken);
    Task<FileUploadStartResult> StartedAsync(FileTreePurgingOperation claimed, CancellationToken cancellationToken);
    Task<bool> RenewAsync(FileTreePurgingOperation claimed, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> FinishAsync(FileTreePurgingOperation claimed, string state, string code, FileTreePurgingReceipt? receipt, CancellationToken cancellationToken);
    Task DeferAsync(FileTreePurgingOperation claimed, CancellationToken cancellationToken);
    Task AcknowledgeAsync(TerminalActor actor, Guid id, long version, FileTreePurgingReceipt receipt, CancellationToken cancellationToken);
}
