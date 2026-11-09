using Microsoft.EntityFrameworkCore;
using Npgsql;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Agent;
using WhaleDeck.Infrastructure.Persistence;
using Retirement = WhaleDeck.Application.Services.ContainerRecycleArchiveRetirementWorkflowValidation;
using StartResult = WhaleDeck.Application.Models.ContainerFileUploadStartResult;

namespace WhaleDeck.IntegrationTests;

public sealed partial class NativeNotificationDispatchTests
{
    private static async Task RetirementRepositoryBoundaries(PlatformDbContext db, PlatformDbContext second, ContainerRecycleArchiveIntent archive,
        ResolvedContainerRecycleArchiveSource ancestor, FilePlanProtector protector, RecycleClock clock, TerminalActor actor)
    {
        var repo = new ContainerRecycleArchiveRetirementRepository(db, protector, protector, protector, protector, clock);
        var other = new ContainerRecycleArchiveRetirementRepository(second, protector, protector, protector, protector, clock);
        var source = ContainerRecycleArchiveRetirementSourceResolver.FromVerifiedRows(archive, ancestor, 30, protector, protector);
        var command = source.Binding.Command;
        var a = command.Archive.Plan;
        var capture = a.Command.Purge.Plan.HistoryCreatedAtUnixMs;
        var due = capture + 30 * 86400000L;
        var hash = new string('B', 64);
        var plan = new ContainerRecycleArchiveRetirementPlanDto(1, command, hash, a.SourceRecordDigest, a.PurgeRecordDigest, a.PurgeMarkerDigest, hash,
            a.HistoryMetadataDigest, capture, due, true, true, true, a.StorageHash, a.StoreIdentity, a.ArchiveIdentity, a.ArchiveChangedAtUnixNs,
            a.ArchiveBytes, a.ArchiveSha256, source.Binding.PublishedIdentity, source.Binding.PublishedChangedAtUnixNs, hash);
        ContainerRecycleArchiveRetirementIntent Request(string? proof = null)
        {
            var key = Guid.NewGuid();
            var now = Retirement.Time(clock.GetUtcNow());
            var row = new ContainerRecycleArchiveRetirementIntent
            {
                Id = Retirement.IntentId(actor.Subject, actor.LoginBinding, key), ArchiveIntentId = archive.Id, RetainDays = 30,
                SourceProofHash = proof ?? source.ProofHash, IdempotencyKey = key, ActorSubject = actor.Subject, TraceId = actor.TraceId,
                LoginBinding = actor.LoginBinding, OwnerHash = Retirement.Owner(actor.Subject, actor.LoginBinding),
                CreatedAtUtc = now, PlanningExpiresAtUtc = now.AddMinutes(15), NextAttemptAtUtc = now, RequestHash = ""
            };
            var mac = protector.FingerprintRetirementRequest(row);
            return new ContainerRecycleArchiveRetirementIntent
            {
                Id = row.Id, ArchiveIntentId = row.ArchiveIntentId, RetainDays = row.RetainDays, SourceProofHash = row.SourceProofHash,
                IdempotencyKey = row.IdempotencyKey, ActorSubject = row.ActorSubject, TraceId = row.TraceId, LoginBinding = row.LoginBinding,
                OwnerHash = row.OwnerHash, CreatedAtUtc = row.CreatedAtUtc, PlanningExpiresAtUtc = row.PlanningExpiresAtUtc,
                NextAttemptAtUtc = row.NextAttemptAtUtc, RequestHash = mac
            };
        }
        async Task<ContainerRecycleArchiveRetirementIntent> Plan(ContainerRecycleArchiveRetirementIntent row, ContainerRecycleArchiveRetirementPlanDto value)
        {
            await repo.EnqueueAsync(row, default);
            Assert.Equal(row.Id, (await other.EnqueueAsync(row, default)).Id);
            var claimed = (await repo.ClaimAsync(default))!;
            Assert.Equal(row.Id, claimed.Id);
            Assert.True(await repo.MarkPlanningAsync(claimed, default));
            await repo.FinishPlanningAsync(claimed, value, Retirement.Prefix + "READY", default);
            var ready = (await other.FindAsync(row.Id, default))!;
            Assert.Equal("Ready", ready.State);
            Retirement.Verify(ready, protector);
            return ready;
        }
        string Claims(ContainerRecycleArchiveRetirementIntent row) =>
            protector.SignRetirementConfirmation(new(row.Id, row.ActorSubject, row.LoginBinding, row.PlanHash!, row.ReadyExpiresAtUtc!.Value));
        ContainerRecycleArchiveRetirementReceiptDto Receipt(ContainerRecycleArchiveRetirementIntent row, string state, bool? applied, bool owned) =>
            new(row.ExecutionJobId!.Value, a.Command.Purge.Plan.Command.Source.OperationId, command.Archive.OperationId, plan.Fingerprint, state, true, owned, applied, due);

        var badSource = Request(new('0', 64));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.EnqueueAsync(badSource, default));
        Assert.False(await second.OperationJobs.AnyAsync(j => j.Id == badSource.Id));
        var auditFailure = Request();
        await second.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION wdv_retirement_reject_audit() RETURNS trigger LANGUAGE plpgsql AS $f$ BEGIN
              IF NEW.action = 'containers.files.recycle-archive-retirement.plan-accepted' THEN RAISE EXCEPTION 'fixture audit refusal'; END IF;
              RETURN NEW; END $f$;
            CREATE TRIGGER wdv_retirement_reject_audit BEFORE INSERT ON audit_events FOR EACH ROW EXECUTE FUNCTION wdv_retirement_reject_audit();
            """);
        try
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => repo.EnqueueAsync(auditFailure, default));
            Assert.Null(await other.FindAsync(auditFailure.Id, default));
            Assert.False(await second.OperationJobs.AnyAsync(j => j.Id == auditFailure.Id));
            Assert.False(await second.OperationJobEvents.AnyAsync(e => e.JobId == auditFailure.Id));
            Assert.False(await second.OutboxMessages.AnyAsync(m => m.MessageType == "ContainerRecycleArchiveRetirementRequested" && m.ProcessedAtUtc == null));
        }
        finally
        {
            await second.Database.ExecuteSqlRawAsync("DROP TRIGGER wdv_retirement_reject_audit ON audit_events; DROP FUNCTION wdv_retirement_reject_audit();");
        }
        var undued = await Plan(Request(), plan with { RetentionElapsed = false, RetirementEligible = false });
        var notDue = await Assert.ThrowsAsync<InvalidOperationException>(() => repo.ConfirmAsync(actor, Claims(undued), undued.Version, default));
        Assert.Equal(Retirement.Prefix + "NOT_DUE", notDue.Message);
        await repo.CancelAsync(actor, undued.Id, default);

        var ready = await Plan(Request(), plan);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.ConfirmAsync(actor, Claims(ready) + "0", ready.Version, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => repo.ConfirmAsync(actor with { LoginBinding = new('1', 64) }, Claims(ready), ready.Version, default));
        await using (var locked = await second.Database.BeginTransactionAsync())
        {
            await second.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM container_recycle_archive_intents WHERE id={archive.Id} FOR UPDATE");
            var blocked = await Assert.ThrowsAnyAsync<Exception>(() => repo.ConfirmAsync(actor, Claims(ready), ready.Version, default));
            Assert.Equal(PostgresErrorCodes.LockNotAvailable, Assert.IsType<PostgresException>(blocked.GetBaseException()).SqlState);
            await locked.RollbackAsync();
        }
        var pending = await repo.ConfirmAsync(actor, Claims(ready), ready.Version, default);
        Assert.Equal(pending.ExecutionJobId, (await other.ConfirmAsync(actor, Claims(ready), ready.Version, default)).ExecutionJobId);
        await new JobRepository(second).RequestCancellationAsync(pending.ExecutionJobId!.Value, actor.Subject, default);
        Assert.True(await repo.CancellationRequestedAsync(pending.Id, default));
        Assert.True(await second.OutboxMessages.AnyAsync(m => m.MessageType == "ContainerRecycleArchiveRetirementCancellationRequested" && m.ProcessedAtUtc == null));
        var canceledClaim = (await repo.ClaimAsync(default))!;
        Assert.Equal(StartResult.Canceled, await repo.StartAsync(canceledClaim, default));
        Assert.True(await repo.FinishAsync(canceledClaim, Retirement.Prefix + "CANCELED", null, default));
        var canceled = (await other.FindAsync(pending.Id, default))!;
        Assert.Equal("Canceled", canceled.State);
        Assert.Null(canceled.RootStartedAtUtc);
        Assert.Null(canceled.ReceiptJson);

        var executionReady = await Plan(Request(), plan);
        var execution = await repo.ConfirmAsync(actor, Claims(executionReady), executionReady.Version, default);
        var claim = (await repo.ClaimAsync(default))!;
        Assert.Equal(StartResult.LeaseLost, await repo.EnterApplyAsync(claim, default));
        Assert.Equal(StartResult.Started, await repo.StartAsync(claim, default));
        Assert.Equal(StartResult.LeaseLost, await repo.EnterApplyAsync(claim, default));
        var reserved = Receipt(execution, "Reserved", false, true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.ReserveAsync(claim, reserved with { ArchiveOperationId = Guid.NewGuid() }, default));
        Assert.True(await repo.ReserveAsync(claim, reserved, default));
        Assert.Equal(StartResult.Started, await repo.EnterApplyAsync(claim, default));
        Assert.Equal(StartResult.LeaseLost, await repo.EnterApplyAsync(claim, default));
        clock.Offset += TimeSpan.FromSeconds(91);
        var reclaimed = (await other.ClaimAsync(default))!;
        Assert.Equal(claim.Id, reclaimed.Id);
        Assert.NotEqual(claim.LeaseToken, reclaimed.LeaseToken);
        Assert.False(await repo.FinishAsync(claim, Retirement.Prefix + "COMPLETED", Receipt(execution, "Completed", true, true), default));
        Assert.Equal(StartResult.LeaseLost, await other.EnterApplyAsync(reclaimed, default));
        var unknown = Receipt(execution, "Unknown", null, true);
        Assert.True(await other.FinishAsync(reclaimed, Retirement.Prefix + "RESULT_UNKNOWN", unknown, default));
        var unresolved = (await repo.FindAsync(execution.Id, default))!;
        Assert.True(unresolved.PublishBarrier);
        Assert.Null(unresolved.RetirementApplied);
        Assert.Null(await repo.ClaimAsync(default));
        var reviewer = actor with { Subject = "native-retirement-reviewer", LoginBinding = new('C', 64) };
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.AcknowledgeAsync(reviewer, unresolved.Id, unresolved.Version + 1, unknown, default));
        await repo.AcknowledgeAsync(reviewer, unresolved.Id, unresolved.Version, unknown with { OwnedFence = false }, default);
        var final = (await other.FindAsync(execution.Id, default))!;
        Retirement.Verify(final, protector);
        Assert.Equal("Acknowledged", final.State);
        Assert.Equal(unresolved.ReceiptJson, final.ReceiptJson);
        Assert.Equal(unresolved.ReceiptHash, final.ReceiptHash);
        Assert.False(final.PublishBarrier);
        Assert.Contains(await second.AuditEvents.AsNoTracking().Where(e => e.JobId == execution.ExecutionJobId).ToArrayAsync(),
            e => e.ActorSubject == reviewer.Subject && e.Action.EndsWith(".unknown-acknowledged", StringComparison.Ordinal));
        Assert.False(await second.OutboxMessages.AnyAsync(m => m.ProcessedAtUtc == null &&
            (m.MessageType == "ContainerRecycleArchiveRetirementRequested" || m.MessageType == "ContainerRecycleArchiveRetirementCancellationRequested")));
        Console.WriteLine("INBOX_PHASE=archive-retirement-repository-source-mac-two-jobs-lease-pre-start-cancel-unknown-audited");
    }
}
