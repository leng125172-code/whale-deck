using System.Text.Json.Serialization;
using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Development;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WorkspaceRetirementBegin(Guid ArchiveId, long ExpectedEpoch);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WorkspaceRetirementConfirmation(string PlanSha256, bool DeleteSourceAndReleaseBudget);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WorkspaceRetirementRecoveryConfirmation(Guid PreviousTaskId, string PlanSha256, bool ContinueDeletionAndReleaseBudget);
public sealed record WorkspaceRetirementAttemptView(Guid TaskId, int Attempt, Guid? PreviousTaskId,
    string Status, string? Code, DateTimeOffset? RecoveryConfirmedAtUtc, DateTimeOffset? DispatchedAtUtc,
    DateTimeOffset? SettledAtUtc, string? ProofSha256);
public sealed record WorkspaceRetirementView(Guid RetirementId, Guid WorkspaceId, Guid ArchiveId, string State,
    string Phase, string? Code, bool AwaitingConfirmation, bool AuthorizationRequired, bool CancelRequested,
    bool AttentionRequired, string? PlanJson, DateTimeOffset? PlanExpiresAtUtc, DateTimeOffset? ConfirmedAtUtc,
    string? ExecutionJson, DateTimeOffset? CompletedAtUtc)
{
    public Guid? ExecuteTaskId { get; init; }
    public int ExecutionAttempt { get; init; }
    public bool BudgetReleased => State == "Succeeded" && Phase == "Retired";
    public bool SourceRetired => BudgetReleased;
    public bool RetainsHdd { get; } = true;
}
public sealed record WorkspaceRetirementClaim(WorkspaceRetirementView View, WorkspaceDefinition Workspace,
    WorkspaceRetirementTask Task, TerminalActor Actor, Guid Lease);

public interface IWorkspaceRetirementStore
{
    Task<WorkspaceRetirementView> BeginAsync(TerminalActor actor, WorkspaceDefinition workspace, Guid key, Guid archiveId, long epoch, CancellationToken token);
    Task<WorkspaceRetirementView?> CurrentAsync(Guid workspaceId, CancellationToken token);
    Task<WorkspaceRetirementView?> GetAsync(Guid workspaceId, Guid retirementId, CancellationToken token);
    Task<WorkspaceRetirementView> ConfirmAsync(TerminalActor actor, Guid workspaceId, Guid retirementId, string planSha256, CancellationToken token);
    Task<WorkspaceRetirementView> CancelAsync(TerminalActor actor, Guid workspaceId, Guid retirementId, CancellationToken token);
    Task<WorkspaceRetirementView> ReauthorizeAsync(TerminalActor actor, Guid workspaceId, Guid retirementId, CancellationToken token);
    Task<WorkspaceRetirementView> RecoverAsync(TerminalActor actor, Guid workspaceId, Guid retirementId, Guid key, WorkspaceRetirementRecoveryConfirmation input, CancellationToken token);
    Task<IReadOnlyList<WorkspaceRetirementAttemptView>> AttemptsAsync(Guid workspaceId, Guid retirementId, CancellationToken token);
    Task<WorkspaceRetirementClaim?> ClaimAsync(CancellationToken token);
    Task<bool> MarkDispatchAsync(WorkspaceRetirementClaim claim, CancellationToken token);
    Task RecordAsync(WorkspaceRetirementClaim claim, WorkspaceRetirementTaskProof proof, CancellationToken token);
    Task<bool> AuthorizationLostAsync(WorkspaceRetirementClaim claim, CancellationToken token);
    Task DeferAsync(WorkspaceRetirementClaim claim, string code, CancellationToken token);
    Task DeferUndispatchedAsync(WorkspaceRetirementClaim claim, string code, CancellationToken token);
}

