using System.Text.Json.Serialization;
using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Development;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WorkspaceRunInput(string Runtime, string Task);
public sealed record WorkspaceRunView(Guid RunId, Guid WorkspaceId, string Runtime, string Task, string State, string Phase,
    DateTimeOffset CreatedAtUtc, DateTimeOffset? CompletedAtUtc, bool CancellationRequested, string? ErrorCode, string? HostStatus);
public sealed record WorkspaceRunClaim(WorkspaceRunView Run, WorkspaceDefinition Workspace, TerminalActor Actor, Guid Lease,
    WorkspaceRunCommand Command, DateTimeOffset? DispatchedAtUtc);

public interface IWorkspaceRunStore
{
    Task<WorkspaceRunView> EnqueueAsync(WorkspaceDefinition workspace, TerminalActor actor, Guid key, WorkspaceRunInput input,
        CancellationToken token);
    Task<WorkspaceRunView?> GetAsync(Guid workspaceId, Guid runId, string owner, CancellationToken token);
    Task<WorkspaceRunView> CancelAsync(Guid workspaceId, Guid runId, TerminalActor actor, CancellationToken token);
    Task<WorkspaceRunClaim?> ClaimAsync(CancellationToken token);
    Task<bool> EnterDispatchAsync(WorkspaceRunClaim claim, CancellationToken token);
    Task DeferAsync(WorkspaceRunClaim claim, CancellationToken token);
    Task FinishAsync(WorkspaceRunClaim claim, string state, string phase, string? errorCode, WorkspaceRunnerReceipt? receipt,
        CancellationToken token);
}

public sealed class WorkspaceRuns(DevelopmentWorkspaces access, IDevelopmentWorkspaceRegistry registry,
    WorkspaceRegistrationPolicy policy, IWorkspaceRunStore queue)
{
    public const string JobType = "development.workspace.run";
    public const int DeadlineSeconds = 180;

    public async Task<WorkspaceRunView> EnqueueAsync(TerminalActor actor, Guid id, Guid key, WorkspaceRunInput input, CancellationToken token)
    {
        if (key == Guid.Empty) throw new ArgumentException("WORKSPACE_RUN_KEY_INVALID");
        _ = Arguments(input);
        var workspace = await RequireAsync(actor, id, token);
        if (workspace.State != WorkspaceStorageAllocation.Allocated ||
            !workspace.Environment.Runtimes.Any(pin => pin.Key == input.Runtime))
            throw new InvalidOperationException("WORKSPACE_RUN_RUNTIME_UNAVAILABLE");
        return await queue.EnqueueAsync(workspace, actor, key, input, token);
    }

    public async Task<WorkspaceRunView> GetAsync(TerminalActor actor, Guid id, Guid runId, CancellationToken token)
    {
        _ = await RequireAsync(actor, id, token);
        return await queue.GetAsync(id, runId, actor.Subject, token) ?? throw new KeyNotFoundException("WORKSPACE_RUN_NOT_FOUND");
    }

    public async Task<WorkspaceRunView> CancelAsync(TerminalActor actor, Guid id, Guid runId, CancellationToken token)
    {
        _ = await RequireAsync(actor, id, token);
        return await queue.CancelAsync(id, runId, actor, token);
    }

    internal async Task<WorkspaceDefinition> RequireAsync(TerminalActor actor, Guid id, CancellationToken token)
    {
        await access.RequireCreatorAsync(actor, token);
        var workspace = await registry.FindAsync(id, actor.Subject, token) ?? throw new KeyNotFoundException("WORKSPACE_NOT_FOUND");
        if (!policy.StorageRootIds.Contains(workspace.StorageRootId, StringComparer.Ordinal))
            throw new UnauthorizedAccessException("WORKSPACE_RUN_ROOT_REVOKED");
        return workspace;
    }

    public static WorkspaceRunCommand BindCommand(Guid runId, WorkspaceDefinition workspace, WorkspaceRunInput input) =>
        new(runId, input.Runtime, Arguments(input), workspace.Budget.CpuMillis, workspace.Budget.MemoryMiB,
            DeadlineSeconds, RequireControl: true);

    private static string[] Arguments(WorkspaceRunInput input) => (input.Runtime, input.Task) switch
    {
        ("node", "test") => ["/usr/local/bin/node", "--test"],
        ("dotnet", "build") => ["/usr/share/dotnet/dotnet", "build", "--no-restore"],
        ("dotnet", "test") => ["/usr/share/dotnet/dotnet", "test", "--no-restore"],
        _ => throw new ArgumentException("WORKSPACE_RUN_TASK_INVALID")
    };
}
