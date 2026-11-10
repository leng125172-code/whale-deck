using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Development;

// Internal transport only. A future persistent workflow must resolve this
// immutable task and the capture digest from its own authorized records.
// No controller, Worker dispatcher, source removal or budget release yet.
public sealed record WorkspaceArchiveTask(Guid TaskId, Guid ArchiveId, string Phase, long ExpectedEpoch,
    string PlanSha256, string ReceiptSha256);
public sealed record WorkspaceArchiveTaskProof(Guid TaskId, string RequestSha256, string Status, string? Code, string? ResultJson);
public enum WorkspaceArchiveFailure { Unsupported, Retryable, Integrity, Unconfirmed }
public sealed class WorkspaceArchiveException(WorkspaceArchiveFailure failure) : Exception("WORKSPACE_ARCHIVE_AGENT_" + failure)
{
    public WorkspaceArchiveFailure Failure { get; } = failure;
}
public interface IWorkspaceArchiveGateway
{
    Task<WorkspaceArchiveTaskProof> StartArchiveAsync(TerminalActor actor, WorkspaceDefinition workspace, WorkspaceArchiveTask task, CancellationToken token);
    Task<WorkspaceArchiveTaskProof> ObserveArchiveAsync(TerminalActor actor, WorkspaceDefinition workspace, WorkspaceArchiveTask task, CancellationToken token);
    Task<WorkspaceArchiveTaskProof> CancelArchiveAsync(TerminalActor actor, WorkspaceDefinition workspace, WorkspaceArchiveTask task, CancellationToken token);
}
