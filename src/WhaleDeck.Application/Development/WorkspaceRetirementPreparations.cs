using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Development;

public sealed record WorkspaceRetirementPreparationView(Guid RetirementId, Guid WorkspaceId, Guid ArchiveId,
    string State, string Phase, string? Code, string? PlanJson, DateTimeOffset? PlanExpiresAtUtc,
    DateTimeOffset? ConfirmedAtUtc, bool CancelRequested, DateTimeOffset? FinishedAtUtc)
{
    public bool RetainsSource { get; } = true;
    public bool ReservesBudget { get; } = true;
    public bool ExecutionAvailable { get; }
}
public sealed record WorkspaceRetirementPreparationClaim(WorkspaceRetirementPreparationView View,
    WorkspaceDefinition Workspace, TerminalActor Actor, Guid Lease);

// Internal application boundary until signed host execution and user workflow
// are implemented. Callers must resolve current identity and approved roots;
// raw receipt/plan JSON is never supplied by a controller or browser.
public interface IWorkspaceRetirementPreparationStore
{
    Task<WorkspaceRetirementPreparationView> BeginAsync(TerminalActor actor, WorkspaceDefinition workspace,
        Guid archiveId, Guid key, CancellationToken token);
    Task<WorkspaceRetirementPreparationView?> GetAsync(Guid workspaceId, Guid retirementId, CancellationToken token);
    Task<WorkspaceRetirementPreparationClaim?> ClaimAsync(CancellationToken token);
    Task RecordPlanAsync(WorkspaceRetirementPreparationClaim claim, string hostPlanJson, CancellationToken token);
    Task<WorkspaceRetirementPreparationView> AcknowledgeAsync(TerminalActor actor, Guid workspaceId, Guid retirementId,
        string planSha256, CancellationToken token);
    Task<WorkspaceRetirementPreparationView> CancelAsync(TerminalActor actor, Guid workspaceId, Guid retirementId, CancellationToken token);
}

public static class WorkspaceRetirementPreparations
{
    public const string JobType = "development.workspace.retirement.prepare";
    public const string Retiring = "Retiring";
}
