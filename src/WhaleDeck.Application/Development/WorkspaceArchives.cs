using System.Text.Json.Serialization;
using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Development;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WorkspaceArchiveBegin(long ExpectedEpoch);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WorkspaceArchiveConfirmation(string PlanSha256);
public sealed record WorkspaceArchiveView(Guid ArchiveId, Guid WorkspaceId, string State, string Phase, string? Code,
    bool AwaitingConfirmation, bool AuthorizationRequired, bool CancelRequested, string? PlanJson, string? CaptureJson,
    string? VerifyJson, DateTimeOffset? CompletedAtUtc)
{
    public bool RetainsSource { get; } = true;
    public bool ReservesBudget { get; } = true;
}
public sealed record WorkspaceArchiveClaim(WorkspaceArchiveView View, WorkspaceDefinition Workspace,
    WorkspaceArchiveTask Task, TerminalActor Actor, Guid Lease);
public interface IWorkspaceArchiveStore
{
    Task<WorkspaceArchiveView> BeginAsync(TerminalActor actor, WorkspaceDefinition workspace, Guid key, long epoch, CancellationToken token);
    Task<WorkspaceArchiveView?> CurrentAsync(Guid workspaceId, CancellationToken token);
    Task<WorkspaceArchiveView?> GetAsync(Guid workspaceId, Guid archiveId, CancellationToken token);
    Task<WorkspaceArchiveView> ConfirmAsync(TerminalActor actor, Guid workspaceId, Guid archiveId, string planSha256, CancellationToken token);
    Task<WorkspaceArchiveView> CancelAsync(TerminalActor actor, Guid workspaceId, Guid archiveId, CancellationToken token);
    Task<WorkspaceArchiveView> ReauthorizeAsync(TerminalActor actor, Guid workspaceId, Guid archiveId, CancellationToken token);
    Task<WorkspaceArchiveClaim?> ClaimAsync(CancellationToken token);
    Task<bool> MarkDispatchAsync(WorkspaceArchiveClaim claim, CancellationToken token);
    Task RecordAsync(WorkspaceArchiveClaim claim, WorkspaceArchiveTaskProof proof, CancellationToken token);
    Task<bool> AuthorizationLostAsync(WorkspaceArchiveClaim claim, CancellationToken token);
    Task DeferAsync(WorkspaceArchiveClaim claim, string code, CancellationToken token);
    Task DeferUndispatchedAsync(WorkspaceArchiveClaim claim, string code, CancellationToken token);
}

public sealed class WorkspaceArchives(WorkspaceReportAuthorization authorization, DevelopmentWorkspaces development,
    IDevelopmentWorkspaceRegistry registry, WorkspaceRegistrationPolicy policy, IWorkspaceArchiveStore store)
{
    public const string JobType = "development.workspace.archive";
    public async Task<WorkspaceArchiveView> BeginAsync(TerminalActor actor, Guid workspaceId, Guid key, WorkspaceArchiveBegin input, CancellationToken token)
    {
        if (key == Guid.Empty || input.ExpectedEpoch < 2 || input.ExpectedEpoch % 2 != 0)
            throw new ArgumentException("WORKSPACE_ARCHIVE_REQUEST_INVALID");
        var workspace = await RequireAsync(actor, workspaceId, token);
        RequireRoot(workspace);
        return await store.BeginAsync(actor, workspace, key, input.ExpectedEpoch, token);
    }
    public async Task<WorkspaceArchiveView?> CurrentAsync(TerminalActor actor, Guid id, CancellationToken token)
    {
        _ = await RequireAsync(actor, id, token);
        return await store.CurrentAsync(id, token);
    }
    public async Task<WorkspaceArchiveView> GetAsync(TerminalActor actor, Guid id, Guid archiveId, CancellationToken token)
    {
        _ = await RequireAsync(actor, id, token);
        return await store.GetAsync(id, archiveId, token) ?? throw new KeyNotFoundException("WORKSPACE_ARCHIVE_NOT_FOUND");
    }
    public async Task<WorkspaceArchiveView> ConfirmAsync(TerminalActor actor, Guid id, Guid archiveId, WorkspaceArchiveConfirmation input, CancellationToken token)
    {
        RequireRoot(await RequireAsync(actor, id, token));
        if (input.PlanSha256 is not { Length: 64 } || input.PlanSha256.Any(c => c is not (>= 'a' and <= 'f' or >= '0' and <= '9')))
            throw new ArgumentException("WORKSPACE_ARCHIVE_CONFIRMATION_INVALID");
        return await store.ConfirmAsync(actor, id, archiveId, input.PlanSha256, token);
    }
    public async Task<WorkspaceArchiveView> CancelAsync(TerminalActor actor, Guid id, Guid archiveId, CancellationToken token)
    {
        _ = await RequireAsync(actor, id, token);
        return await store.CancelAsync(actor, id, archiveId, token);
    }
    public async Task<WorkspaceArchiveView> ReauthorizeAsync(TerminalActor actor, Guid id, Guid archiveId, CancellationToken token)
    {
        RequireRoot(await RequireAsync(actor, id, token));
        return await store.ReauthorizeAsync(actor, id, archiveId, token);
    }
    internal async Task RequireExecutionAsync(WorkspaceArchiveClaim claim, CancellationToken token)
    {
        var current = await RequireAsync(claim.Actor, claim.Workspace.Id, token);
        RequireRoot(current);
        if (current.State != WorkspaceStorageAllocation.Allocated || current.LifecycleState != WorkspaceLifecycle.Stopped ||
            current.LifecycleVersion != claim.Task.ExpectedEpoch || current.EnvironmentSha256 != claim.Workspace.EnvironmentSha256)
            throw new UnauthorizedAccessException("WORKSPACE_ARCHIVE_BINDING_REVOKED");
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
            throw new UnauthorizedAccessException("WORKSPACE_ARCHIVE_ROOT_REVOKED");
    }
}
