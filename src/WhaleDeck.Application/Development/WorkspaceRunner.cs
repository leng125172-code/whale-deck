using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Development;

public sealed record WorkspaceRunCommand(Guid RunId, string Runtime, string[] Arguments, int CpuMillis, int MemoryMiB, int DeadlineSeconds,
    bool RequireControl = false);
public sealed record WorkspaceRunnerArtifact(WorkspaceTestFormat Format, string State, int Size, string Sha256);
public sealed record WorkspaceRunnerReceipt(Guid RunId, string RequestSha256, string Status, int? RuntimeExitCode,
    WorkspaceRunnerArtifact? TestReport = null);
public enum WorkspaceRunnerFailure { Retryable, NotFound, Interrupted, Unsupported }
public sealed class WorkspaceRunnerException(WorkspaceRunnerFailure failure) : Exception("WORKSPACE_RUN_HOST_UNAVAILABLE")
{
    public WorkspaceRunnerFailure Failure { get; } = failure;
}

public interface IWorkspaceRunnerGateway
{
    Task<WorkspaceRunnerReceipt> StartAsync(TerminalActor actor, WorkspaceDefinition workspace, WorkspaceRunCommand command,
        CancellationToken token);
    Task<WorkspaceRunnerReceipt> ObserveAsync(TerminalActor actor, WorkspaceDefinition workspace, WorkspaceRunCommand command,
        CancellationToken token);
    Task<WorkspaceRunnerReceipt> CancelAsync(TerminalActor actor, WorkspaceDefinition workspace, WorkspaceRunCommand command,
        CancellationToken token);
    Task<WorkspaceRunnerReceipt> ReclaimAsync(TerminalActor actor, WorkspaceDefinition workspace, WorkspaceRunCommand command,
        CancellationToken token);
}
