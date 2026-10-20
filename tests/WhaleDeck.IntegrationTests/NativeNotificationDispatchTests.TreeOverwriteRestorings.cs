using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Agent;
using WhaleDeck.Infrastructure.Persistence;

namespace WhaleDeck.IntegrationTests;

public sealed partial class NativeNotificationDispatchTests
{
    // Real PostgreSQL transactions/locks/triggers and Application services;
    // identity and Agent transport are deliberate stubs, not installed UDS proof.
    private static async Task FileTreeOverwriteRestoringWorkflowBoundaries(DbContextOptions<PlatformDbContext> options)
    {
        await using var db = new PlatformDbContext(options);
        var keyPath = Path.Combine(Path.GetTempPath(), "wd-native-overwrite-" + Guid.NewGuid().ToString("N"));
        await File.WriteAllBytesAsync(keyPath, RandomNumberGenerator.GetBytes(32));
        try
        {
            var protector = new FilePlanProtector(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:CapabilityKeyFile"] = keyPath }).Build());
            var clock = new NativeRestoringClock(); var identity = new NativeRestoringIdentity();
            var gateway = new NativeOverwriteRestoringGateway();
            var repository = new FileTreeOverwriteRestoringRepository(db, protector, clock, protector);
            var authorization = new FileManagementService(null!, null!, null!, identity, clock);
            var service = new FileTreeOverwriteRestoringService(authorization, repository, protector, gateway, clock);
            var worker = new FileTreeOverwriteRestoringDispatcher(repository, gateway, identity, protector, clock);
            var actor = new TerminalActor("native-overwrite-admin", null, [], new('A', 64), "native-overwrite", null);
            var captureActor = actor with { Subject = "native-overwrite-historical-capture-owner" };
            var captureRepository = new FileTreeRecyclingRepository(db, protector, clock, protector);
            var captureGateway = new NativeRecyclingGateway();
            var captureService = new FileTreeRecyclingService(authorization, captureRepository, protector, captureGateway, clock);
            var captureWorker = new FileTreeRecyclingDispatcher(captureRepository, captureGateway, identity, protector, clock);
            var captureQueued = await captureService.QueueAsync(captureActor, new(new("test.overwrite", "historical-source"), new('B', 64)), Guid.NewGuid().ToString("D"), default);
            Assert.True(await captureWorker.TickAsync(default));
            var captureReady = await captureService.GetAsync(captureActor, captureQueued.Id, default);
            var captureConfirmed = await captureService.ConfirmAsync(captureActor, captureReady.Id, new(captureReady.Confirmation!.PlanHash, true, captureReady.Version), default);
            Assert.True(await captureWorker.TickAsync(default));
            var captureId = captureConfirmed.ExecutionJobId!.Value;
            var historical = await db.FileTreeCaptures.AsNoTracking().SingleAsync(item => item.Id == captureId);
            var historicalProof = JsonSerializer.Serialize(historical); gateway.RegisterCapture(historical);
            CreateFileTreeOverwriteRestoringCommand Command(string slug) => new(captureId, new("test.overwrite", "target-" + slug), new('6', 64));
            async Task<FileTreeOverwriteRestoringIntentDto> Ready(string slug)
            {
                var queued = await service.QueueAsync(actor, Command(slug), Guid.NewGuid().ToString("D"), default);
                Assert.Equal("Queued", queued.State); Assert.Null(queued.ExecutionJobId); Assert.Null(queued.OverwrittenCaptureId);
                Assert.False(queued.BackupVerified); Assert.True(await worker.TickAsync(default));
                var ready = await service.GetAsync(actor, queued.Id, default); Assert.Equal("Ready", ready.State);
                Assert.Null(ready.OverwrittenCaptureId); Assert.False(ready.BackupVerified); return ready;
            }
            Task<FileTreeOverwriteRestoringIntentDto> Confirm(FileTreeOverwriteRestoringIntentDto ready) =>
                service.ConfirmAsync(actor, ready.Id, new(ready.Confirmation!.PlanHash, true, ready.Version), default);
            Task Due(Guid id) => db.FileTreeOverwriteRestoringOperations.Where(item => item.Id == id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.NextAttemptAtUtc, clock.GetUtcNow()));
            async Task AssertAtomicAbsence(Guid intentId)
            {
                Assert.False(await db.FileTreeOverwriteRestoringOperations.AnyAsync(item => item.Id == intentId));
                Assert.False(await db.OperationJobs.AnyAsync(item => item.Id == intentId));
                Assert.False(await db.OperationJobEvents.AnyAsync(item => item.JobId == intentId));
                Assert.False(await db.AuditEvents.AnyAsync(item => item.JobId == intentId));
                var marker = intentId.ToString("D");
                Assert.False(await db.OutboxMessages.FromSqlInterpolated($"SELECT * FROM outbox_messages WHERE payload_json->>'jobId'={marker}").AnyAsync());
            }

            identity.Admin = false;
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.QueueAsync(actor, Command("denied"), Guid.NewGuid().ToString("D"), default));
            identity.Admin = true;
            var key = Guid.NewGuid().ToString("D"); var command = Command("one");
            var queuedOne = await service.QueueAsync(actor, command, key, default);
            Assert.Equal(queuedOne.Id, (await service.QueueAsync(actor, command, key, default)).Id);
            Assert.Equal(queuedOne.Id, (await service.GetByKeyAsync(actor, key, default)).Id);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.QueueAsync(actor, command with { TargetExpectedVersion = new('7', 64) }, key, default));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.QueueAsync(actor, Command("different"), key, default));
            await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetByKeyAsync(actor with { LoginBinding = new('C', 64) }, key, default));
            Assert.Null(await repository.OwnedAsync(actor with { LoginBinding = new('C', 64) }, queuedOne.Id, default));
            Assert.Empty(await db.FilePathLocks.Where(item => item.OperationId == queuedOne.Id).ToArrayAsync());
            Assert.True(await worker.TickAsync(default)); var readyOne = await service.GetAsync(actor, queuedOne.Id, default);
            Assert.Equal("Ready", readyOne.State); Assert.NotNull(readyOne.Confirmation); Assert.Equal(0, gateway.Applies);
            Assert.True(readyOne.TotalBytes > FileManagementService.MaximumBytes); Assert.True(readyOne.TargetTotalBytes > FileManagementService.MaximumBytes);
            Assert.Null(readyOne.OverwrittenCaptureId); Assert.Null(readyOne.BackupArchiveBytes);
            Assert.Equal("Succeeded", (await db.OperationJobs.AsNoTracking().SingleAsync(item => item.Id == readyOne.Id)).State);
            await using (var reloaded = new PlatformDbContext(options))
            {
                var another = new FileTreeOverwriteRestoringService(authorization, new FileTreeOverwriteRestoringRepository(reloaded, protector, clock, protector), protector, gateway, clock);
                Assert.Equal(readyOne.Confirmation.PlanHash, (await another.GetAsync(actor, readyOne.Id, default)).Confirmation!.PlanHash);
                await Assert.ThrowsAsync<KeyNotFoundException>(() => another.GetAsync(actor with { LoginBinding = new('C', 64) }, readyOne.Id, default));
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => another.ConfirmAsync(actor with { LoginBinding = new('C', 64) }, readyOne.Id,
                    new(readyOne.Confirmation.PlanHash, true, readyOne.Version), default));
            }
            var confirmations = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
            {
                await using var concurrent = new PlatformDbContext(options);
                return await new FileTreeOverwriteRestoringService(authorization, new FileTreeOverwriteRestoringRepository(concurrent, protector, clock, protector), protector, gateway, clock)
                    .ConfirmAsync(actor, readyOne.Id, new(readyOne.Confirmation.PlanHash, true, readyOne.Version), default);
            }));
            Assert.Single(confirmations.Select(item => item.ExecutionJobId).Distinct()); var confirmedOne = confirmations[0];
            Assert.NotEqual(confirmedOne.Id, confirmedOne.ExecutionJobId); Assert.Null(confirmedOne.OverwrittenCaptureId);
            Assert.Equal(3, await db.FilePathLocks.CountAsync(item => item.OperationId == confirmedOne.ExecutionJobId));
            var token = FileTreeOverwriteRestoringValidation.CaptureToken(captureId);
            Assert.Equal(token, FileTreeRestoringValidation.CaptureToken(captureId));
            Assert.Single(await db.FilePathLocks.Where(item => item.OperationId == confirmedOne.ExecutionJobId && item.ScopeKind == "Token" && item.PathHash == token).ToArrayAsync());
            Assert.Equal("files.tree-overwrite-restore", (await db.OperationJobs.AsNoTracking().SingleAsync(item => item.Id == confirmedOne.ExecutionJobId)).JobType);

            // The old absent-target restoration shares capture and hierarchy locks.
            var absentRepository = new FileTreeRestoringRepository(db, protector, clock, protector, protector);
            var absentGateway = new NativeOverwriteCaptureAbsentGateway(historical);
            var absentService = new FileTreeRestoringService(authorization, absentRepository, protector, absentGateway, clock);
            var absentWorker = new FileTreeRestoringDispatcher(absentRepository, absentGateway, identity, protector, clock);
            var absentQueued = await absentService.QueueAsync(actor, new(captureId, new("test.overwrite", "different-absent-target")), Guid.NewGuid().ToString("D"), default);
            Assert.True(await absentWorker.TickAsync(default)); var absentReady = await absentService.GetAsync(actor, absentQueued.Id, default);
            var busy = await Assert.ThrowsAsync<InvalidOperationException>(() => absentService.ConfirmAsync(actor, absentReady.Id, new(absentReady.Confirmation!.PlanHash, true, absentReady.Version), default));
            Assert.Equal("FILES_TARGET_BUSY", busy.Message); Assert.Null((await absentService.GetAsync(actor, absentReady.Id, default)).ExecutionJobId);
            Assert.False(await db.OperationJobs.AnyAsync(item => item.Id == FileTreeRestoringValidation.ExecutionId(absentReady.Id)));
            await absentService.CancelAsync(actor, absentReady.Id, default);
            var childPlan = new FileMutationPlan(new("copy", new("test.overwrite", "target-one/child"), new("test.overwrite", "elsewhere"), new('A', 64), ""),
                new(1, 2, 0x81A4, 1000, 1000, 1, 7, -1), new(1, 3), new(1, 3), 7, new('B', 64), new('D', 64));
            var hierarchy = await Assert.ThrowsAsync<InvalidOperationException>(() => new FileMutationRepository(db).EnqueueAsync(actor, Guid.NewGuid(), Guid.NewGuid().ToString("D"), new('E', 64),
                new(actor.Subject, actor.LoginBinding, childPlan, clock.GetUtcNow().AddMinutes(5)), clock.GetUtcNow().AddMinutes(15), default));
            Assert.Equal("FILES_TARGET_BUSY", hierarchy.Message);
            Assert.True(await worker.TickAsync(default)); var completedOne = await service.GetAsync(actor, confirmedOne.Id, default);
            Assert.Equal("Succeeded", completedOne.State); Assert.Equal(1, gateway.Applies);
            Assert.True(completedOne.BackupVerified && completedOne.BackupMayBeRetained && completedOne.OriginalsRetained && completedOne.TargetMayHaveChanged);
            Assert.Equal(completedOne.ExecutionJobId, completedOne.OverwrittenCaptureId); Assert.False(completedOne.PartialRetained);
            Assert.Empty(await db.FilePathLocks.Where(item => item.OperationId == confirmedOne.ExecutionJobId).ToArrayAsync());
            Assert.Equal(confirmedOne.ExecutionJobId, (await Confirm(readyOne)).ExecutionJobId); Assert.Equal(1, gateway.Applies);
            var savedOne = await repository.OwnedAsync(actor, completedOne.Id, default); Assert.NotNull(savedOne);
            var oldTargetCapture = await db.FileTreeCaptures.AsNoTracking().SingleAsync(item => item.Id == completedOne.OverwrittenCaptureId);
            var oldTargetPlan = FileTreeOverwriteRestoringValidation.ReadPlan(savedOne);
            var oldTargetProof = FileTreeCaptureValidation.Verify(oldTargetCapture, protector, protector);
            Assert.Equal("Overwrite", oldTargetProof.Kind); Assert.Equal(savedOne.OwnerHash, oldTargetCapture.OwnerHash);
            Assert.NotEqual(historical.OwnerHash, oldTargetCapture.OwnerHash); Assert.Equal(savedOne.TargetHash, oldTargetCapture.SourceHash);
            Assert.Equal(new FileTreeRecyclingCommand(command.Target, command.TargetExpectedVersion), oldTargetProof.Source);
            Assert.Equal(oldTargetPlan.TargetManifestSha256, oldTargetProof.ManifestSha256); Assert.Equal(oldTargetPlan.TargetTreeSha256, oldTargetProof.ContentSha256);
            Assert.Equal(oldTargetPlan.TargetEntryCount, oldTargetProof.EntryCount); Assert.Equal(oldTargetPlan.TargetTotalBytes, oldTargetProof.TotalBytes);
            Assert.Equal(completedOne.BackupArchiveBytes, oldTargetProof.ArchiveBytes); Assert.Equal(completedOne.BackupArchiveSha256, oldTargetProof.ArchiveSha256);
            Assert.Single(await db.AuditEvents.Where(item => item.JobId == confirmedOne.ExecutionJobId && item.Action == "files.tree-overwrite-restore.result").ToArrayAsync());
            var processedMessages = await db.OutboxMessages.Where(item => item.MessageType == "FileTreeOverwriteRestoringRequested").ToArrayAsync();
            Assert.Contains(processedMessages, item => item.PayloadJson.Contains(confirmedOne.ExecutionJobId!.Value.ToString("D"), StringComparison.Ordinal) && item.ProcessedAtUtc is not null);

            // A newly catalogued Overwrite capture is a first-class authenticated
            // input to both new overwrite and old absent-target planning.
            gateway.RegisterCapture(oldTargetCapture);
            var again = await service.QueueAsync(actor, new(oldTargetCapture.Id, new("test.overwrite", "target-again"), new('7', 64)), Guid.NewGuid().ToString("D"), default);
            Assert.True(await worker.TickAsync(default)); var againReady = await service.GetAsync(actor, again.Id, default);
            Assert.Equal("Ready", againReady.State); Assert.Equal("Overwrite", againReady.Command.CaptureKind);
            Assert.Equal(command.Target, againReady.Command.Source); Assert.Equal(oldTargetProof.TotalBytes, againReady.TotalBytes);
            var againConfirmed = await Confirm(againReady); Assert.True(await worker.TickAsync(default));
            Assert.Equal("Succeeded", (await service.GetAsync(actor, againConfirmed.Id, default)).State);
            var previousKindGateway = new NativeOverwriteCaptureAbsentGateway(oldTargetCapture);
            var previousKindService = new FileTreeRestoringService(authorization, absentRepository, protector, previousKindGateway, clock);
            var previousKindWorker = new FileTreeRestoringDispatcher(absentRepository, previousKindGateway, identity, protector, clock);
            var fromOverwrite = await previousKindService.QueueAsync(actor, new(oldTargetCapture.Id, new("test.overwrite", "absent-from-overwrite")), Guid.NewGuid().ToString("D"), default);
            Assert.True(await previousKindWorker.TickAsync(default)); var fromOverwriteReady = await previousKindService.GetAsync(actor, fromOverwrite.Id, default);
            Assert.Equal("Ready", fromOverwriteReady.State); Assert.Equal("Overwrite", fromOverwriteReady.Command.CaptureKind);
            Assert.Equal(oldTargetProof.ArchiveBytes, fromOverwriteReady.ArchiveBytes); await previousKindService.CancelAsync(actor, fromOverwriteReady.Id, default);

            var cancelReady = await Ready("cancel-ready"); await service.CancelAsync(actor, cancelReady.Id, default);
            await Assert.ThrowsAsync<InvalidOperationException>(() => Confirm(cancelReady));
            var cancelQueued = await service.QueueAsync(actor, Command("cancel-queued"), Guid.NewGuid().ToString("D"), default);
            await service.CancelAsync(actor, cancelQueued.Id, default); Assert.Equal("Canceled", (await service.GetAsync(actor, cancelQueued.Id, default)).State);
            var cancelExecution = await Confirm(await Ready("cancel-execution")); var applied = gateway.Applies;
            await service.CancelAsync(actor, cancelExecution.Id, default); Assert.True(await worker.TickAsync(default));
            var canceled = await service.GetAsync(actor, cancelExecution.Id, default);
            Assert.Equal("Canceled", canceled.State); Assert.Equal(applied, gateway.Applies); Assert.Null(canceled.OverwrittenCaptureId); Assert.False(canceled.BackupVerified);
            Assert.False(await db.FileTreeCaptures.AnyAsync(item => item.Id == cancelExecution.ExecutionJobId));
            var expired = await Ready("expired-ready"); clock.Now = clock.Now.AddMinutes(6);
            await Assert.ThrowsAsync<InvalidOperationException>(() => Confirm(expired)); Assert.True(await worker.TickAsync(default));
            Assert.Equal("Expired", (await service.GetAsync(actor, expired.Id, default)).State);
            var executionExpired = await Confirm(await Ready("expired-execution")); clock.Now = clock.Now.AddMinutes(16); applied = gateway.Applies;
            Assert.True(await worker.TickAsync(default)); Assert.Equal(applied, gateway.Applies);
            Assert.Equal("Failed", (await service.GetAsync(actor, executionExpired.Id, default)).State);
            var downgrade = await Confirm(await Ready("downgrade")); identity.Admin = false; applied = gateway.Applies;
            Assert.True(await worker.TickAsync(default)); identity.Admin = true;
            Assert.Equal(applied, gateway.Applies); Assert.Equal("Canceled", (await service.GetAsync(actor, downgrade.Id, default)).State);

            var lease = await Confirm(await Ready("lease-takeover"));
            var oldLease = Assert.IsType<FileTreeOverwriteRestoringOperation>(await repository.ClaimAsync(clock.GetUtcNow(), default)); Assert.Equal(lease.Id, oldLease.Id);
            clock.Now = clock.Now.AddSeconds(91); await Due(lease.Id);
            var newLease = Assert.IsType<FileTreeOverwriteRestoringOperation>(await repository.ClaimAsync(clock.GetUtcNow(), default));
            Assert.Equal(lease.Id, newLease.Id); Assert.NotEqual(oldLease.LeaseToken, newLease.LeaseToken);
            Assert.False(await repository.RenewAsync(oldLease, clock.GetUtcNow(), default));
            Assert.Equal(FileUploadStartResult.LeaseLost, await repository.StartedAsync(oldLease, default));
            Assert.Equal(FileUploadStartResult.Started, await repository.StartedAsync(newLease, default));
            var leaseResult = await gateway.ApplyTreeOverwriteRestoringAsync(newLease, default);
            Assert.True(await repository.FinishAsync(newLease, "Succeeded", "FILES_TREE_OVERWRITE_RESTORE_COMPLETED", leaseResult, default));

            var lost = await Confirm(await Ready("lost-response")); gateway.LoseResponse = true;
            Assert.True(await worker.TickAsync(default)); applied = gateway.Applies;
            var lostRow = await service.GetAsync(actor, lost.Id, default); Assert.Equal("Unknown", lostRow.State); Assert.Null(lostRow.OverwrittenCaptureId);
            Assert.NotNull((await repository.OwnedAsync(actor, lost.Id, default))!.RootStartedAtUtc);
            Assert.Equal(3, await db.FilePathLocks.CountAsync(item => item.OperationId == lost.ExecutionJobId));
            gateway.ReceiptUnavailable = true;
            var lostEvents = await db.OperationJobEvents.CountAsync(item => item.JobId == lost.ExecutionJobId);
            var lostAudit = await db.AuditEvents.CountAsync(item => item.JobId == lost.ExecutionJobId);
            clock.Now = clock.Now.AddSeconds(7); await Due(lost.Id); Assert.True(await worker.TickAsync(default));
            Assert.Equal(applied, gateway.Applies); Assert.Equal(lostEvents, await db.OperationJobEvents.CountAsync(item => item.JobId == lost.ExecutionJobId));
            Assert.Equal(lostAudit, await db.AuditEvents.CountAsync(item => item.JobId == lost.ExecutionJobId)); gateway.ReceiptUnavailable = false;
            clock.Now = clock.Now.AddSeconds(7); await Due(lost.Id); Assert.True(await worker.TickAsync(default));
            Assert.Equal(applied, gateway.Applies); Assert.Equal("Succeeded", (await service.GetAsync(actor, lost.Id, default)).State);
            var started = await Confirm(await Ready("started-no-receipt"));
            var startedLease = Assert.IsType<FileTreeOverwriteRestoringOperation>(await repository.ClaimAsync(clock.GetUtcNow(), default)); Assert.Equal(started.Id, startedLease.Id);
            Assert.Equal(FileUploadStartResult.Started, await repository.StartedAsync(startedLease, default));
            clock.Now = clock.Now.AddSeconds(91); await Due(started.Id); applied = gateway.Applies;
            Assert.True(await worker.TickAsync(default)); Assert.Equal(applied, gateway.Applies);
            Assert.Equal("Failed", (await service.GetAsync(actor, started.Id, default)).State);

            var unknown = await Confirm(await Ready("unknown-with-backup")); gateway.Unknown = true;
            Assert.True(await worker.TickAsync(default)); var unknownRow = await service.GetAsync(actor, unknown.Id, default);
            Assert.Equal("Unknown", unknownRow.State); Assert.True(unknownRow.PartialRetained && unknownRow.BackupVerified && unknownRow.OriginalsRetained && unknownRow.TargetMayHaveChanged);
            Assert.Equal(unknown.ExecutionJobId, unknownRow.OverwrittenCaptureId);
            var unknownBackup = await db.FileTreeCaptures.AsNoTracking().SingleAsync(item => item.Id == unknown.ExecutionJobId);
            var backupSnapshot = JsonSerializer.Serialize(unknownBackup);
            var unknownEvents = await db.OperationJobEvents.CountAsync(item => item.JobId == unknown.ExecutionJobId);
            gateway.DowngradeVerifiedProof = true; clock.Now = clock.Now.AddSeconds(7); await Due(unknown.Id); applied = gateway.Applies;
            Assert.True(await worker.TickAsync(default)); Assert.Equal(applied, gateway.Applies);
            Assert.Equal(unknownEvents, await db.OperationJobEvents.CountAsync(item => item.JobId == unknown.ExecutionJobId));
            Assert.True((await service.GetAsync(actor, unknown.Id, default)).BackupVerified);
            Assert.Equal(backupSnapshot, JsonSerializer.Serialize(await db.FileTreeCaptures.AsNoTracking().SingleAsync(item => item.Id == unknown.ExecutionJobId)));
            gateway.DowngradeVerifiedProof = false;
            foreach (var sql in new[]
            {
                "UPDATE file_tree_overwrite_restoring_operations SET backup_archive_bytes=backup_archive_bytes+1 WHERE id={0}",
                "UPDATE file_tree_overwrite_restoring_operations SET originals_retained=false WHERE id={0}",
                "UPDATE file_tree_overwrite_restoring_operations SET archive_bytes=NULL,archive_sha256=NULL WHERE id={0}"
            })
            {
                var rejection = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql, unknown.Id)); Assert.Equal("23514", rejection.SqlState);
            }
            unknownRow = await service.GetAsync(actor, unknown.Id, default);
            await service.AcknowledgeAsync(actor, unknown.Id, new(unknownRow.Version, true), default); gateway.Unknown = false;
            var acknowledged = await service.GetAsync(actor, unknown.Id, default);
            Assert.Equal("Acknowledged", acknowledged.State); Assert.True(acknowledged.BackupVerified && acknowledged.OriginalsRetained && acknowledged.TargetMayHaveChanged);
            Assert.Empty(await db.FilePathLocks.Where(item => item.OperationId == unknown.ExecutionJobId).ToArrayAsync());
            Assert.Contains((await captureService.CapturesAsync(actor, acknowledged.Command.Target, null, 25, default)).Items,
                item => item.Id == acknowledged.OverwrittenCaptureId && item.Kind == "Overwrite" && item.ContentSha256 is not null);

            // Audit rejection rolls back queue, confirmation, backup catalogue,
            // Jobs, Outbox and locks as one transaction, not just the intent row.
            var rollbackReady = await Ready("confirm-audit-rollback");
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE audit_events ADD CONSTRAINT native_overwrite_audit_failure CHECK (action NOT LIKE 'files.tree-overwrite-restore.%') NOT VALID");
            var rollbackKey = Guid.NewGuid().ToString("D"); var rollbackId = FileTreeOverwriteRestoringValidation.IntentId(actor, rollbackKey);
            try
            {
                await Assert.ThrowsAsync<DbUpdateException>(() => service.QueueAsync(actor, Command("queue-audit-rollback"), rollbackKey, default));
                await AssertAtomicAbsence(rollbackId);
                await Assert.ThrowsAsync<DbUpdateException>(() => Confirm(rollbackReady));
                Assert.Null((await repository.OwnedAsync(actor, rollbackReady.Id, default))!.ExecutionJobId);
                var rejectedExecution = FileTreeOverwriteRestoringValidation.ExecutionId(rollbackReady.Id);
                Assert.False(await db.OperationJobs.AnyAsync(item => item.Id == rejectedExecution));
                Assert.False(await db.OperationJobEvents.AnyAsync(item => item.JobId == rejectedExecution));
                Assert.False(await db.AuditEvents.AnyAsync(item => item.JobId == rejectedExecution));
                var rejectedMarker = rejectedExecution.ToString("D");
                Assert.False(await db.OutboxMessages.AnyAsync(item => item.PayloadJson.Contains(rejectedMarker)));
                Assert.Empty(await db.FilePathLocks.Where(item => item.OperationId == rejectedExecution).ToArrayAsync());
            }
            finally { await db.Database.ExecuteSqlRawAsync("ALTER TABLE audit_events DROP CONSTRAINT native_overwrite_audit_failure"); }
            await service.CancelAsync(actor, rollbackReady.Id, default);
            var rollbackResult = await Confirm(await Ready("result-audit-rollback"));
            var rollbackLease = Assert.IsType<FileTreeOverwriteRestoringOperation>(await repository.ClaimAsync(clock.GetUtcNow(), default)); Assert.Equal(rollbackResult.Id, rollbackLease.Id);
            Assert.Equal(FileUploadStartResult.Started, await repository.StartedAsync(rollbackLease, default));
            var completedReceipt = await gateway.ApplyTreeOverwriteRestoringAsync(rollbackLease, default); applied = gateway.Applies;
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE audit_events ADD CONSTRAINT native_overwrite_result_audit_failure CHECK (action<>'files.tree-overwrite-restore.result') NOT VALID");
            try
            {
                await Assert.ThrowsAsync<DbUpdateException>(() => repository.FinishAsync(rollbackLease, "Succeeded", "FILES_TREE_OVERWRITE_RESTORE_COMPLETED", completedReceipt, default));
                Assert.False(await db.FileTreeCaptures.AnyAsync(item => item.Id == rollbackResult.ExecutionJobId));
                Assert.False((await repository.OwnedAsync(actor, rollbackResult.Id, default))!.BackupVerified);
                Assert.Equal("Running", (await db.OperationJobs.AsNoTracking().SingleAsync(item => item.Id == rollbackResult.ExecutionJobId)).State);
                Assert.Equal(3, await db.FilePathLocks.CountAsync(item => item.OperationId == rollbackResult.ExecutionJobId));
                var pendingMarker = rollbackResult.ExecutionJobId!.Value.ToString("D");
                var pending = await db.OutboxMessages.Where(item => item.MessageType == "FileTreeOverwriteRestoringRequested" && item.PayloadJson.Contains(pendingMarker)).ToArrayAsync();
                Assert.Single(pending); Assert.Null(pending[0].ProcessedAtUtc);
            }
            finally { await db.Database.ExecuteSqlRawAsync("ALTER TABLE audit_events DROP CONSTRAINT native_overwrite_result_audit_failure"); }
            await repository.DeferAsync(rollbackLease, default); clock.Now = clock.Now.AddSeconds(7); await Due(rollbackResult.Id);
            Assert.True(await worker.TickAsync(default)); Assert.Equal(applied, gateway.Applies);
            Assert.Equal("Succeeded", (await service.GetAsync(actor, rollbackResult.Id, default)).State);

            var immutableRequest = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE file_tree_overwrite_restoring_operations SET request_hash={new string('0', 64)} WHERE id={completedOne.Id}"));
            Assert.Equal("23514", immutableRequest.SqlState);
            await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM file_tree_overwrite_restoring_operations WHERE id={completedOne.Id}"));
            await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE file_tree_captures SET archive_bytes=archive_bytes+1 WHERE id={oldTargetCapture.Id}"));
            await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM file_tree_captures WHERE id={oldTargetCapture.Id}"));
            var forgedCapture = Guid.NewGuid();
            var origin = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO file_tree_captures (id,intent_id,kind,owner_hash,source_hash,source_json,manifest_sha256,content_sha256,
                    entry_count,total_bytes,archive_bytes,archive_sha256,proof_hash,created_at_utc)
                SELECT {forgedCapture},intent_id,'Overwrite',owner_hash,source_hash,source_json,manifest_sha256,{new string('C', 64)},
                    entry_count,total_bytes,archive_bytes,archive_sha256,proof_hash,created_at_utc FROM file_tree_captures WHERE id={captureId}
                """));
            Assert.Equal("23514", origin.SqlState); Assert.Equal("tree_capture_origin_required", origin.MessageText);
            Assert.False(await db.FileTreeCaptures.AnyAsync(item => item.Id == forgedCapture));
            Assert.Equal(historicalProof, JsonSerializer.Serialize(await db.FileTreeCaptures.AsNoTracking().SingleAsync(item => item.Id == captureId)));
            await Assert.ThrowsAsync<KeyNotFoundException>(() => service.QueueAsync(actor, new(Guid.NewGuid(), new("test.overwrite", "missing-capture"), new('6', 64)), Guid.NewGuid().ToString("D"), default));
            var waiting = new List<FileTreeOverwriteRestoringIntentDto>();
            for (var index = 0; index < 4; index++) waiting.Add(await service.QueueAsync(actor, Command("budget-" + index), Guid.NewGuid().ToString("D"), default));
            var budget = await Assert.ThrowsAsync<InvalidOperationException>(() => service.QueueAsync(actor, Command("budget-denied"), Guid.NewGuid().ToString("D"), default));
            Assert.Equal("FILES_OPERATION_BUDGET_EXHAUSTED", budget.Message);
            foreach (var intent in waiting) await service.CancelAsync(actor, intent.Id, default);
            Assert.Equal(0, await db.FileTreeOverwriteRestoringOperations.CountAsync(item => item.ActorSubject == actor.Subject && item.CompletedAtUtc == null));
            var ownExecutions = await db.FileTreeOverwriteRestoringOperations.Where(item => item.ActorSubject == actor.Subject && item.ExecutionJobId != null)
                .Select(item => item.ExecutionJobId!.Value).ToArrayAsync();
            Assert.Empty(await db.FilePathLocks.Where(item => ownExecutions.Contains(item.OperationId)).ToArrayAsync());
            Console.WriteLine("INBOX_PHASE=tree-overwrite-restore-real-jsonb-two-jobs-capture-locks-backup-monotonic-audit-lease-budget");
        }
        finally { File.Delete(keyPath); }
    }

    private sealed class NativeOverwriteRestoringGateway : IFileTreeOverwriteRestoringGateway
    {
        private readonly Dictionary<Guid, FileTreeCapture> _captures = [];
        private readonly Dictionary<Guid, FileTreeOverwriteRestoringReceipt> _receipts = [];
        public bool LoseResponse { get; set; }
        public bool ReceiptUnavailable { get; set; }
        public bool Unknown { get; set; }
        public bool DowngradeVerifiedProof { get; set; }
        public int Applies { get; private set; }
        public void RegisterCapture(FileTreeCapture capture) => _captures.Add(capture.Id, capture);
        public Task<FileTreeOverwriteRestoringPlan> PlanTreeOverwriteRestoringAsync(TerminalActor actor, FileTreeOverwriteRestoringCommand command, CancellationToken cancellationToken)
        {
            var capture = _captures[command.CaptureId];
            return Task.FromResult(new FileTreeOverwriteRestoringPlan(command, capture.ArchiveSha256, capture.ArchiveBytes,
                capture.ContentSha256 ?? new('C', 64), capture.EntryCount, capture.TotalBytes, new(ulong.MaxValue, 9007199254740993UL),
                new('D', 64), new('E', 64), new('F', 64), new('4', 64), new('5', 64), 5, 2147483655,
                new(9007199254740993UL, ulong.MaxValue)));
        }
        public Task<FileTreeOverwriteRestoringReceipt?> InspectTreeOverwriteRestoringAsync(FileTreeOverwriteRestoringOperation operation, CancellationToken cancellationToken)
        {
            if (ReceiptUnavailable) throw new IOException("test-only receipt transport unavailable");
            return Task.FromResult(DowngradeVerifiedProof ? Result(operation, "Unknown", false) : _receipts.GetValueOrDefault(operation.ExecutionJobId!.Value));
        }
        public Task<FileTreeOverwriteRestoringReceipt> ApplyTreeOverwriteRestoringAsync(FileTreeOverwriteRestoringOperation operation, CancellationToken cancellationToken)
        {
            Applies++; var result = Result(operation, Unknown ? "Unknown" : "Completed", true); _receipts[operation.ExecutionJobId!.Value] = result;
            if (LoseResponse) { LoseResponse = false; throw new IOException("test-only lost response"); }
            return Task.FromResult(result);
        }
        public Task<FileTreeOverwriteRestoringReceipt> DiscardTreeOverwriteRestoringAsync(FileTreeOverwriteRestoringOperation operation, CancellationToken cancellationToken)
        {
            var id = operation.ExecutionJobId!.Value; var result = _receipts.GetValueOrDefault(id) ?? Result(operation, "Aborted", false);
            _receipts[id] = result; return Task.FromResult(result);
        }
        private static FileTreeOverwriteRestoringReceipt Result(FileTreeOverwriteRestoringOperation row, string state, bool verified)
        {
            var plan = FileTreeOverwriteRestoringValidation.ReadPlan(row);
            return new(row.ExecutionJobId!.Value, state, plan.Fingerprint, "tar.gz", plan.EntryCount, plan.TotalBytes, plan.ArchiveBytes,
                plan.ArchiveSha256, state == "Completed" ? new('A', 64) : "", state == "Unknown", "DirectoryOverwriteRestore", plan.Command.CaptureId,
                verified ? row.ExecutionJobId : null, verified, verified, verified, verified, verified ? 2147498000 : 0, verified ? new('8', 64) : "");
        }
    }

    private sealed class NativeOverwriteCaptureAbsentGateway(FileTreeCapture capture) : IFileTreeRestoringGateway
    {
        public Task<FileTreeRestoringPlan> PlanTreeRestoringAsync(TerminalActor actor, FileTreeRestoringCommand command, CancellationToken cancellationToken) =>
            Task.FromResult(new FileTreeRestoringPlan(command, capture.ArchiveSha256, capture.ArchiveBytes, capture.ContentSha256 ?? new('C', 64),
                capture.EntryCount, capture.TotalBytes, new(ulong.MaxValue, 9007199254740993UL), new('D', 64), new('E', 64), new('F', 64)));
        public Task<FileTreeRestoringReceipt?> InspectTreeRestoringAsync(FileTreeRestoringOperation operation, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FileTreeRestoringReceipt> ApplyTreeRestoringAsync(FileTreeRestoringOperation operation, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FileTreeRestoringReceipt> DiscardTreeRestoringAsync(FileTreeRestoringOperation operation, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
