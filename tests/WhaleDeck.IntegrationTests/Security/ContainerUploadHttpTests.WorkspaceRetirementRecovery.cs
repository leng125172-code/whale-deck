using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using WhaleDeck.Application.Development;
using WhaleDeck.Application.Models;
using WhaleDeck.Infrastructure.Persistence;

namespace WhaleDeck.IntegrationTests.Security;

public sealed partial class ContainerUploadHttpTests
{
    private static readonly string[] RecoveryStatuses = ["Interrupted", "Stopped", "Succeeded"];
    private static readonly int[] RecoveryAttempts = [0, 1, 2];
    private static async Task RecoverInterruptedRetirementInPostgres(DbContextOptions<PlatformDbContext> options, WorkspaceDefinition workspace,
        WorkspaceRetirementView interrupted, WorkspaceRetirementClaim destructive, string plan, TerminalActor actor, RetirementPreparationClock clock)
    {
        await using var inspect = new PlatformDbContext(options);
        var digest = ReadDigest(plan, "retirementPlanSha256");
        var oldProof = await inspect.WorkspaceRetirementSteps.AsNoTracking().SingleAsync(s => s.Id == destructive.Task.TaskId);
        var originalConfirmation = (await Get()).ConfirmedAtUtc;
        var input = new WorkspaceRetirementRecoveryConfirmation(destructive.Task.TaskId, digest, true); var key = Guid.NewGuid();
        await SqlFence($"UPDATE development_workspace_retirement_workflows SET attention_required=false WHERE id='{interrupted.RetirementId:D}'", "ck_workspace_retire_recovery");
        await Assert.ThrowsAsync<InvalidOperationException>(() => Recover(key, input with { PreviousTaskId = Guid.NewGuid() }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Recover(key, input with { PlanSha256 = new string('0', 64) }));
        await ForgedRecoveryIsRejected();
        await inspect.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION qa_recovery_audit_fault() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN IF NEW.action='development.workspace.retirement.confirm-recovery-delete-source' THEN RAISE EXCEPTION 'qa_recovery_audit_fault'; END IF; RETURN NEW; END $$;
            CREATE TRIGGER qa_recovery_audit_fault BEFORE INSERT ON audit_events FOR EACH ROW EXECUTE FUNCTION qa_recovery_audit_fault();
            """);
        try
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => Recover(key, input));
            Assert.True((await Get()).AttentionRequired);
            Assert.Equal(0, await inspect.WorkspaceRetirementRecoveries.CountAsync(r => r.RetirementId == interrupted.RetirementId));
        }
        finally { await inspect.Database.ExecuteSqlRawAsync("DROP TRIGGER qa_recovery_audit_fault ON audit_events; DROP FUNCTION qa_recovery_audit_fault()"); }
        var attempts = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Recover(key, input)));
        Assert.Single(attempts.Select(r => r.ExecuteTaskId).Distinct());
        var resuming = attempts[0]; Assert.Equal(1, resuming.ExecutionAttempt);
        Assert.False(resuming.AttentionRequired || resuming.CancelRequested || resuming.AuthorizationRequired || resuming.BudgetReleased);
        Assert.NotEqual(destructive.Task.TaskId, resuming.ExecuteTaskId); Assert.Equal(originalConfirmation, resuming.ConfirmedAtUtc);
        Assert.Equal(1, await inspect.WorkspaceRetirementRecoveries.CountAsync(r => r.RetirementId == interrupted.RetirementId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Recover(key, input with { PreviousTaskId = Guid.NewGuid() }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Recover(Guid.NewGuid(), input));
        await Write(s => s.RecordAsync(destructive, Proof(destructive, Execution(destructive, plan)), default));
        Assert.Equal(0, await Completions()); Assert.Equal("Retiring", await State());

        // An undispatched recovery still follows a partially destructive
        // attempt. Only a host Cancel tombstone settles this new task.
        await Store(s => s.CancelAsync(actor, workspace.Id, interrupted.RetirementId, default));
        Assert.Equal("Retiring", await State());
        var canceled = (await Store(s => s.ClaimAsync(default)))!;
        Assert.Equal(resuming.ExecuteTaskId, canceled.Task.TaskId); Assert.True(canceled.View.CancelRequested);
        await Write(s => s.RecordAsync(canceled, Proof(canceled, null, "Stopped"), default));
        Assert.True((await Get()).AttentionRequired); Assert.Equal(0, await Completions());
        var secondInput = input with { PreviousTaskId = canceled.Task.TaskId }; var secondKey = Guid.NewGuid();
        var second = await Recover(secondKey, secondInput); Assert.Equal(2, second.ExecutionAttempt);
        var resumed = (await Store(s => s.ClaimAsync(default)))!;
        Assert.Equal(second.ExecuteTaskId, resumed.Task.TaskId); Assert.True(await Store(s => s.MarkDispatchAsync(resumed, default)));
        await Assert.ThrowsAsync<WorkspaceRetirementException>(() => Write(s => s.RecordAsync(resumed, Proof(destructive, Execution(destructive, plan)), default)));
        await Write(s => s.RecordAsync(canceled, Proof(canceled, null, "Stopped"), default));
        clock.Advance(91); var observer = (await Store(s => s.ClaimAsync(default)))!;
        Assert.Equal(resumed.Task.TaskId, observer.Task.TaskId); Assert.False(await Store(s => s.MarkDispatchAsync(observer, default)));
        Assert.Equal(Execution(destructive, plan), Execution(observer, plan));
        await Write(s => s.RecordAsync(observer, Proof(observer, Execution(observer, plan)), default));
        Assert.True((await Get()).BudgetReleased); Assert.Equal("Retired", await State()); Assert.Equal(1, await Completions());
        Assert.Equal(second.ExecuteTaskId, (await Recover(secondKey, secondInput)).ExecuteTaskId);
        var history = await Store(s => s.AttemptsAsync(workspace.Id, interrupted.RetirementId, default));
        Assert.Equal(RecoveryStatuses, history.Select(h => h.Status));
        Assert.Equal(RecoveryAttempts, history.Select(h => h.Attempt));
        Assert.Equal(destructive.Task.TaskId, history[1].PreviousTaskId); Assert.Equal(canceled.Task.TaskId, history[2].PreviousTaskId);
        var retained = await inspect.WorkspaceRetirementSteps.AsNoTracking().SingleAsync(s => s.Id == destructive.Task.TaskId);
        Assert.Equal(oldProof.ProofJson, retained.ProofJson); Assert.Equal(oldProof.RequestJson, retained.RequestJson);
        Assert.Equal(oldProof.DispatchedAtUtc, retained.DispatchedAtUtc); Assert.Equal(oldProof.SettledAtUtc, retained.SettledAtUtc);
        await SqlFence($"DELETE FROM development_workspace_retirement_recoveries WHERE retirement_id='{interrupted.RetirementId:D}'", "ck_workspace_retire_history");
        await SqlFence($"UPDATE development_workspace_retirement_recoveries SET previous_proof_sha256=repeat('0',64) WHERE retirement_id='{interrupted.RetirementId:D}'", "ck_workspace_retire_history");
        await SqlFence($"UPDATE development_workspace_retirement_steps SET attempt=99 WHERE id='{destructive.Task.TaskId:D}'", "ck_workspace_retire_recovery");
        var down = await Assert.ThrowsAsync<PostgresException>(() => inspect.GetService<IMigrator>().MigrateAsync("20261110202208_WorkspaceRetirementExecution"));
        Assert.Equal("workspace_retirement_recovery_history_present", down.MessageText);
        Assert.Equal(1, await inspect.AuditEvents.CountAsync(a => a.JobId == interrupted.RetirementId && a.Action == "development.workspace.retirement.budget-released"));
        Console.WriteLine("INBOX_PHASE=workspace-retirement-recovery-postgres-new-task-immutable-proof-cancel-fence-replay-lease-budget");

        Task<WorkspaceRetirementView> Recover(Guid requestKey, WorkspaceRetirementRecoveryConfirmation value) => Store(s => s.RecoverAsync(actor, workspace.Id, interrupted.RetirementId, requestKey, value, default));
        async Task<WorkspaceRetirementView> Get() => (await Store(s => s.GetAsync(workspace.Id, interrupted.RetirementId, default)))!;
        Task<int> Completions() => inspect.WorkspaceRetirementCompletions.CountAsync(c => c.WorkspaceId == workspace.Id);
        Task<string> State() => inspect.DevelopmentWorkspaces.AsNoTracking().Where(w => w.Id == workspace.Id).Select(w => w.LifecycleState).SingleAsync();
        async Task<T> Store<T>(Func<WorkspaceRetirementRepository, Task<T>> action)
        { await using var db = new PlatformDbContext(options); return await action(new(db, clock, new WorkspaceLifecycleRepository(db, clock))); }
        async Task Write(Func<WorkspaceRetirementRepository, Task> action)
        { await using var db = new PlatformDbContext(options); await action(new(db, clock, new WorkspaceLifecycleRepository(db, clock))); }
        async Task SqlFence(string sql, string expected)
        { var error = await Assert.ThrowsAsync<PostgresException>(() => inspect.Database.ExecuteSqlRawAsync(sql)); Assert.Equal(expected, error.ConstraintName); }
        async Task ForgedRecoveryIsRejected()
        {
            await using var db = new PlatformDbContext(options);
            await using var transaction = await db.Database.BeginTransactionAsync();
            var forged = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO development_workspace_retirement_steps (id,retirement_id,phase,attempt,request_json)
                    SELECT {forged}, retirement_id, 'execute', 1, replace(request_json,{destructive.Task.TaskId.ToString("D")},{forged.ToString("D")})
                    FROM development_workspace_retirement_steps WHERE id={destructive.Task.TaskId};
                INSERT INTO development_workspace_retirement_recoveries (id,retirement_id,previous_step_id,idempotency_key,attempt,actor_subject,login_binding,previous_proof_sha256,plan_sha256,confirmed_at_utc)
                    VALUES ({forged},{interrupted.RetirementId},{destructive.Task.TaskId},{Guid.NewGuid()},1,{actor.Subject},{actor.LoginBinding},repeat('0',64),{digest},{clock.GetUtcNow()});
                UPDATE development_workspace_retirement_workflows SET execute_step_id={forged},execution_attempt=1,attention_required=false,cancel_requested=false,authorization_required=false
                    WHERE id={interrupted.RetirementId};
                """);
            var failure = await Assert.ThrowsAsync<PostgresException>(() => transaction.CommitAsync());
            Assert.Equal("ck_workspace_retire_recovery", failure.ConstraintName);
        }
    }
}
