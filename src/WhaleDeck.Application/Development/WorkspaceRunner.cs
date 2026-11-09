using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Development;

public sealed record WorkspaceRunCommand(Guid RunId, string Runtime, string[] Arguments, int CpuMillis, int MemoryMiB, int DeadlineSeconds);
public sealed record WorkspaceRunnerReceipt(Guid RunId, string RequestSha256, string Status, int? RuntimeExitCode);

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
