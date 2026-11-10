using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Development;

public sealed record WorkspaceLifecycleHostProof(Guid WorkspaceId, string Action, long Epoch, string State,
    string PlanSha256, string StorageProofSha256);
public sealed record WorkspaceLifecyclePlan(Guid WorkspaceId, string Action, long ExpectedVersion,
    string HostPlanSha256, string StorageProofSha256, string PlanSha256)
{
    public bool RetainsData { get; } = true;
    public bool ReservesBudget { get; } = true;
    public bool RequiresIdle { get; } = true;
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WorkspaceLifecycleCommand(string Action, string PlanSha256);
public sealed record WorkspaceLifecycleView(Guid OperationId, Guid WorkspaceId, string Action,
    string State, string Phase, string? Code, DateTimeOffset? CompletedAtUtc);
public sealed record WorkspaceLifecycleClaim(WorkspaceLifecycleView View, WorkspaceDefinition Workspace,
    WorkspaceLifecyclePlan Plan, TerminalActor Actor, Guid Lease);
public enum WorkspaceLifecycleFailure { Unsupported, Retryable, Integrity, Rejected }
public sealed class WorkspaceLifecycleException(WorkspaceLifecycleFailure failure) : Exception("WORKSPACE_LIFECYCLE_AGENT_" + failure)
{
    public WorkspaceLifecycleFailure Failure { get; } = failure;
}
public interface IWorkspaceLifecycleGateway
{
    Task<WorkspaceLifecycleHostProof> PlanAsync(TerminalActor actor, WorkspaceDefinition workspace, string action, CancellationToken token);
    Task<WorkspaceLifecycleHostProof> ApplyAsync(TerminalActor actor, WorkspaceDefinition workspace,
        WorkspaceLifecyclePlan plan, Guid operationId, CancellationToken token);
}
public interface IWorkspaceLifecycleStore
{
    Task RequireIdleAsync(Guid workspaceId, CancellationToken token);
    Task<WorkspaceLifecycleView?> ReplayAsync(TerminalActor actor, Guid workspaceId, Guid key, WorkspaceLifecycleCommand command, CancellationToken token);
    Task<WorkspaceLifecycleView> AdmitAsync(TerminalActor actor, WorkspaceDefinition workspace, Guid key,
        WorkspaceLifecycleCommand command, WorkspaceLifecyclePlan plan, CancellationToken token);
    Task<WorkspaceLifecycleView?> CurrentAsync(Guid workspaceId, CancellationToken token);
    Task<WorkspaceLifecycleView?> GetAsync(Guid workspaceId, Guid operationId, CancellationToken token);
    Task<WorkspaceLifecycleView> ReauthorizeAsync(TerminalActor actor, Guid workspaceId, Guid operationId, CancellationToken token);
    Task<WorkspaceLifecycleClaim?> ClaimAsync(CancellationToken token);
    Task CompleteAsync(WorkspaceLifecycleClaim claim, WorkspaceLifecycleHostProof proof, CancellationToken token);
    Task DeferAsync(WorkspaceLifecycleClaim claim, string code, CancellationToken token);
}

public sealed class WorkspaceLifecycle(WorkspaceReportAuthorization authorization, DevelopmentWorkspaces workspaces,
    IDevelopmentWorkspaceRegistry registry, WorkspaceRegistrationPolicy policy, IWorkspaceLifecycleStore store,
    IWorkspaceLifecycleGateway gateway)
{
    public const string JobType = "development.workspace.lifecycle";
    public const string Active = "Active";
    public const string Stopping = "Stopping";
    public const string Stopped = "Stopped";
    public const string Resuming = "Resuming";

    public async Task<WorkspaceLifecyclePlan> PlanAsync(TerminalActor actor, Guid id, string action, CancellationToken token)
    {
        ValidateAction(action);
        var workspace = await RequireAsync(actor, id, action, token);
        var expected = action == "stop" ? Active : Stopped;
        if (workspace.State != WorkspaceStorageAllocation.Allocated || workspace.LifecycleState != expected ||
            workspace.LifecycleVersion is < 1 or >= long.MaxValue - 2)
            throw new InvalidOperationException("WORKSPACE_LIFECYCLE_STATE_CHANGED");
        await store.RequireIdleAsync(id, token);
        var proof = await gateway.PlanAsync(actor, workspace, action, token);
        if (proof.WorkspaceId != id || proof.Action != action || proof.Epoch != workspace.LifecycleVersion ||
            proof.State != expected || !Digest(proof.PlanSha256) || !Digest(proof.StorageProofSha256))
            throw new WorkspaceLifecycleException(WorkspaceLifecycleFailure.Integrity);
        return BuildPlan(workspace, proof);
    }

    public async Task<WorkspaceLifecycleView> ApplyAsync(TerminalActor actor, Guid id, Guid key,
        WorkspaceLifecycleCommand command, CancellationToken token)
    {
        ValidateAction(command.Action);
        if (key == Guid.Empty || !Digest(command.PlanSha256)) throw new ArgumentException("WORKSPACE_LIFECYCLE_REQUEST_INVALID");
        var workspace = await RequireAsync(actor, id, command.Action, token);
        var replay = await store.ReplayAsync(actor, id, key, command, token);
        if (replay is not null) return replay;
        var plan = await PlanAsync(actor, id, command.Action, token);
        if (plan.PlanSha256 != command.PlanSha256) throw new InvalidOperationException("WORKSPACE_LIFECYCLE_PLAN_CHANGED");
        return await store.AdmitAsync(actor, workspace, key, command, plan, token);
    }

    public async Task<WorkspaceLifecycleView?> CurrentAsync(TerminalActor actor, Guid id, CancellationToken token)
    {
        _ = await RequireAsync(actor, id, null, token);
        return await store.CurrentAsync(id, token);
    }
    public async Task<WorkspaceLifecycleView> GetAsync(TerminalActor actor, Guid id, Guid operationId, CancellationToken token)
    {
        _ = await RequireAsync(actor, id, null, token);
        return await store.GetAsync(id, operationId, token) ?? throw new KeyNotFoundException("WORKSPACE_LIFECYCLE_NOT_FOUND");
    }
    public async Task<WorkspaceLifecycleView> ReauthorizeAsync(TerminalActor actor, Guid id, Guid operationId, CancellationToken token)
    {
        var view = await GetAsync(actor, id, operationId, token);
        _ = await RequireAsync(actor, id, view.Action, token);
        return await store.ReauthorizeAsync(actor, id, operationId, token);
    }
    internal async Task RequireExecutionAsync(WorkspaceLifecycleClaim claim, CancellationToken token) =>
        _ = await RequireAsync(claim.Actor, claim.Workspace.Id, claim.Plan.Action, token);

    private async Task<WorkspaceDefinition> RequireAsync(TerminalActor actor, Guid id, string? action, CancellationToken token)
    {
        var current = await authorization.ResolveAsync(actor, token);
        var workspace = await registry.FindAsync(id, current.IsAdministrator ? null : actor.Subject, token)
            ?? throw new KeyNotFoundException("WORKSPACE_NOT_FOUND");
        if (!policy.StorageRootIds.Contains(workspace.StorageRootId, StringComparer.Ordinal))
            throw new UnauthorizedAccessException("WORKSPACE_LIFECYCLE_ROOT_REVOKED");
        if (action == "resume") await workspaces.RequireCreatorAsync(actor, token);
        return workspace;
    }

    public static WorkspaceLifecyclePlan BuildPlan(WorkspaceDefinition workspace, WorkspaceLifecycleHostProof proof)
    {
        var raw = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema = "whaledeck-workspace-idle-lifecycle-v1", workspace.Id, workspace.OwnerSubject, workspace.StorageRootId,
            workspace.Version, workspace.EnvironmentSha256, workspace.Budget, workspace.LifecycleState, workspace.LifecycleVersion,
            proof.Action, proof.PlanSha256, proof.StorageProofSha256
        });
        return new(workspace.Id, proof.Action, workspace.LifecycleVersion, proof.PlanSha256, proof.StorageProofSha256,
            Convert.ToHexStringLower(SHA256.HashData(raw)));
    }
    private static void ValidateAction(string action)
    {
        if (action is not ("stop" or "resume")) throw new ArgumentException("WORKSPACE_LIFECYCLE_ACTION_INVALID");
    }
    private static bool Digest(string? value) => value is { Length: 64 } && value.All(c => c is >= 'a' and <= 'f' or >= '0' and <= '9');
}
