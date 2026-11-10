using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Development;

// Internal host transport boundary. The caller must resolve current identity,
// the immutable persisted archive chain, and explicit execution confirmation.
// None of these fields are accepted directly from a browser/controller.
public sealed record WorkspaceRetirementTask(Guid TaskId, Guid RetirementId, Guid ArchiveId, string Phase,
    long ExpectedEpoch, string ArchivePlanSha256, string CaptureReceiptSha256, string RetirementPlanSha256);
public sealed record WorkspaceRetirementTaskProof(Guid TaskId, string RequestSha256, string Status, string? Code, string? ResultJson);
public interface IWorkspaceRetirementGateway
{
    Task<WorkspaceRetirementTaskProof> StartRetirementAsync(TerminalActor actor, WorkspaceDefinition workspace, WorkspaceRetirementTask task, CancellationToken token);
    Task<WorkspaceRetirementTaskProof> ObserveRetirementAsync(TerminalActor actor, WorkspaceDefinition workspace, WorkspaceRetirementTask task, CancellationToken token);
    Task<WorkspaceRetirementTaskProof> CancelRetirementAsync(TerminalActor actor, WorkspaceDefinition workspace, WorkspaceRetirementTask task, CancellationToken token);
}
public enum WorkspaceRetirementFailure { Unsupported, Unconfirmed, Retryable, Integrity }
public sealed class WorkspaceRetirementException(WorkspaceRetirementFailure failure, bool startWasNotDispatched = false)
    : Exception("WORKSPACE_RETIREMENT_" + failure.ToString().ToUpperInvariant())
{
    public WorkspaceRetirementFailure Failure { get; } = failure;
    public bool StartWasNotDispatched { get; } = startWasNotDispatched;
}
