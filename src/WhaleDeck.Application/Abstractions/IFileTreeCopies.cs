using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Abstractions;

public interface IFileTreeCopyGateway
{
    Task<FileTreeCopyPlan> PlanTreeCopyAsync(TerminalActor actor, FileTreeCopyCommand command, CancellationToken cancellationToken);
    Task<FileTreeCopyReceipt?> InspectTreeCopyAsync(FileTreeCopyOperation operation, CancellationToken cancellationToken);
    Task<FileTreeCopyReceipt> ApplyTreeCopyAsync(FileTreeCopyOperation operation, CancellationToken cancellationToken);
    Task<FileTreeCopyReceipt> DiscardTreeCopyAsync(FileTreeCopyOperation operation, CancellationToken cancellationToken);
}
public interface IFileTreeCopyProtector
{
    string FingerprintRequest(FileTreeCopyRequestBinding binding);
    string FingerprintPlan(FileTreeCopyPlanBinding binding);
    string SignConfirmation(FileTreeCopyConfirmation claims);
    FileTreeCopyConfirmation VerifyConfirmation(string token);
}
public interface IFileTreeCopyRepository
{
    Task<FileTreeCopyOperation> QueueAsync(TerminalActor actor, string key, FileTreeCopyCommand command, CancellationToken cancellationToken);
    Task<FileTreeCopyOperation?> OwnedAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken);
    Task<FileTreeCopyOperation> ConfirmAsync(TerminalActor actor, FileTreeCopyConfirmation claims, long version, CancellationToken cancellationToken);
    Task CancelAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken);
    Task<FileTreeCopyOperation?> ClaimAsync(DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> CancellationRequestedAsync(FileTreeCopyOperation row, CancellationToken cancellationToken);
    Task<bool> MarkPlanningAsync(FileTreeCopyOperation claimed, CancellationToken cancellationToken);
    Task<bool> FinishPlanningAsync(FileTreeCopyOperation claimed, FileTreeCopyPlan? plan, string code, CancellationToken cancellationToken);
    Task<FileUploadStartResult> StartedAsync(FileTreeCopyOperation claimed, CancellationToken cancellationToken);
    Task<bool> RenewAsync(FileTreeCopyOperation claimed, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> FinishAsync(FileTreeCopyOperation claimed, string state, string code, FileTreeCopyReceipt? receipt, CancellationToken cancellationToken);
    Task DeferAsync(FileTreeCopyOperation claimed, CancellationToken cancellationToken);
    Task AcknowledgeAsync(TerminalActor actor, Guid id, long version, FileTreeCopyReceipt receipt, CancellationToken cancellationToken);
}
