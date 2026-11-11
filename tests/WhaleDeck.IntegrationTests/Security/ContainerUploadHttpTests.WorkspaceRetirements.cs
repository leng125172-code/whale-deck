using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using WhaleDeck.Application.Development;
using WhaleDeck.Application.Models;
using WhaleDeck.Contracts;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Agent;
using WhaleDeck.Infrastructure.Persistence;

namespace WhaleDeck.IntegrationTests.Security;

public sealed partial class ContainerUploadHttpTests
{
    // Actual PostgreSQL migrations, constraints, concurrent transactions and
    // audit rollback. Host receipts are synthetic; this does not delete any
    // installed source or prove physical HDD/quota retirement.
    internal static async Task ExecuteWorkspaceRetirementInPostgres(DbContextOptions<PlatformDbContext> options)
    {
        await using var inspect = new PlatformDbContext(options);
        await inspect.Database.MigrateAsync();
        Assert.False(inspect.Database.HasPendingModelChanges());
        var original = await inspect.DevelopmentWorkspaces.AsNoTracking().FirstAsync(w => w.Slug.StartsWith("archive-") && w.LifecycleState == "Stopped");
        var workspace = (await new DevelopmentWorkspaceRegistry(inspect).FindAsync(original.Id, null, default))!;
        var archive = await (from a in inspect.WorkspaceArchiveIntents.AsNoTracking()
            join job in inspect.OperationJobs.AsNoTracking() on a.Id equals job.Id
            where a.WorkspaceId == workspace.Id && a.ExpectedEpoch == workspace.LifecycleVersion && a.Phase == "verify" &&
                a.VerifyJson != null && a.FinishedAtUtc != null && !a.CancelRequested && a.Lease == null && a.LeaseUntilUtc == null &&
                job.JobType == WorkspaceArchives.JobType && job.State == "Succeeded" && job.CompletedAtUtc != null
            orderby a.FinishedAtUtc descending select a).FirstAsync();
        var actor = new TerminalActor(workspace.OwnerSubject, null, [], new string('a', 64), "retirement-workflow-test", null);
        var clock = new RetirementPreparationClock();
        var plan = CanonicalRetirement(RetirementPlan(archive.VerifyJson!));
        var digest = ReadDigest(plan, "retirementPlanSha256");
        var reports = await inspect.WorkspaceRunReports.CountAsync();
        var sourceRows = await inspect.WorkspaceSourceIntents.CountAsync();
        var archiveSteps = await inspect.WorkspaceArchiveSteps.CountAsync();
        var totals = await inspect.DevelopmentWorkspaces.GroupBy(_ => 1).Select(rows => new WorkspaceBudget(
            rows.Sum(w => w.CpuMillis), rows.Sum(w => w.MemoryMiB), rows.Sum(w => w.DiskMiB))).SingleAsync();
        var replacementId = Guid.NewGuid();
        var replacement = new WorkspaceDefinition(replacementId, "retired-budget-" + replacementId.ToString("N")[..16], workspace.OwnerSubject,
            workspace.StorageRootId, WorkspaceStorageAllocation.Registered, 1, workspace.Budget, workspace.Environment, workspace.EnvironmentSha256, clock.GetUtcNow());
        var capacityError = await Assert.ThrowsAsync<InvalidOperationException>(() => Register(replacement));
        Assert.Equal("WORKSPACE_REGISTRATION_CAPACITY", capacityError.Message);

        // Canceling before any dispatch must restore Stopped, never release capacity.
        var canceled = await Begin(Guid.NewGuid());
        await Store(s => s.CancelAsync(actor, workspace.Id, canceled.RetirementId, default));
        Assert.Equal("Canceled", (await Get(canceled)).State);
        Assert.Equal("Stopped", await State()); Assert.Equal(0, await Completions());
        Assert.Null(await Store(s => s.ClaimAsync(default)));

        var key = Guid.NewGuid();
        var starts = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Begin(key)));
        Assert.Single(starts.Select(v => v.RetirementId).Distinct());
        var expires = starts[0];
        await SqlFence($"INSERT INTO development_workspace_lifecycle (id,workspace_id) VALUES ('{Guid.NewGuid():D}','{workspace.Id:D}')", "ck_workspace_retire_lifecycle");
        await SqlFence($"UPDATE development_workspaces SET disk_mi_b=disk_mi_b+256 WHERE id='{workspace.Id:D}'", "ck_workspace_retirement_fence");
        var first = (await Store(s => s.ClaimAsync(default)))!; Assert.Equal("plan", first.Task.Phase);
        Assert.True(await Store(s => s.MarkDispatchAsync(first, default)));
        clock.Advance(91);
        var recovered = (await Store(s => s.ClaimAsync(default)))!;
        Assert.Equal(first.Task, recovered.Task); Assert.NotEqual(first.Lease, recovered.Lease);
        Assert.False(await Store(s => s.MarkDispatchAsync(recovered, default)));
        await Write(s => s.RecordAsync(first, Proof(first, plan), default));
        Assert.Null((await Get(expires)).PlanJson);
        await Assert.ThrowsAsync<WorkspaceRetirementException>(() => Write(s => s.RecordAsync(recovered, Proof(recovered, plan) with { TaskId = Guid.NewGuid() }, default)));
        await Write(s => s.RecordAsync(recovered, Proof(recovered, plan), default));
        await SqlFence($"UPDATE development_workspace_retirement_steps SET request_json=request_json||' ' WHERE id='{recovered.Task.TaskId:D}'", "ck_workspace_retire_immutable");
        await SqlFence($"UPDATE development_workspace_retirement_workflows SET plan_expires_at_utc=plan_expires_at_utc+interval '1 hour' WHERE id='{expires.RetirementId:D}'", "ck_workspace_retire_immutable");
        clock.Advance(601);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Store(s => s.ConfirmAsync(actor, workspace.Id, expires.RetirementId, digest, default)));
        await Store(s => s.CancelAsync(actor, workspace.Id, expires.RetirementId, default));
        Assert.Equal("Stopped", await State()); Assert.Equal(0, await Completions());

        // Clearing a dispatch fence is allowed only before any host observation.
        var revoked = await Begin(Guid.NewGuid()); var beforeRpc = (await Store(s => s.ClaimAsync(default)))!;
        Assert.True(await Store(s => s.MarkDispatchAsync(beforeRpc, default)));
        await Write(s => s.DeferUndispatchedAsync(beforeRpc, "WORKSPACE_RETIREMENT_UNSUPPORTED", default)); clock.Advance(4);
        var preflightRetry = (await Store(s => s.ClaimAsync(default)))!;
        Assert.Equal(beforeRpc.Task.TaskId, preflightRetry.Task.TaskId); Assert.True(await Store(s => s.MarkDispatchAsync(preflightRetry, default)));
        await Write(s => s.RecordAsync(preflightRetry, Proof(preflightRetry, null, "Running"), default)); clock.Advance(4);
        var observed = (await Store(s => s.ClaimAsync(default)))!;
        await Write(s => s.DeferUndispatchedAsync(observed, "WORKSPACE_RETIREMENT_RETRYABLE", default));
        Assert.NotNull((await inspect.WorkspaceRetirementSteps.AsNoTracking().SingleAsync(s => s.Id == observed.Task.TaskId)).DispatchedAtUtc);
        Assert.True(await Store(s => s.AuthorizationLostAsync(observed, default)));
        await Write(s => s.RecordAsync(observed, Proof(observed, plan), default));
        Assert.Equal("Canceled", (await Get(revoked)).State); Assert.Equal("Stopped", await State()); Assert.Equal(0, await Completions());

        var execution = await Begin(Guid.NewGuid()); var planned = (await Store(s => s.ClaimAsync(default)))!;
        Assert.True(await Store(s => s.MarkDispatchAsync(planned, default)));
        await Write(s => s.RecordAsync(planned, Proof(planned, plan), default));
        var confirmations = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Store(s => s.ConfirmAsync(actor, workspace.Id, execution.RetirementId, digest, default))));
        Assert.Equal(confirmations[0].ConfirmedAtUtc, confirmations[1].ConfirmedAtUtc);
        Assert.Equal(2, await inspect.WorkspaceRetirementSteps.CountAsync(s => s.RetirementId == execution.RetirementId));
        var executing = (await Store(s => s.ClaimAsync(default)))!; Assert.Equal("execute", executing.Task.Phase);
        await SqlFence($"INSERT INTO development_workspace_retirement_completions (workspace_id,retirement_id,execute_step_id,completed_at_utc) VALUES ('{workspace.Id:D}','{execution.RetirementId:D}','{executing.Task.TaskId:D}',now())", "ck_workspace_retirement_completion");
        var success = Execution(executing, plan);
        await Assert.ThrowsAsync<WorkspaceRetirementException>(() => Write(s => s.RecordAsync(executing, Proof(executing, success), default)));
        Assert.True(await Store(s => s.MarkDispatchAsync(executing, default)));
        var wrong = JsonNode.Parse(success)!.AsObject(); wrong["sourceManifestSha256"] = new string('f', 64);
        await Assert.ThrowsAsync<WorkspaceRetirementException>(() => Write(s => s.RecordAsync(executing, Proof(executing, CanonicalRetirement(wrong.ToJsonString())), default)));
        Assert.Equal(0, await Completions()); Assert.Equal("Retiring", await State());

        // A lost Start reply/lease never admits a new execute task. A late
        // cancel or revoked session cannot erase a real successful receipt.
        clock.Advance(91); var observer = (await Store(s => s.ClaimAsync(default)))!;
        Assert.Equal(executing.Task, observer.Task); Assert.False(await Store(s => s.MarkDispatchAsync(observer, default)));
        await Write(s => s.RecordAsync(executing, Proof(executing, success), default)); Assert.Equal(0, await Completions());
        await Store(s => s.CancelAsync(actor, workspace.Id, execution.RetirementId, default));
        Assert.True(await Store(s => s.AuthorizationLostAsync(observer, default)));
        await inspect.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION qa_retirement_audit_fault() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN IF NEW.action='development.workspace.retirement.budget-released' THEN RAISE EXCEPTION 'qa_retirement_audit_fault'; END IF; RETURN NEW; END $$;
            CREATE TRIGGER qa_retirement_audit_fault BEFORE INSERT ON audit_events FOR EACH ROW EXECUTE FUNCTION qa_retirement_audit_fault();
            """);
        try
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => Write(s => s.RecordAsync(observer, Proof(observer, success), default)));
            Assert.Equal(0, await Completions()); Assert.Equal("Retiring", await State()); Assert.Null((await Get(execution)).CompletedAtUtc);
        }
        finally { await inspect.Database.ExecuteSqlRawAsync("DROP TRIGGER qa_retirement_audit_fault ON audit_events; DROP FUNCTION qa_retirement_audit_fault()"); }
        await Write(s => s.RecordAsync(observer, Proof(observer, success), default));
        var retired = await Get(execution); Assert.Equal("Succeeded", retired.State); Assert.Equal("Retired", retired.Phase);
        Assert.True(retired.BudgetReleased && retired.SourceRetired && retired.RetainsHdd); Assert.Equal(1, await Completions());
        var events = await inspect.OperationJobEvents.CountAsync(e => e.JobId == execution.RetirementId);
        await Write(s => s.RecordAsync(observer, Proof(observer, success), default));
        Assert.Equal(events, await inspect.OperationJobEvents.CountAsync(e => e.JobId == execution.RetirementId));
        Assert.Equal(1, await inspect.AuditEvents.CountAsync(a => a.JobId == execution.RetirementId && a.Action == "development.workspace.retirement.budget-released"));
        var replay = await Store(s => s.ConfirmAsync(actor, workspace.Id, execution.RetirementId, digest, default));
        Assert.Equal(retired.CompletedAtUtc, replay.CompletedAtUtc);

        await SqlFence($"UPDATE development_workspaces SET lifecycle_state='Active',lifecycle_version=lifecycle_version+1 WHERE id='{workspace.Id:D}'", "ck_workspace_retired_permanent");
        await SqlFence($"DELETE FROM development_workspaces WHERE id='{workspace.Id:D}'", "ck_workspace_retired_permanent");
        await SqlFence($"DELETE FROM development_workspace_retirement_completions WHERE workspace_id='{workspace.Id:D}'", "ck_workspace_retire_history");
        await SqlFence($"UPDATE development_workspace_retirement_completions SET completed_at_utc=now() WHERE workspace_id='{workspace.Id:D}'", "ck_workspace_retire_history");
        await SqlFence($"DELETE FROM development_workspace_retirement_steps WHERE id='{observer.Task.TaskId:D}'", "ck_workspace_retire_history");
        await SqlFence($"UPDATE operation_jobs SET result_json=jsonb_build_object() WHERE id='{execution.RetirementId:D}'", "ck_workspace_retirement_completion");
        await SqlFence($"INSERT INTO development_workspace_runs (id,workspace_id) VALUES ('{Guid.NewGuid():D}','{workspace.Id:D}')", "ck_workspace_lifecycle_admission");
        await SqlFence($"INSERT INTO development_workspace_source_imports (id,workspace_id) VALUES ('{Guid.NewGuid():D}','{workspace.Id:D}')", "ck_workspace_lifecycle_admission");
        await SqlFence($"INSERT INTO development_workspace_lifecycle (id,workspace_id) VALUES ('{Guid.NewGuid():D}','{workspace.Id:D}')", "ck_workspace_retire_lifecycle");
        await SqlFence($"INSERT INTO development_workspace_archives (id,workspace_id,expected_epoch) VALUES ('{Guid.NewGuid():D}','{workspace.Id:D}',{workspace.LifecycleVersion})", "ck_workspace_archive_admission");
        var historical = await inspect.DevelopmentWorkspaces.AsNoTracking().SingleAsync(w => w.Id == workspace.Id);
        Assert.Equal(original.EnvironmentJson, historical.EnvironmentJson); Assert.Equal(original.CpuMillis, historical.CpuMillis);
        Assert.Equal(original.MemoryMiB, historical.MemoryMiB); Assert.Equal(original.DiskMiB, historical.DiskMiB);
        Assert.Equal(original.Version, historical.Version); Assert.Equal(original.LifecycleVersion, historical.LifecycleVersion);
        Assert.Equal(reports, await inspect.WorkspaceRunReports.CountAsync()); Assert.Equal(sourceRows, await inspect.WorkspaceSourceIntents.CountAsync());
        Assert.Equal(archiveSteps, await inspect.WorkspaceArchiveSteps.CountAsync());
        Assert.False((await Register(replacement)).Replayed);
        var duplicate = replacement with { Id = Guid.NewGuid(), Slug = workspace.Slug };
        Assert.Equal("WORKSPACE_SLUG_CONFLICT", (await Assert.ThrowsAsync<InvalidOperationException>(() => Register(duplicate))).Message);
        var down = await Assert.ThrowsAsync<PostgresException>(() => inspect.GetService<IMigrator>().MigrateAsync("20261110171841_WorkspaceRetirementPreparation"));
        Assert.Equal("workspace_retirement_execution_history_present", down.MessageText);
        await inspect.Database.MigrateAsync();
        Assert.Equal(observer.Task.TaskId, (await Get(execution)).ExecuteTaskId);
        Assert.Equal(1, await Completions());

        // An interrupted destructive phase is not proof of an intact source.
        // A separate fixture remains fenced and charged for explicit recovery.
        var attentionId = Guid.NewGuid();
        inspect.DevelopmentWorkspaces.Add(new DevelopmentWorkspaceRecord
        {
            Id = attentionId, Slug = "retire-attention-" + attentionId.ToString("N")[..16], OwnerSubject = original.OwnerSubject, StorageRootId = original.StorageRootId,
            State = original.State, Version = original.Version, LifecycleState = "Stopped", LifecycleVersion = 2,
            CpuMillis = original.CpuMillis, MemoryMiB = original.MemoryMiB, DiskMiB = original.DiskMiB,
            EnvironmentJson = original.EnvironmentJson, EnvironmentSha256 = original.EnvironmentSha256,
            RegistrationKey = Guid.NewGuid(), RequestSha256 = original.RequestSha256, CreatedAtUtc = original.CreatedAtUtc,
            StoragePlanSha256 = original.StoragePlanSha256, StorageAllocationStartedAtUtc = original.StorageAllocationStartedAtUtc,
            StorageAllocatedAtUtc = original.StorageAllocatedAtUtc
        });
        await inspect.SaveChangesAsync(); inspect.ChangeTracker.Clear();
        workspace = (await new DevelopmentWorkspaceRegistry(inspect).FindAsync(attentionId, null, default))!;
        var host = new ArchiveHost(); var archiving = await Archive(s => s.BeginAsync(actor, workspace, Guid.NewGuid(), workspace.LifecycleVersion, default));
        foreach (var phase in new[] { "plan", "capture", "verify" })
        {
            var claim = (await Archive(s => s.ClaimAsync(default)))!; Assert.Equal(phase, claim.Task.Phase);
            Assert.True(await Archive(s => s.MarkDispatchAsync(claim, default)));
            var proof = await host.StartArchiveAsync(actor, workspace, claim.Task, default);
            await ArchiveWrite(s => s.RecordAsync(claim, proof, default));
            if (phase == "plan")
            {
                var ready = (await Archive(s => s.GetAsync(workspace.Id, archiving.ArchiveId, default)))!;
                await Archive(s => s.ConfirmAsync(actor, workspace.Id, archiving.ArchiveId, ReadDigest(ready.PlanJson!, "planSha256"), default));
            }
            clock.Advance(4);
        }
        archive = await inspect.WorkspaceArchiveIntents.AsNoTracking().SingleAsync(a => a.Id == archiving.ArchiveId);
        plan = CanonicalRetirement(RetirementPlan(archive.VerifyJson!)); digest = ReadDigest(plan, "retirementPlanSha256");
        var interrupted = await Begin(Guid.NewGuid()); var initial = (await Store(s => s.ClaimAsync(default)))!;
        Assert.True(await Store(s => s.MarkDispatchAsync(initial, default))); await Write(s => s.RecordAsync(initial, Proof(initial, plan), default));
        await Store(s => s.ConfirmAsync(actor, workspace.Id, interrupted.RetirementId, digest, default));
        var destructive = (await Store(s => s.ClaimAsync(default)))!; Assert.True(await Store(s => s.MarkDispatchAsync(destructive, default)));
        await Write(s => s.RecordAsync(destructive, Proof(destructive, null, "Interrupted"), default));
        var attention = await Get(interrupted); Assert.True(attention.AttentionRequired); Assert.False(attention.BudgetReleased);
        Assert.Null(attention.CompletedAtUtc); Assert.Equal("Retiring", await State()); Assert.Equal(0, await Completions());
        clock.Advance(91); Assert.Null(await Store(s => s.ClaimAsync(default)));
        Assert.True((await Store(s => s.ReauthorizeAsync(actor, workspace.Id, interrupted.RetirementId, default))).AttentionRequired);
        await Store(s => s.CancelAsync(actor, workspace.Id, interrupted.RetirementId, default)); Assert.Equal("Retiring", await State());
        await SqlFence($"UPDATE development_workspace_retirement_workflows SET finished_at_utc=now() WHERE id='{interrupted.RetirementId:D}'; UPDATE development_workspaces SET lifecycle_state='Stopped' WHERE id='{workspace.Id:D}'", "ck_workspace_retirement_completion");
        await RecoverInterruptedRetirementInPostgres(options, workspace, interrupted, destructive, plan, actor, clock);
        Console.WriteLine("INBOX_PHASE=workspace-retirement-execution-postgres-fixed-task-fence-proof-audit-rollback-tombstone-budget-history");

        Task<WorkspaceRetirementView> Begin(Guid key) => Store(s => s.BeginAsync(actor, workspace, key, archive.Id, workspace.LifecycleVersion, default));
        async Task<WorkspaceRetirementView> Get(WorkspaceRetirementView view) => (await Store(s => s.GetAsync(workspace.Id, view.RetirementId, default)))!;
        Task<int> Completions() => inspect.WorkspaceRetirementCompletions.CountAsync(c => c.WorkspaceId == workspace.Id);
        Task<string> State() => inspect.DevelopmentWorkspaces.AsNoTracking().Where(w => w.Id == workspace.Id).Select(w => w.LifecycleState).SingleAsync();
        async Task<T> Store<T>(Func<WorkspaceRetirementRepository, Task<T>> action)
        { await using var db = new PlatformDbContext(options); return await action(new(db, clock, new WorkspaceLifecycleRepository(db, clock))); }
        async Task Write(Func<WorkspaceRetirementRepository, Task> action)
        { await using var db = new PlatformDbContext(options); await action(new(db, clock, new WorkspaceLifecycleRepository(db, clock))); }
        async Task<T> Archive<T>(Func<WorkspaceArchiveRepository, Task<T>> action)
        { await using var db = new PlatformDbContext(options); return await action(new(db, clock, new WorkspaceLifecycleRepository(db, clock))); }
        async Task ArchiveWrite(Func<WorkspaceArchiveRepository, Task> action)
        { await using var db = new PlatformDbContext(options); await action(new(db, clock, new WorkspaceLifecycleRepository(db, clock))); }
        async Task<WorkspaceRegistrationReceipt> Register(WorkspaceDefinition value)
        { await using var db = new PlatformDbContext(options); return await new DevelopmentWorkspaceRegistry(db).RegisterAsync(actor, Guid.NewGuid(), workspace.EnvironmentSha256, value, totals, default); }
        async Task SqlFence(string sql, string expected)
        { var error = await Assert.ThrowsAsync<PostgresException>(() => inspect.Database.ExecuteSqlRawAsync(sql)); Assert.Equal(expected, error.ConstraintName); }
    }

    private static WorkspaceRetirementTaskProof Proof(WorkspaceRetirementClaim claim, string? result, string status = "Succeeded") => new(
        claim.Task.TaskId, DevelopmentRetirementBinding.HostHash(AgentGateway.BuildRetirementRequest(claim.Actor, claim.Workspace, claim.Task)), status, null, result);
    private static string CanonicalRetirement(string json)
    { using var value = JsonDocument.Parse(json); return Encoding.UTF8.GetString(DevelopmentRetirementPlanBinding.Canonical(value.RootElement)); }
    private static string Execution(WorkspaceRetirementClaim claim, string plan)
    {
        using var value = JsonDocument.Parse(plan);
        return CanonicalRetirement(JsonSerializer.Serialize(new
        {
            schemaVersion = 1, purpose = "whaledeck-development-source-retirement-receipt-v1",
            retirementId = claim.Task.RetirementId.ToString("D"), workspaceId = claim.Workspace.Id.ToString("D"),
            requestSha256 = DevelopmentRetirementBinding.EngineHash(AgentGateway.BuildRetirementRequest(claim.Actor, claim.Workspace, claim.Task)),
            retirementPlanSha256 = claim.Task.RetirementPlanSha256,
            storageProofSha256 = value.RootElement.GetProperty("storageProofSha256").GetString(),
            sourceManifestSha256 = value.RootElement.GetProperty("sourceManifestSha256").GetString(),
            captureReceiptSha256 = value.RootElement.GetProperty("captureReceiptSha256").GetString(),
            projectId = 1, sourceRemoved = true, quotaReleased = true, claimReleased = true, retainsHdd = true, releasesApplicationBudget = false
        }));
    }
}
