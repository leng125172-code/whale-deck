using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Abstractions;

public interface IFileTreePackingGateway
{
    Task<FileTreePackingPlan> PlanTreePackingAsync(TerminalActor actor, FileTreePackingCommand command, CancellationToken cancellationToken);
    Task<FileTreePackingReceipt?> InspectTreePackingAsync(FileTreePackingOperation operation, CancellationToken cancellationToken);
    Task<FileTreePackingReceipt> ApplyTreePackingAsync(FileTreePackingOperation operation, CancellationToken cancellationToken);
    Task<FileTreePackingReceipt> DiscardTreePackingAsync(FileTreePackingOperation operation, CancellationToken cancellationToken);
    IAsyncEnumerable<FileDownloadPart> DownloadTreePackingAsync(FileTreePackingOperation operation, TerminalActor actor, CancellationToken cancellationToken);
}
public interface IFileTreePackingProtector
{
    string FingerprintRequest(FileTreePackingRequestBinding binding);
    string FingerprintPlan(FileTreePackingPlanBinding binding);
    string SignConfirmation(FileTreePackingConfirmation claims);
    FileTreePackingConfirmation VerifyPackingConfirmation(string token);
}
public interface IFileTreePackingRepository
{
    Task<FileTreePackingOperation> QueueAsync(TerminalActor actor, string key, FileTreePackingCommand command, CancellationToken cancellationToken);
    Task<FileTreePackingOperation?> OwnedAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken);
    Task<FileTreePackingOperation?> ArtifactAsync(Guid id, CancellationToken cancellationToken);
    Task AuditDownloadAsync(TerminalActor actor, FileTreePackingOperation operation, string result, CancellationToken cancellationToken);
    Task<FileTreePackingOperation> ConfirmAsync(TerminalActor actor, FileTreePackingConfirmation claims, long version, CancellationToken cancellationToken);
    Task CancelAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken);
    Task<FileTreePackingOperation?> ClaimAsync(DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> CancellationRequestedAsync(FileTreePackingOperation row, CancellationToken cancellationToken);
    Task<bool> MarkPlanningAsync(FileTreePackingOperation claimed, CancellationToken cancellationToken);
    Task<bool> FinishPlanningAsync(FileTreePackingOperation claimed, FileTreePackingPlan? plan, string code, CancellationToken cancellationToken);
    Task<FileUploadStartResult> StartedAsync(FileTreePackingOperation claimed, CancellationToken cancellationToken);
    Task<bool> RenewAsync(FileTreePackingOperation claimed, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> FinishAsync(FileTreePackingOperation claimed, string state, string code, FileTreePackingReceipt? receipt, CancellationToken cancellationToken);
    Task DeferAsync(FileTreePackingOperation claimed, CancellationToken cancellationToken);
    Task AcknowledgeAsync(TerminalActor actor, Guid id, long version, FileTreePackingReceipt receipt, CancellationToken cancellationToken);
}
