using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WhaleDeck.Application.Development;
using WhaleDeck.Application.Models;
using WhaleDeck.Contracts;
using WhaleDeck.Infrastructure.Persistence;

namespace WhaleDeck.IntegrationTests.Security;

public sealed partial class ContainerUploadHttpTests
{
    // Real migration/transactions/SQL fences. Host archive and retirement-plan
    // content is explicitly synthetic; no Agent, real source or HDD is touched.
    internal static async Task PrepareWorkspaceRetirementInPostgres(DbContextOptions<PlatformDbContext> options)
    {
        await using var inspect = new PlatformDbContext(options);
        var row = await inspect.DevelopmentWorkspaces.AsNoTracking().FirstAsync(w => w.Slug.StartsWith("archive-") && w.LifecycleState == "Stopped");
        var workspace = (await new DevelopmentWorkspaceRegistry(inspect).FindAsync(row.Id, null, default))!;
        var clock = new WorkspaceRunClock(); var actor = new TerminalActor(workspace.OwnerSubject, null, [], new string('a', 64), "retirement-prepare-test", null);
        var host = new ArchiveHost();
        var queued = await Archive(s => s.BeginAsync(actor, workspace, Guid.NewGuid(), workspace.LifecycleVersion, default));
        foreach (var phase in new[] { "plan", "capture", "verify" })
        {
            var claim = (await Archive(s => s.ClaimAsync(default)))!; Assert.Equal(phase, claim.Task.Phase);
            Assert.True(await Archive(s => s.MarkDispatchAsync(claim, default)));
            var proof = await host.StartArchiveAsync(actor, workspace, claim.Task, default);
            await ArchiveWrite(s => s.RecordAsync(claim, proof, default));
            if (phase == "plan")
            {
                var ready = (await Archive(s => s.GetAsync(workspace.Id, queued.ArchiveId, default)))!;
                await Archive(s => s.ConfirmAsync(actor, workspace.Id, queued.ArchiveId, ReadDigest(ready.PlanJson!, "planSha256"), default));
            }
            clock.Advance(4);
        }
        var archived = (await Archive(s => s.GetAsync(workspace.Id, queued.ArchiveId, default)))!; Assert.Equal("Succeeded", archived.State);
        var plan = RetirementPlan(archived.VerifyJson!);
        var totals = await Totals(); var originalArchiveRows = await inspect.WorkspaceArchiveSteps.AsNoTracking().CountAsync();
        var unknown = await Assert.ThrowsAsync<InvalidOperationException>(() => Store(s => s.BeginAsync(actor, workspace, Guid.NewGuid(), Guid.NewGuid(), default)));
        Assert.Equal("WORKSPACE_RETIREMENT_ARCHIVE_NOT_VERIFIED", unknown.Message);
        var key = Guid.NewGuid();
        var starts = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Store(s => s.BeginAsync(actor, workspace, queued.ArchiveId, key, default))));
        Assert.Single(starts.Select(v => v.RetirementId).Distinct()); var intent = starts[0];
        Assert.True(intent.RetainsSource && intent.ReservesBudget); Assert.False(intent.ExecutionAvailable);
        Assert.Equal(totals, await Totals());
        Assert.Equal("Retiring", (await inspect.DevelopmentWorkspaces.AsNoTracking().SingleAsync(w => w.Id == workspace.Id)).LifecycleState);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Store(s => s.BeginAsync(actor, workspace, queued.ArchiveId, Guid.NewGuid(), default)));
        await AssertSqlFence($"UPDATE development_workspaces SET lifecycle_state='Active', lifecycle_version=lifecycle_version+1 WHERE id='{workspace.Id:D}'", "ck_workspace_retirement_fence");
        await AssertSqlFence($"UPDATE development_workspaces SET cpu_millis=cpu_millis+100 WHERE id='{workspace.Id:D}'", "ck_workspace_retirement_fence");
        await AssertSqlFence($"INSERT INTO development_workspace_runs (id,workspace_id) VALUES ('{Guid.NewGuid():D}','{workspace.Id:D}')", "ck_workspace_lifecycle_admission");
        await AssertSqlFence($"INSERT INTO development_workspace_source_imports (id,workspace_id) VALUES ('{Guid.NewGuid():D}','{workspace.Id:D}')", "ck_workspace_lifecycle_admission");
        await AssertSqlFence($"INSERT INTO development_workspace_archives (id,workspace_id,expected_epoch) VALUES ('{Guid.NewGuid():D}','{workspace.Id:D}',{workspace.LifecycleVersion})", "ck_workspace_archive_admission");
        await AssertSqlFence($"UPDATE development_workspace_retirements SET finished_at_utc=now() WHERE id='{intent.RetirementId:D}'", "ck_workspace_retirement_pair");
        var claimA = (await Store(s => s.ClaimAsync(default)))!;
        var wrongPlan = JsonNode.Parse(plan)!.AsObject(); wrongPlan["deletionAuthorized"] = true;
        await Assert.ThrowsAsync<InvalidDataException>(() => StoreWrite(s => s.RecordPlanAsync(claimA, wrongPlan.ToJsonString(), default)));
        await StoreWrite(s => s.RecordPlanAsync(claimA, plan, default));
        var prepared = (await Store(s => s.GetAsync(workspace.Id, intent.RetirementId, default)))!;
        Assert.Equal("AwaitingConfirmation", prepared.Phase); Assert.NotNull(prepared.PlanExpiresAtUtc);
        Assert.Null(await Store(s => s.ClaimAsync(default)));
        var digest = ReadDigest(plan, "retirementPlanSha256");
        var acknowledged = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Store(s => s.AcknowledgeAsync(actor, workspace.Id, intent.RetirementId, digest, default))));
        Assert.Equal(acknowledged[0].ConfirmedAtUtc, acknowledged[1].ConfirmedAtUtc);
        Assert.Equal("Prepared", acknowledged[0].Phase); Assert.False(acknowledged[0].ExecutionAvailable);
        Assert.Equal(1, await inspect.AuditEvents.CountAsync(a => a.JobId == intent.RetirementId && a.Action == "development.workspace.retirement.prepare.acknowledge"));
        Assert.Equal(totals, await Totals());
        await AssertSqlFence($"UPDATE development_workspace_retirements SET plan_expires_at_utc=plan_expires_at_utc+interval '1 hour' WHERE id='{intent.RetirementId:D}'", "ck_workspace_retirement_immutable");
        await AssertSqlFence($"UPDATE development_workspace_retirements SET finished_at_utc=now() WHERE id='{intent.RetirementId:D}'; UPDATE development_workspaces SET lifecycle_state='Stopped',cpu_millis=cpu_millis+100 WHERE id='{workspace.Id:D}'", "ck_workspace_retirement_fence");
        var canceled = await Store(s => s.CancelAsync(actor, workspace.Id, intent.RetirementId, default)); Assert.Equal("Canceled", canceled.State);
        Assert.Equal("Stopped", (await inspect.DevelopmentWorkspaces.AsNoTracking().SingleAsync(w => w.Id == workspace.Id)).LifecycleState);
        var eventCount = await inspect.OperationJobEvents.CountAsync(e => e.JobId == intent.RetirementId);
        await StoreWrite(s => s.RecordPlanAsync(claimA, plan, default));
        Assert.Equal(eventCount, await inspect.OperationJobEvents.CountAsync(e => e.JobId == intent.RetirementId));

        // A read-only preparation still owns its row fence until its leased
        // caller returns or a replacement claim observes the cancel request.
        var second = await Store(s => s.BeginAsync(actor, workspace, queued.ArchiveId, Guid.NewGuid(), default));
        var active = (await Store(s => s.ClaimAsync(default)))!;
        var cancelPending = await Store(s => s.CancelAsync(actor, workspace.Id, second.RetirementId, default));
        Assert.True(cancelPending.CancelRequested); Assert.Null(cancelPending.FinishedAtUtc);
        Assert.Equal("Retiring", (await inspect.DevelopmentWorkspaces.AsNoTracking().SingleAsync(w => w.Id == workspace.Id)).LifecycleState);
        await StoreWrite(s => s.RecordPlanAsync(active, plan, default));
        Assert.Equal("Canceled", (await Store(s => s.GetAsync(workspace.Id, second.RetirementId, default)))!.State);

        var third = await Store(s => s.BeginAsync(actor, workspace, queued.ArchiveId, Guid.NewGuid(), default));
        var oldLease = (await Store(s => s.ClaimAsync(default)))!; clock.Advance(91);
        var replacement = (await Store(s => s.ClaimAsync(default)))!; Assert.NotEqual(oldLease.Lease, replacement.Lease);
        await StoreWrite(s => s.RecordPlanAsync(oldLease, plan, default));
        Assert.Null((await Store(s => s.GetAsync(workspace.Id, third.RetirementId, default)))!.PlanJson);
        await StoreWrite(s => s.RecordPlanAsync(replacement, plan, default)); clock.Advance(601);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Store(s => s.AcknowledgeAsync(actor, workspace.Id, third.RetirementId, digest, default)));
        await Store(s => s.CancelAsync(actor, workspace.Id, third.RetirementId, default));
        var final = await inspect.DevelopmentWorkspaces.AsNoTracking().SingleAsync(w => w.Id == workspace.Id);
        Assert.Equal(row.EnvironmentJson, final.EnvironmentJson); Assert.Equal(row.OwnerSubject, final.OwnerSubject);
        Assert.Equal(row.Version, final.Version); Assert.Equal(row.LifecycleVersion, final.LifecycleVersion); Assert.Equal("Stopped", final.LifecycleState);
        Assert.Equal(totals, await Totals()); Assert.Equal(originalArchiveRows, await inspect.WorkspaceArchiveSteps.CountAsync());
        Assert.Equal(3, await inspect.WorkspaceRetirementIntents.CountAsync(r => r.WorkspaceId == workspace.Id));
        Assert.Equal(0, await inspect.WorkspaceRetirementIntents.CountAsync(r => r.FinishedAtUtc == null));
        Assert.Equal(0, await inspect.WorkspaceRetirementIntents.CountAsync(r => r.Lease != null));
        Console.WriteLine("INBOX_PHASE=workspace-retirement-preparation-postgres-replay-row-fence-lease-expiry-ack-cancel-budget-retained");

        async Task<string> Totals() => JsonSerializer.Serialize(await inspect.DevelopmentWorkspaces.AsNoTracking().OrderBy(w => w.Id)
            .Select(w => new { w.Id, w.OwnerSubject, w.CpuMillis, w.MemoryMiB, w.DiskMiB, w.Version, w.LifecycleVersion }).ToArrayAsync());
        async Task AssertSqlFence(string sql, string expected)
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => inspect.Database.ExecuteSqlRawAsync(sql));
            Assert.Equal(expected, error.ConstraintName);
        }
        async Task<T> Store<T>(Func<WorkspaceRetirementPreparationRepository, Task<T>> action)
        { await using var db = new PlatformDbContext(options); return await action(new(db, clock, new WorkspaceLifecycleRepository(db, clock))); }
        async Task StoreWrite(Func<WorkspaceRetirementPreparationRepository, Task> action)
        { await using var db = new PlatformDbContext(options); await action(new(db, clock, new WorkspaceLifecycleRepository(db, clock))); }
        async Task<T> Archive<T>(Func<WorkspaceArchiveRepository, Task<T>> action)
        { await using var db = new PlatformDbContext(options); return await action(new(db, clock, new WorkspaceLifecycleRepository(db, clock))); }
        async Task ArchiveWrite(Func<WorkspaceArchiveRepository, Task> action)
        { await using var db = new PlatformDbContext(options); await action(new(db, clock, new WorkspaceLifecycleRepository(db, clock))); }
    }
    private static string RetirementPlan(string verifyJson)
    {
        var result = JsonNode.Parse(verifyJson)!.AsObject();
        var archiveId = result["operationId"]!.DeepClone(); var archivePlan = result["planSha256"]!.DeepClone(); var receipt = result["receiptSha256"]!.DeepClone();
        foreach (var key in new[] { "operationId", "planSha256", "receiptSha256", "archiveFormat" }) result.Remove(key);
        result["archiveId"] = archiveId; result["archivePlanSha256"] = archivePlan; result["captureReceiptSha256"] = receipt;
        result["purpose"] = DevelopmentRetirementPlanBinding.Purpose; result["retainsSource"] = true;
        result["reservesBudget"] = true; result["deletionAuthorized"] = false;
        using var parsed = JsonDocument.Parse(result.ToJsonString());
        result["retirementPlanSha256"] = Convert.ToHexStringLower(SHA256.HashData(DevelopmentRetirementPlanBinding.Canonical(parsed.RootElement)));
        return result.ToJsonString();
    }
}
