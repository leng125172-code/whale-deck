using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Development;

// Internal transport. The persistent workflow resolves the immutable task and
// digest chain from authorized records; no source removal or budget release.
public sealed record WorkspaceArchiveTask(Guid TaskId, Guid ArchiveId, string Phase, long ExpectedEpoch,
    string PlanSha256, string ReceiptSha256);
public sealed record WorkspaceArchiveTaskProof(Guid TaskId, string RequestSha256, string Status, string? Code, string? ResultJson);
public enum WorkspaceArchiveFailure { Unsupported, Retryable, Integrity, Unconfirmed }
public sealed class WorkspaceArchiveException(WorkspaceArchiveFailure failure, bool startWasNotDispatched = false) : Exception("WORKSPACE_ARCHIVE_AGENT_" + failure)
{
    public WorkspaceArchiveFailure Failure { get; } = failure;
    // Only gateway preflight, before invoking the archive RPC, can establish
    // this fact. An RPC error or lost reply after invocation never does.
    public bool StartWasNotDispatched { get; } = startWasNotDispatched;
}
public interface IWorkspaceArchiveGateway
{
    Task<WorkspaceArchiveTaskProof> StartArchiveAsync(TerminalActor actor, WorkspaceDefinition workspace, WorkspaceArchiveTask task, CancellationToken token);
    Task<WorkspaceArchiveTaskProof> ObserveArchiveAsync(TerminalActor actor, WorkspaceDefinition workspace, WorkspaceArchiveTask task, CancellationToken token);
    Task<WorkspaceArchiveTaskProof> CancelArchiveAsync(TerminalActor actor, WorkspaceDefinition workspace, WorkspaceArchiveTask task, CancellationToken token);
}
