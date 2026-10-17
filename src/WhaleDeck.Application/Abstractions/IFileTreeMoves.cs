using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Abstractions;

public interface IFileTreeMoveGateway
{
    Task<FileTreeMovePlan> PlanTreeMoveAsync(TerminalActor actor, FileTreeCopyCommand command, CancellationToken cancellationToken);
    Task<FileTreeMoveReceipt?> InspectTreeMoveAsync(FileTreeMoveOperation operation, CancellationToken cancellationToken);
    Task<FileTreeMoveReceipt> ApplyTreeMoveAsync(FileTreeMoveOperation operation, CancellationToken cancellationToken);
    Task<FileTreeMoveReceipt> DiscardTreeMoveAsync(FileTreeMoveOperation operation, CancellationToken cancellationToken);
}
public interface IFileTreeMoveRepository
{
    Task<FileTreeMoveOperation> QueueAsync(TerminalActor actor, string key, FileTreeCopyCommand command, CancellationToken cancellationToken);
    Task<FileTreeMoveOperation?> OwnedAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken);
    Task<FileTreeMoveOperation> ConfirmAsync(TerminalActor actor, FileTreeMoveConfirmation claims, long version, CancellationToken cancellationToken);
    Task CancelAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken);
    Task<FileTreeMoveOperation?> ClaimAsync(DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> CancellationRequestedAsync(FileTreeMoveOperation row, CancellationToken cancellationToken);
    Task<bool> MarkPlanningAsync(FileTreeMoveOperation claimed, CancellationToken cancellationToken);
    Task<bool> FinishPlanningAsync(FileTreeMoveOperation claimed, FileTreeMovePlan? plan, string code, CancellationToken cancellationToken);
    Task<FileUploadStartResult> StartedAsync(FileTreeMoveOperation claimed, CancellationToken cancellationToken);
    Task<bool> RenewAsync(FileTreeMoveOperation claimed, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> FinishAsync(FileTreeMoveOperation claimed, string state, string code, FileTreeMoveReceipt? receipt, CancellationToken cancellationToken);
    Task DeferAsync(FileTreeMoveOperation claimed, CancellationToken cancellationToken);
    Task AcknowledgeAsync(TerminalActor actor, Guid id, long version, FileTreeMoveReceipt receipt, CancellationToken cancellationToken);
}
