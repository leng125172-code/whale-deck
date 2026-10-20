using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Abstractions;

public interface IFileTreeOverwriteRestoringGateway
{
    Task<FileTreeOverwriteRestoringPlan> PlanTreeOverwriteRestoringAsync(TerminalActor actor, FileTreeOverwriteRestoringCommand command, CancellationToken cancellationToken);
    Task<FileTreeOverwriteRestoringReceipt?> InspectTreeOverwriteRestoringAsync(FileTreeOverwriteRestoringOperation operation, CancellationToken cancellationToken);
    Task<FileTreeOverwriteRestoringReceipt> ApplyTreeOverwriteRestoringAsync(FileTreeOverwriteRestoringOperation operation, CancellationToken cancellationToken);
    Task<FileTreeOverwriteRestoringReceipt> DiscardTreeOverwriteRestoringAsync(FileTreeOverwriteRestoringOperation operation, CancellationToken cancellationToken);
}
public interface IFileTreeOverwriteRestoringProtector
{
    string FingerprintRequest(FileTreeOverwriteRestoringRequestBinding binding);
    string FingerprintPlan(FileTreeOverwriteRestoringPlanBinding binding);
    string FingerprintCapture(FileTreeOverwrittenCaptureBinding binding);
    string SignConfirmation(FileTreeOverwriteRestoringConfirmation claims);
    FileTreeOverwriteRestoringConfirmation VerifyOverwriteRestoringConfirmation(string token);
}
public interface IFileTreeOverwriteRestoringRepository
{
    Task<FileTreeOverwriteRestoringOperation> QueueAsync(TerminalActor actor, string key, CreateFileTreeOverwriteRestoringCommand request, CancellationToken cancellationToken);
    Task<FileTreeOverwriteRestoringOperation?> OwnedAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken);
    Task<FileTreeOverwriteRestoringOperation> ConfirmAsync(TerminalActor actor, FileTreeOverwriteRestoringConfirmation claims, long version, CancellationToken cancellationToken);
    Task CancelAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken);
    Task<FileTreeOverwriteRestoringOperation?> ClaimAsync(DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> CancellationRequestedAsync(FileTreeOverwriteRestoringOperation row, CancellationToken cancellationToken);
    Task<bool> MarkPlanningAsync(FileTreeOverwriteRestoringOperation claimed, CancellationToken cancellationToken);
    Task<bool> FinishPlanningAsync(FileTreeOverwriteRestoringOperation claimed, FileTreeOverwriteRestoringPlan? plan, string code, CancellationToken cancellationToken);
    Task<FileUploadStartResult> StartedAsync(FileTreeOverwriteRestoringOperation claimed, CancellationToken cancellationToken);
    Task<bool> RenewAsync(FileTreeOverwriteRestoringOperation claimed, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> FinishAsync(FileTreeOverwriteRestoringOperation claimed, string state, string code, FileTreeOverwriteRestoringReceipt? receipt, CancellationToken cancellationToken);
    Task DeferAsync(FileTreeOverwriteRestoringOperation claimed, CancellationToken cancellationToken);
    Task AcknowledgeAsync(TerminalActor actor, Guid id, long version, FileTreeOverwriteRestoringReceipt receipt, CancellationToken cancellationToken);
}
