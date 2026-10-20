using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Abstractions;

public interface IFileTreeRestoringGateway
{
    Task<FileTreeRestoringPlan> PlanTreeRestoringAsync(TerminalActor actor, FileTreeRestoringCommand command, CancellationToken cancellationToken);
    Task<FileTreeRestoringReceipt?> InspectTreeRestoringAsync(FileTreeRestoringOperation operation, CancellationToken cancellationToken);
    Task<FileTreeRestoringReceipt> ApplyTreeRestoringAsync(FileTreeRestoringOperation operation, CancellationToken cancellationToken);
    Task<FileTreeRestoringReceipt> DiscardTreeRestoringAsync(FileTreeRestoringOperation operation, CancellationToken cancellationToken);
}
public interface IFileTreeRestoringProtector
{
    string FingerprintRequest(FileTreeRestoringRequestBinding binding);
    string FingerprintPlan(FileTreeRestoringPlanBinding binding);
    string SignConfirmation(FileTreeRestoringConfirmation claims);
    FileTreeRestoringConfirmation VerifyRestoringConfirmation(string token);
}
public interface IFileTreeRestoringRepository
{
    Task<FileTreeRestoringOperation> QueueAsync(TerminalActor actor, string key, CreateFileTreeRestoringCommand request, CancellationToken cancellationToken);
    Task<FileTreeRestoringOperation?> OwnedAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken);
    Task<FileTreeRestoringOperation> ConfirmAsync(TerminalActor actor, FileTreeRestoringConfirmation claims, long version, CancellationToken cancellationToken);
    Task CancelAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken);
    Task<FileTreeRestoringOperation?> ClaimAsync(DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> CancellationRequestedAsync(FileTreeRestoringOperation row, CancellationToken cancellationToken);
    Task<bool> MarkPlanningAsync(FileTreeRestoringOperation claimed, CancellationToken cancellationToken);
    Task<bool> FinishPlanningAsync(FileTreeRestoringOperation claimed, FileTreeRestoringPlan? plan, string code, CancellationToken cancellationToken);
    Task<FileUploadStartResult> StartedAsync(FileTreeRestoringOperation claimed, CancellationToken cancellationToken);
    Task<bool> RenewAsync(FileTreeRestoringOperation claimed, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> FinishAsync(FileTreeRestoringOperation claimed, string state, string code, FileTreeRestoringReceipt? receipt, CancellationToken cancellationToken);
    Task DeferAsync(FileTreeRestoringOperation claimed, CancellationToken cancellationToken);
    Task AcknowledgeAsync(TerminalActor actor, Guid id, long version, FileTreeRestoringReceipt receipt, CancellationToken cancellationToken);
}