public sealed class WorkspaceRetirements(WorkspaceReportAuthorization authorization, DevelopmentWorkspaces development,
    IDevelopmentWorkspaceRegistry registry, WorkspaceRegistrationPolicy policy, IWorkspaceRetirementStore store)
{
    public const string JobType = "development.workspace.retirement";
    public const string Retired = "Retired";
    public async Task<WorkspaceRetirementView> BeginAsync(TerminalActor actor, Guid id, Guid key, WorkspaceRetirementBegin input, CancellationToken token)
    {
        if (key == Guid.Empty || input.ArchiveId == Guid.Empty || input.ExpectedEpoch < 2 || input.ExpectedEpoch % 2 != 0)
            throw new ArgumentException("WORKSPACE_RETIREMENT_REQUEST_INVALID");
        var workspace = await RequireAsync(actor, id, token); RequireRoot(workspace);
        return await store.BeginAsync(actor, workspace, key, input.ArchiveId, input.ExpectedEpoch, token);
    }
    public async Task<WorkspaceRetirementView?> CurrentAsync(TerminalActor actor, Guid id, CancellationToken token)
    {
        _ = await RequireAsync(actor, id, token); return await store.CurrentAsync(id, token);
    }
    public async Task<WorkspaceRetirementView> GetAsync(TerminalActor actor, Guid id, Guid retirementId, CancellationToken token)
    {
        _ = await RequireAsync(actor, id, token);
        return await store.GetAsync(id, retirementId, token) ?? throw new KeyNotFoundException("WORKSPACE_RETIREMENT_NOT_FOUND");
    }
    public async Task<WorkspaceRetirementView> ConfirmAsync(TerminalActor actor, Guid id, Guid retirementId, WorkspaceRetirementConfirmation input, CancellationToken token)
    {
        RequireRoot(await RequireAsync(actor, id, token));
        if (!input.DeleteSourceAndReleaseBudget || input.PlanSha256 is not { Length: 64 } ||
            input.PlanSha256.Any(c => c is not (>= 'a' and <= 'f' or >= '0' and <= '9')))
            throw new ArgumentException("WORKSPACE_RETIREMENT_CONFIRMATION_INVALID");
        return await store.ConfirmAsync(actor, id, retirementId, input.PlanSha256, token);
    }
    public async Task<WorkspaceRetirementView> CancelAsync(TerminalActor actor, Guid id, Guid retirementId, CancellationToken token)
    {
        _ = await RequireAsync(actor, id, token); return await store.CancelAsync(actor, id, retirementId, token);
    }
    public async Task<WorkspaceRetirementView> ReauthorizeAsync(TerminalActor actor, Guid id, Guid retirementId, CancellationToken token)
    {
        RequireRoot(await RequireAsync(actor, id, token)); return await store.ReauthorizeAsync(actor, id, retirementId, token);
    }
    public async Task<WorkspaceRetirementView> RecoverAsync(TerminalActor actor, Guid id, Guid retirementId, Guid key, WorkspaceRetirementRecoveryConfirmation input, CancellationToken token)
    {
        RequireRoot(await RequireAsync(actor, id, token));
        if (key == Guid.Empty || input.PreviousTaskId == Guid.Empty || !input.ContinueDeletionAndReleaseBudget ||
            input.PlanSha256 is not { Length: 64 } || input.PlanSha256.Any(c => c is not (>= 'a' and <= 'f' or >= '0' and <= '9')))
            throw new ArgumentException("WORKSPACE_RETIREMENT_RECOVERY_CONFIRMATION_INVALID");
        return await store.RecoverAsync(actor, id, retirementId, key, input, token);
    }
    public async Task<IReadOnlyList<WorkspaceRetirementAttemptView>> AttemptsAsync(TerminalActor actor, Guid id, Guid retirementId, CancellationToken token)
    {
        _ = await GetAsync(actor, id, retirementId, token);
        return await store.AttemptsAsync(id, retirementId, token);
    }
    internal async Task RequireExecutionAsync(WorkspaceRetirementClaim claim, CancellationToken token)
    {
        var current = await RequireAsync(claim.Actor, claim.Workspace.Id, token); RequireRoot(current);
        if (current.State != WorkspaceStorageAllocation.Allocated || current.LifecycleState != WorkspaceRetirementPreparations.Retiring ||
            current.LifecycleVersion != claim.Task.ExpectedEpoch || current.EnvironmentSha256 != claim.Workspace.EnvironmentSha256)
            throw new UnauthorizedAccessException("WORKSPACE_RETIREMENT_BINDING_REVOKED");
    }
    private async Task<WorkspaceDefinition> RequireAsync(TerminalActor actor, Guid id, CancellationToken token)
    {
        var current = await authorization.ResolveAsync(actor, token);
        if (!current.IsAdministrator) await development.RequireCreatorAsync(actor, token);
        return await registry.FindAsync(id, current.IsAdministrator ? null : actor.Subject, token)
            ?? throw new KeyNotFoundException("WORKSPACE_NOT_FOUND");
    }
    private void RequireRoot(WorkspaceDefinition workspace)
    {
        if (!policy.StorageRootIds.Contains(workspace.StorageRootId, StringComparer.Ordinal))
            throw new UnauthorizedAccessException("WORKSPACE_RETIREMENT_ROOT_REVOKED");
    }
}
