using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Development;

public sealed record WorkspaceStoragePlan(Guid WorkspaceId, string StorageRootId, string EnvironmentSha256, int DiskMiB, string PlanSha256);
public sealed record WorkspaceRunnerPreflight(Guid WorkspaceId, long WorkspaceVersion, string EnvironmentSha256,
    WorkspaceBudget Budget, WorkspaceEnvironment Environment);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WorkspaceStorageApplyCommand(string PlanSha256);

public sealed class WorkspaceStorageAllocation(DevelopmentWorkspaces workspaces, IDevelopmentWorkspaceRegistry registry,
    IWorkspaceStorageGateway gateway, WorkspaceRegistrationPolicy policy, TimeProvider clock)
{
    public const string Registered = "Registered";
    public const string Allocating = "StorageAllocating";
    public const string Allocated = "StorageAllocated";

    public async Task<WorkspaceStoragePlan> PlanAsync(TerminalActor actor, Guid id, CancellationToken token)
    {
        var workspace = await RequireOwnedWorkspaceAsync(actor, id, token);
        if (workspace.State is not (Registered or Allocating or Allocated))
            throw new InvalidOperationException("WORKSPACE_STORAGE_PLAN_UNAVAILABLE");
        return BuildPlan(workspace);
    }

    public async Task<WorkspaceRunnerPreflight> PreflightAsync(TerminalActor actor, Guid id, CancellationToken token)
    {
        var workspace = await RequireOwnedWorkspaceAsync(actor, id, token);
        if (workspace.State != Allocated) throw new InvalidOperationException("WORKSPACE_STORAGE_NOT_ALLOCATED");
        await gateway.AttestAsync(actor, workspace, token);
        await workspaces.RequireCreatorAsync(actor, token);
        return new(workspace.Id, workspace.Version, workspace.EnvironmentSha256, workspace.Budget, workspace.Environment);
    }

    public async Task<WorkspaceDefinition> ApplyAsync(TerminalActor actor, Guid id, WorkspaceStorageApplyCommand command, CancellationToken token)
    {
        if (command.PlanSha256 is not { Length: 64 } || command.PlanSha256.Any(c => c is not (>= 'a' and <= 'f' or >= '0' and <= '9')))
            throw new ArgumentException("WORKSPACE_STORAGE_PLAN_INVALID");
        var plan = await PlanAsync(actor, id, token);
        if (command.PlanSha256 != plan.PlanSha256) throw new InvalidOperationException("WORKSPACE_STORAGE_PLAN_CHANGED");
        var workspace = await registry.BeginStorageAllocationAsync(actor, plan, clock.GetUtcNow(), token);
        if (workspace.State == Allocated) return workspace;

        // A canceled or uncertain RPC leaves the persisted intent intact.
        // Only a fresh authorized request may replay the same host allocation.
        await workspaces.RequireCreatorAsync(actor, token);
        await gateway.AllocateAsync(actor, workspace, token);
        return await registry.CompleteStorageAllocationAsync(actor, plan, clock.GetUtcNow(), token);
    }

    private async Task<WorkspaceDefinition> RequireOwnedWorkspaceAsync(TerminalActor actor, Guid id, CancellationToken token)
    {
        await workspaces.RequireCreatorAsync(actor, token);
        var workspace = await registry.FindAsync(id, actor.Subject, token) ?? throw new KeyNotFoundException("WORKSPACE_NOT_FOUND");
        if (!policy.StorageRootIds.Contains(workspace.StorageRootId, StringComparer.Ordinal) ||
            workspace.Budget.DiskMiB is < 256 or > WorkspaceRegistrationPolicy.MaximumWorkspaceDiskMiB)
            throw new InvalidOperationException("WORKSPACE_STORAGE_PLAN_UNAVAILABLE");
        return workspace;
    }

    public static WorkspaceStoragePlan BuildPlan(WorkspaceDefinition workspace)
    {
        var fingerprint = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema = "whaledeck-workspace-storage-v1", workspace.Id, workspace.OwnerSubject, workspace.StorageRootId,
            workspace.EnvironmentSha256, workspace.Budget.DiskMiB
        });
        return new(workspace.Id, workspace.StorageRootId, workspace.EnvironmentSha256, workspace.Budget.DiskMiB,
            Convert.ToHexStringLower(SHA256.HashData(fingerprint)));
    }
}
