using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WhaleDeck.Application.Development;
using WhaleDeck.Application.Models;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;
using WhaleDeck.Infrastructure.Agent;

namespace WhaleDeck.Infrastructure.Persistence;

public sealed partial class WorkspaceRetirementRepository
{
    public Task<WorkspaceRetirementView> RecoverAsync(TerminalActor actor, Guid workspaceId, Guid retirementId, Guid key,
        WorkspaceRetirementRecoveryConfirmation input, CancellationToken token) => Transaction(async () =>
    {
        var current = await LockWorkspace(workspaceId, token); var row = await LockIntent(workspaceId, retirementId, token);
        var job = await Job(row, token);
        if (key == Guid.Empty || !input.ContinueDeletionAndReleaseBudget)
            throw new ArgumentException("WORKSPACE_RETIREMENT_RECOVERY_CONFIRMATION_INVALID");
        var replay = await db.WorkspaceRetirementRecoveries.AsNoTracking().SingleOrDefaultAsync(r => r.RetirementId == retirementId && r.IdempotencyKey == key, token);
        if (replay is not null)
        {
            if (replay.ActorSubject != actor.Subject || replay.PreviousStepId != input.PreviousTaskId || replay.PlanSha256 != input.PlanSha256)
                throw new InvalidOperationException("WORKSPACE_RETIREMENT_RECOVERY_KEY_CONFLICT");
            return View(row, job);
        }
        var snapshot = RequireFence(current, row); RequireNoLease(row);
        if (row.FinishedAtUtc is not null || row.Phase != "execute" || !row.AttentionRequired || row.Lease is not null ||
            row.ExecuteStepId != input.PreviousTaskId || row.ExecutionAttempt >= 1024 || row.PlanJson is null ||
            Property(row.PlanJson, "retirementPlanSha256") != input.PlanSha256 || row.ExecutionJson is not null)
            throw new InvalidOperationException("WORKSPACE_RETIREMENT_RECOVERY_STATE_CHANGED");
        var previous = await Step(row, token); var task = ReadTask(row, previous); ValidateRequest(snapshot, task, previous);
        var proof = previous.ProofJson is null ? null : JsonSerializer.Deserialize<WorkspaceRetirementTaskProof>(previous.ProofJson, Json);
        var request = AgentGateway.BuildRetirementRequest(actor, snapshot, task);
        if (previous.SettledAtUtc is null || proof is null || proof.ResultJson is not null || proof.Status is not ("Interrupted" or "Stopped" or "Rejected") ||
            !DevelopmentRetirementBinding.Matches(request, new WorkspaceRetirementReceipt { TaskId = proof.TaskId.ToString("D"),
                RequestSha256 = proof.RequestSha256, Status = proof.Status, Code = proof.Code ?? "" }))
            throw new InvalidOperationException("WORKSPACE_RETIREMENT_RECOVERY_PROOF_INVALID");
        _ = await Evidence(row, token);
        var now = Now(); row.ExecutionAttempt++; AddStep(row);
        db.WorkspaceRetirementRecoveries.Add(new() { Id = row.ExecuteStepId!.Value, RetirementId = row.Id,
            PreviousStepId = previous.Id, IdempotencyKey = key, Attempt = row.ExecutionAttempt, ActorSubject = actor.Subject,
            LoginBinding = actor.LoginBinding, PreviousProofSha256 = ProofHash(previous.ProofJson!), PlanSha256 = input.PlanSha256, ConfirmedAtUtc = now });
        row.AttentionRequired = false; row.AuthorizationRequired = false; row.CancelRequested = false; row.AvailableAtUtc = now;
        row.ExecutionSubject = actor.Subject; row.LoginBinding = actor.LoginBinding;
        job.Phase = "Execute"; job.State = "Running"; job.ErrorCode = null; job.CancelRequestedAtUtc = null; job.Version++;
        Event(job, "WORKSPACE_RETIREMENT_RECOVERY_CONFIRMED", now); Audit(actor, row, "confirm-recovery-delete-source", now);
        return View(row, job);
    }, token);

    public async Task<IReadOnlyList<WorkspaceRetirementAttemptView>> AttemptsAsync(Guid workspaceId, Guid retirementId, CancellationToken token)
    {
        var steps = await (from step in db.WorkspaceRetirementSteps.AsNoTracking()
            join row in db.WorkspaceRetirementWorkflows.AsNoTracking() on step.RetirementId equals row.Id
            where row.WorkspaceId == workspaceId && row.Id == retirementId && step.Phase == "execute"
            orderby step.Attempt select step).ToArrayAsync(token);
        var recoveries = await db.WorkspaceRetirementRecoveries.AsNoTracking().Where(r => r.RetirementId == retirementId).ToDictionaryAsync(r => r.Id, token);
        return steps.Select(step =>
        {
            var proof = step.ProofJson is null ? null : JsonSerializer.Deserialize<WorkspaceRetirementTaskProof>(step.ProofJson, Json);
            recoveries.TryGetValue(step.Id, out var recovery);
            return new WorkspaceRetirementAttemptView(step.Id, step.Attempt, recovery?.PreviousStepId, proof?.Status ?? "Pending",
                proof?.Code, recovery?.ConfirmedAtUtc, step.DispatchedAtUtc, step.SettledAtUtc, step.ProofJson is null ? null : ProofHash(step.ProofJson));
        }).ToArray();
    }

    // Npgsql reads the immutable jsonb proof in PostgreSQL's text form; the
    // database guard independently hashes that same representation.
    private static string ProofHash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
