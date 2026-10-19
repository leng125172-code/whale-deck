using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Abstractions;

public interface IFileTreeUnpackingGateway
{
    Task<FileTreeUnpackingPlan> PlanTreeUnpackingAsync(TerminalActor actor, FileTreeUnpackingCommand command, CancellationToken cancellationToken);
    Task<FileTreeUnpackingReceipt?> InspectTreeUnpackingAsync(FileTreeUnpackingOperation operation, CancellationToken cancellationToken);
    Task<FileTreeUnpackingReceipt> ApplyTreeUnpackingAsync(FileTreeUnpackingOperation operation, CancellationToken cancellationToken);
    Task<FileTreeUnpackingReceipt> DiscardTreeUnpackingAsync(FileTreeUnpackingOperation operation, CancellationToken cancellationToken);
}
public interface IFileTreeUnpackingProtector
{
    string FingerprintRequest(FileTreeUnpackingRequestBinding binding);
    string FingerprintPlan(FileTreeUnpackingPlanBinding binding);
    string SignConfirmation(FileTreeUnpackingConfirmation claims);
    FileTreeUnpackingConfirmation VerifyUnpackingConfirmation(string token);
}
public interface IFileTreeUnpackingRepository
{
    Task<FileTreeUnpackingOperation> QueueAsync(TerminalActor actor, string key, FileTreeUnpackingCommand command, CancellationToken cancellationToken);
    Task<FileTreeUnpackingOperation?> OwnedAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken);
    Task<FileTreeUnpackingOperation> ConfirmAsync(TerminalActor actor, FileTreeUnpackingConfirmation claims, long version, CancellationToken cancellationToken);
    Task CancelAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken);
    Task<FileTreeUnpackingOperation?> ClaimAsync(DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> CancellationRequestedAsync(FileTreeUnpackingOperation row, CancellationToken cancellationToken);
    Task<bool> MarkPlanningAsync(FileTreeUnpackingOperation claimed, CancellationToken cancellationToken);
    Task<bool> FinishPlanningAsync(FileTreeUnpackingOperation claimed, FileTreeUnpackingPlan? plan, string code, CancellationToken cancellationToken);
    Task<FileUploadStartResult> StartedAsync(FileTreeUnpackingOperation claimed, CancellationToken cancellationToken);
    Task<bool> RenewAsync(FileTreeUnpackingOperation claimed, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> FinishAsync(FileTreeUnpackingOperation claimed, string state, string code, FileTreeUnpackingReceipt? receipt, CancellationToken cancellationToken);
    Task DeferAsync(FileTreeUnpackingOperation claimed, CancellationToken cancellationToken);
    Task AcknowledgeAsync(TerminalActor actor, Guid id, long version, FileTreeUnpackingReceipt receipt, CancellationToken cancellationToken);
}
