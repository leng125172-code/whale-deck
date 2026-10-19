using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Abstractions;

public interface IFileTreeRecyclingGateway
{
    Task<FileTreeRecyclingPlan> PlanTreeRecyclingAsync(TerminalActor actor, FileTreeRecyclingCommand command, CancellationToken cancellationToken);
    Task<FileTreeRecyclingReceipt?> InspectTreeRecyclingAsync(FileTreeRecyclingOperation operation, CancellationToken cancellationToken);
    Task<FileTreeRecyclingReceipt> ApplyTreeRecyclingAsync(FileTreeRecyclingOperation operation, CancellationToken cancellationToken);
    Task<FileTreeRecyclingReceipt> DiscardTreeRecyclingAsync(FileTreeRecyclingOperation operation, CancellationToken cancellationToken);
}
public interface IFileTreeRecyclingProtector
{
    string FingerprintCapture(FileTreeCaptureBinding binding);
    string FingerprintRequest(FileTreeRecyclingRequestBinding binding);
    string FingerprintPlan(FileTreeRecyclingPlanBinding binding);
    string SignConfirmation(FileTreeRecyclingConfirmation claims);
    FileTreeRecyclingConfirmation VerifyRecyclingConfirmation(string token);
}
public interface IFileTreeRecyclingRepository
{
    Task<FileTreeCapturePage> CapturesAsync(FileLocationDto location, Guid? after, int take, CancellationToken cancellationToken);
    Task<FileTreeRecyclingOperation> QueueAsync(TerminalActor actor, string key, FileTreeRecyclingCommand command, CancellationToken cancellationToken);
    Task<FileTreeRecyclingOperation?> OwnedAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken);
    Task<FileTreeRecyclingOperation> ConfirmAsync(TerminalActor actor, FileTreeRecyclingConfirmation claims, long version, CancellationToken cancellationToken);
    Task CancelAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken);
    Task<FileTreeRecyclingOperation?> ClaimAsync(DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> CancellationRequestedAsync(FileTreeRecyclingOperation row, CancellationToken cancellationToken);
    Task<bool> MarkPlanningAsync(FileTreeRecyclingOperation claimed, CancellationToken cancellationToken);
    Task<bool> FinishPlanningAsync(FileTreeRecyclingOperation claimed, FileTreeRecyclingPlan? plan, string code, CancellationToken cancellationToken);
    Task<FileUploadStartResult> StartedAsync(FileTreeRecyclingOperation claimed, CancellationToken cancellationToken);
    Task<bool> RenewAsync(FileTreeRecyclingOperation claimed, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> FinishAsync(FileTreeRecyclingOperation claimed, string state, string code, FileTreeRecyclingReceipt? receipt, CancellationToken cancellationToken);
    Task DeferAsync(FileTreeRecyclingOperation claimed, CancellationToken cancellationToken);
    Task AcknowledgeAsync(TerminalActor actor, Guid id, long version, FileTreeRecyclingReceipt receipt, CancellationToken cancellationToken);
}
