using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
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
    private static async Task FileTreePurgingWorkflowBoundaries(DbContextOptions<PlatformDbContext> options)
    {
        await using var db = new PlatformDbContext(options);
        var keyPath = Path.Combine(Path.GetTempPath(), "wd-native-purging-" + Guid.NewGuid().ToString("N"));
        await File.WriteAllBytesAsync(keyPath, RandomNumberGenerator.GetBytes(32));
        try
        {
            var protector = new FilePlanProtector(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:CapabilityKeyFile"] = keyPath }).Build());
            var clock = new NativeRestoringClock(); var identity = new NativeRestoringIdentity(); var gateway = new NativePurgingGateway();
            var authorization = new FileManagementService(null!, null!, null!, identity, clock);
            var repository = new FileTreePurgingRepository(db, protector, clock, protector, protector);
            var service = new FileTreePurgingService(authorization, repository, protector, gateway, clock);
            var worker = new FileTreePurgingDispatcher(repository, gateway, identity, protector, clock);
            var actor = new TerminalActor("native-purging-admin", null, [], new('A', 64), "native-purging", null);
            var captureActor = actor with { Subject = "native-purging-historical-owner" };
            var captureGateway = new NativeRecyclingGateway();
            var captures = new FileTreeRecyclingRepository(db, protector, clock, protector);
            var captureService = new FileTreeRecyclingService(authorization, captures, protector, captureGateway, clock);
            var captureWorker = new FileTreeRecyclingDispatcher(captures, captureGateway, identity, protector, clock);
            var restoring = new FileTreeRestoringService(authorization, new FileTreeRestoringRepository(db, protector, clock, protector, protector), protector, new NativeRestoringGateway(), clock);
            async Task<Guid> Capture(string slug)
            {
                var queued = await captureService.QueueAsync(captureActor, new(new("test.purgeflow", slug), new('B', 64)), Guid.NewGuid().ToString("D"), default);
                Assert.True(await captureWorker.TickAsync(default)); var ready = await captureService.GetAsync(captureActor, queued.Id, default);
                var confirmed = await captureService.ConfirmAsync(captureActor, ready.Id, new(ready.Confirmation!.PlanHash, true, ready.Version), default);
                Assert.True(await captureWorker.TickAsync(default)); var id = confirmed.ExecutionJobId!.Value;
                gateway.RegisterCapture(await db.FileTreeCaptures.AsNoTracking().SingleAsync(item => item.Id == id)); return id;
            }
            async Task<FileTreePurgingIntentDto> Ready(Guid capture, string scope)
            {
                var queued = await service.QueueAsync(actor, new(capture, scope), Guid.NewGuid().ToString("D"), default);
                Assert.True(await worker.TickAsync(default)); var ready = await service.GetAsync(actor, queued.Id, default);
                Assert.Equal("Ready", ready.State); return ready;
            }
            Task<FileTreePurgingIntentDto> Confirm(FileTreePurgingIntentDto ready) => service.ConfirmAsync(actor, ready.Id, new(ready.Confirmation!.PlanHash, true, ready.Version), default);
            async Task<FileTreeCaptureDto> History(Guid capture) => Assert.Single((await captures.CapturesAsync(new("test.purgeflow", ""), null, 100, default)).Items, item => item.Id == capture);
            async Task Due(Guid id)
            {
                clock.Now = clock.Now.AddSeconds(31);
                await db.FileTreePurgingOperations.Where(item => item.Id == id).ExecuteUpdateAsync(setters => setters.SetProperty(item => item.NextAttemptAtUtc, clock.GetUtcNow()));
            }
            foreach (var scope in new[] { "Archive", "Originals", "All" })
            {
                var capture = await Capture("scope-" + scope); var original = await db.FileTreeCaptures.AsNoTracking().SingleAsync(item => item.Id == capture);
                var catalogue = JsonSerializer.Serialize(original); var key = Guid.NewGuid().ToString("D");
                identity.Admin = false;
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.QueueAsync(actor, new(capture, scope), key, default)); identity.Admin = true;
                var queued = await service.QueueAsync(actor, new(capture, scope), key, default);
                Assert.Equal(queued.Id, (await service.GetByKeyAsync(actor, key, default)).Id);
                Assert.Equal(queued.Id, (await service.QueueAsync(actor, new(capture, scope), key, default)).Id);
                await Assert.ThrowsAsync<InvalidOperationException>(() => service.QueueAsync(actor, new(capture, scope == "All" ? "Archive" : "All"), key, default));
                await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetAsync(actor with { LoginBinding = new('C', 64) }, queued.Id, default));
                Assert.True(await worker.TickAsync(default)); var ready = await service.GetAsync(actor, queued.Id, default);
                await using (var reloaded = new PlatformDbContext(options))
                    Assert.Equal(ready.Confirmation!.PlanHash, (await new FileTreePurgingService(authorization, new FileTreePurgingRepository(reloaded, protector, clock, protector, protector), protector, gateway, clock)
                        .GetAsync(actor, ready.Id, default)).Confirmation!.PlanHash);
                var confirmations = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
                {
                    await using var concurrent = new PlatformDbContext(options);
                    return await new FileTreePurgingService(authorization, new FileTreePurgingRepository(concurrent, protector, clock, protector, protector), protector, gateway, clock)
                        .ConfirmAsync(actor, ready.Id, new(ready.Confirmation!.PlanHash, true, ready.Version), default);
                }));
                var confirmed = confirmations[0]; Assert.Single(confirmations.Select(item => item.ExecutionJobId).Distinct());
                Assert.Equal(2, await db.FilePathLocks.CountAsync(item => item.OperationId == confirmed.ExecutionJobId)); Assert.True((await History(capture)).CleanupActive);
                await Assert.ThrowsAsync<InvalidOperationException>(() => restoring.QueueAsync(actor, new(capture, new("test.purgeflow", "restore-" + scope)), Guid.NewGuid().ToString("D"), default));
                var child = new FileMutationPlan(new("copy", new("test.purgeflow", "scope-" + scope + "/child"), new("test.purgeflow", "elsewhere"), new('A', 64), ""),
                    new(1, 2, 0x81A4, 1000, 1000, 1, 7, -1), new(1, 3), new(1, 3), 7, new('B', 64), new('D', 64));
                Assert.Equal("FILES_TARGET_BUSY", (await Assert.ThrowsAsync<InvalidOperationException>(() => new FileMutationRepository(db).EnqueueAsync(actor, Guid.NewGuid(), Guid.NewGuid().ToString("D"), new('E', 64),
                    new(actor.Subject, actor.LoginBinding, child, clock.GetUtcNow().AddMinutes(5)), clock.GetUtcNow().AddMinutes(15), default))).Message);
                Assert.True(await worker.TickAsync(default)); var succeeded = await service.GetAsync(actor, ready.Id, default); Assert.Equal("Succeeded", succeeded.State);
                var row = (await repository.OwnedAsync(actor, ready.Id, default))!; Assert.True(FileTreePurgingValidation.HasValidBinding(row, protector));
                Assert.NotNull(row.ReceiptHash); Assert.Empty(await db.FilePathLocks.Where(item => item.OperationId == confirmed.ExecutionJobId).ToArrayAsync());
                var history = await History(capture); Assert.False(history.CleanupActive);
                Assert.Equal(scope == "Originals" ? "Retained" : "Purged", history.ArchiveState);
                Assert.Equal(scope == "Archive" ? "Retained" : "Purged", history.OriginalsState);
                Assert.Equal(catalogue, JsonSerializer.Serialize(await db.FileTreeCaptures.AsNoTracking().SingleAsync(item => item.Id == capture)));
                Assert.Equal(confirmed.ExecutionJobId, (await Confirm(ready)).ExecutionJobId);
                await Assert.ThrowsAsync<InvalidOperationException>(() => service.QueueAsync(actor, new(capture, scope), Guid.NewGuid().ToString("D"), default));
                if (scope == "Originals")
                {
                    var allowed = await restoring.QueueAsync(actor, new(capture, new("test.purgeflow", "restorable-after-originals")), Guid.NewGuid().ToString("D"), default);
                    await restoring.CancelAsync(actor, allowed.Id, default);
                    var second = await Confirm(await Ready(capture, "Archive")); Assert.True(await worker.TickAsync(default));
                    Assert.Equal("Purged", (await History(capture)).ArchiveState); Assert.Equal("Purged", (await History(capture)).OriginalsState);
                    Assert.NotNull(second.ExecutionJobId);
                }
                else await Assert.ThrowsAsync<InvalidOperationException>(() => restoring.QueueAsync(actor, new(capture, new("test.purgeflow", "deleted-archive")), Guid.NewGuid().ToString("D"), default));
                foreach (var sql in new[] { "DELETE FROM file_tree_purging_operations WHERE id={0}", "UPDATE file_tree_purging_operations SET receipt_hash=repeat('F',64) WHERE id={0}",
                    "UPDATE file_tree_purging_operations SET version=version+1 WHERE id={0}" })
                { var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql, row.Id)); Assert.Equal("23514", error.SqlState); }
            }
            // Root admitted, response lost: retained=false cannot be invented.
            var lostCapture = await Capture("lost-response"); var lost = await Confirm(await Ready(lostCapture, "All")); gateway.Unknown = true; gateway.LoseResponse = true;
            Assert.True(await worker.TickAsync(default)); Assert.Equal("Unknown", (await service.GetAsync(actor, lost.Id, default)).State);
            var uncertain = await History(lostCapture); Assert.Equal("Uncertain", uncertain.ArchiveState); Assert.Equal("Uncertain", uncertain.OriginalsState); Assert.True(uncertain.CleanupActive);
            var applied = gateway.Applies; var eventCount = await db.OperationJobEvents.CountAsync(item => item.JobId == lost.ExecutionJobId);
            var auditCount = await db.AuditEvents.CountAsync(item => item.JobId == lost.ExecutionJobId); gateway.ReceiptUnavailable = true;
            await Due(lost.Id); Assert.True(await worker.TickAsync(default)); Assert.Equal(applied, gateway.Applies);
            Assert.Equal(eventCount, await db.OperationJobEvents.CountAsync(item => item.JobId == lost.ExecutionJobId));
            Assert.Equal(auditCount, await db.AuditEvents.CountAsync(item => item.JobId == lost.ExecutionJobId));
            Assert.Equal(2, await db.FilePathLocks.CountAsync(item => item.OperationId == lost.ExecutionJobId)); gateway.ReceiptUnavailable = false;
            await Due(lost.Id); Assert.True(await worker.TickAsync(default)); Assert.Equal(applied, gateway.Applies);
            var observed = (await repository.OwnedAsync(actor, lost.Id, default))!; Assert.NotNull(observed.ReceiptHash);
            Assert.Equal("Purged", (await History(lostCapture)).OriginalsState); Assert.Equal("Uncertain", (await History(lostCapture)).ArchiveState);
            var observedAt = observed.ReceiptObservedAtUtc; var receiptHash = observed.ReceiptHash;
            var immutable = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE file_tree_purging_operations SET receipt_hash=repeat('F',64) WHERE id={lost.Id}"));
            Assert.Contains("tree_purging_receipt_immutable", immutable.MessageText, StringComparison.Ordinal);
            await Due(lost.Id); Assert.True(await worker.TickAsync(default)); Assert.Equal(observedAt, (await repository.OwnedAsync(actor, lost.Id, default))!.ReceiptObservedAtUtc);
            var unknown = await service.GetAsync(actor, lost.Id, default); await service.AcknowledgeAsync(actor, lost.Id, new(unknown.Version, true), default);
            var acknowledged = (await repository.OwnedAsync(actor, lost.Id, default))!; Assert.Equal("Acknowledged", acknowledged.State); Assert.Equal(receiptHash, acknowledged.ReceiptHash);
            Assert.Equal(observedAt, acknowledged.ReceiptObservedAtUtc); Assert.False((await History(lostCapture)).CleanupActive);
            Assert.Equal("Uncertain", (await History(lostCapture)).ArchiveState); Assert.Empty(await db.FilePathLocks.Where(item => item.OperationId == lost.ExecutionJobId).ToArrayAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(() => restoring.QueueAsync(actor, new(lostCapture, new("test.purgeflow", "uncertain-archive")), Guid.NewGuid().ToString("D"), default));
            gateway.Unknown = false;
            var canceledCapture = await Capture("canceled"); var canceled = await Confirm(await Ready(canceledCapture, "All")); await service.CancelAsync(actor, canceled.Id, default);
            applied = gateway.Applies; Assert.True(await worker.TickAsync(default)); Assert.Equal(applied, gateway.Applies); Assert.Equal("Canceled", (await service.GetAsync(actor, canceled.Id, default)).State);
            Assert.Equal("Retained", (await History(canceledCapture)).ArchiveState);
            var canceledReady = await Ready(canceledCapture, "All"); await service.CancelAsync(actor, canceledReady.Id, default);
            await Assert.ThrowsAsync<InvalidOperationException>(() => Confirm(canceledReady));
            var queuedCancel = await service.QueueAsync(actor, new(canceledCapture, "All"), Guid.NewGuid().ToString("D"), default);
            await service.CancelAsync(actor, queuedCancel.Id, default); Assert.Equal("Canceled", (await service.GetAsync(actor, queuedCancel.Id, default)).State);
            await Assert.ThrowsAsync<KeyNotFoundException>(() => service.QueueAsync(actor, new(Guid.NewGuid(), "All"), Guid.NewGuid().ToString("D"), default));
            var revoked = await Confirm(await Ready(canceledCapture, "All")); identity.Admin = false; applied = gateway.Applies;
            Assert.True(await worker.TickAsync(default)); identity.Admin = true; Assert.Equal(applied, gateway.Applies);
            Assert.Equal("Canceled", (await service.GetAsync(actor, revoked.Id, default)).State);
            var firstReady = await Ready(canceledCapture, "Archive"); var secondReady = await Ready(canceledCapture, "All");
            var firstConfirmed = await Confirm(firstReady);
            await Assert.ThrowsAsync<InvalidOperationException>(() => Confirm(secondReady));
            Assert.Null((await repository.OwnedAsync(actor, secondReady.Id, default))!.ExecutionJobId);
            await service.CancelAsync(actor, firstConfirmed.Id, default); Assert.True(await worker.TickAsync(default)); await service.CancelAsync(actor, secondReady.Id, default);
            var overwriteGateway = new NativeOverwriteRestoringGateway(); overwriteGateway.RegisterCapture(await db.FileTreeCaptures.AsNoTracking().SingleAsync(item => item.Id == canceledCapture));
            var overwriteRepository = new FileTreeOverwriteRestoringRepository(db, protector, clock, protector);
            var overwriteService = new FileTreeOverwriteRestoringService(authorization, overwriteRepository, protector, overwriteGateway, clock);
            var overwriteWorker = new FileTreeOverwriteRestoringDispatcher(overwriteRepository, overwriteGateway, identity, protector, clock);
            var overwrite = await overwriteService.QueueAsync(actor, new(canceledCapture, new("test.purgeflow", "overwrite-capture"), new('6', 64)), Guid.NewGuid().ToString("D"), default);
            Assert.True(await overwriteWorker.TickAsync(default)); overwrite = await overwriteService.GetAsync(actor, overwrite.Id, default);
            overwrite = await overwriteService.ConfirmAsync(actor, overwrite.Id, new(overwrite.Confirmation!.PlanHash, true, overwrite.Version), default);
            Assert.True(await overwriteWorker.TickAsync(default)); var overwrittenCapture = overwrite.ExecutionJobId!.Value;
            var overwrittenCatalogue = await db.FileTreeCaptures.AsNoTracking().SingleAsync(item => item.Id == overwrittenCapture); Assert.Equal("Overwrite", overwrittenCatalogue.Kind);
            gateway.RegisterCapture(overwrittenCatalogue); var overwrittenPurge = await Confirm(await Ready(overwrittenCapture, "All"));
            Assert.True(await worker.TickAsync(default)); Assert.Equal("Succeeded", (await service.GetAsync(actor, overwrittenPurge.Id, default)).State);
            Assert.Equal("Purged", (await History(overwrittenCapture)).ArchiveState); Assert.Equal("Purged", (await History(overwrittenCapture)).OriginalsState);
            var expired = await Ready(canceledCapture, "Archive"); clock.Now = clock.Now.AddMinutes(6);
            await Assert.ThrowsAsync<InvalidOperationException>(() => Confirm(expired)); Assert.True(await worker.TickAsync(default)); Assert.Equal("Expired", (await service.GetAsync(actor, expired.Id, default)).State);
            var audit = await Ready(canceledCapture, "All");
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE audit_events ADD CONSTRAINT native_purging_audit_failure CHECK (action <> 'files.tree-purge.confirmed') NOT VALID");
            try { await Assert.ThrowsAsync<DbUpdateException>(() => Confirm(audit)); }
            finally { await db.Database.ExecuteSqlRawAsync("ALTER TABLE audit_events DROP CONSTRAINT native_purging_audit_failure"); }
            Assert.Null((await repository.OwnedAsync(actor, audit.Id, default))!.ExecutionJobId);
            Assert.False(await db.OperationJobs.AnyAsync(item => item.Id == FileTreePurgingValidation.ExecutionId(audit.Id)));
            Assert.Empty(await db.FilePathLocks.Where(item => item.OperationId == FileTreePurgingValidation.ExecutionId(audit.Id)).ToArrayAsync()); await service.CancelAsync(actor, audit.Id, default);
            var waiting = new List<FileTreePurgingIntentDto>();
            for (var index = 0; index < 4; index++) waiting.Add(await service.QueueAsync(actor, new(canceledCapture, "All"), Guid.NewGuid().ToString("D"), default));
            Assert.Equal("FILES_OPERATION_BUDGET_EXHAUSTED", (await Assert.ThrowsAsync<InvalidOperationException>(() => service.QueueAsync(actor, new(canceledCapture, "All"), Guid.NewGuid().ToString("D"), default))).Message);
            foreach (var item in waiting) await service.CancelAsync(actor, item.Id, default);
            var downgrade = await Assert.ThrowsAsync<PostgresException>(() => db.GetService<IMigrator>().MigrateAsync("20261020112522_FileTreeOverwriteRestoringWorkflow"));
            Assert.Contains("tree_purging_downgrade_requires_empty_kind", downgrade.MessageText, StringComparison.Ordinal);
            Assert.False(db.Database.HasPendingModelChanges()); Assert.Equal(0, await db.FileTreePurgingOperations.CountAsync(item => item.CompletedAtUtc == null));
            var details = string.Join(' ', await db.AuditEvents.Where(item => item.Action.StartsWith("files.tree-purge.")).Select(item => item.DetailJson).ToArrayAsync());
            Assert.DoesNotContain("CaptureOwner", details, StringComparison.Ordinal); Assert.DoesNotContain("lost-response", details, StringComparison.Ordinal);
            Console.WriteLine("INBOX_PHASE=tree-purge-real-jsonb-signed-permanent-receipt-component-history-concurrent-locks-unknown-audit-cancel-budget");
        }
        finally { File.Delete(keyPath); }
    }

    // Intentional Agent transport stub. This proves actual PostgreSQL transactions,
    // constraints and application behavior, not physical payload deletion or UI.
    private sealed class NativePurgingGateway : IFileTreePurgingGateway
    {
        private readonly Dictionary<Guid, FileTreeCapture> _captures = [];
        private readonly Dictionary<Guid, FileTreePurgingReceipt> _receipts = [];
        public int Applies { get; private set; } public bool Unknown { get; set; } public bool LoseResponse { get; set; } public bool ReceiptUnavailable { get; set; }
        public void RegisterCapture(FileTreeCapture capture) => _captures.Add(capture.Id, capture);
        public Task<FileTreePurgingPlan> PlanTreePurgingAsync(TerminalActor actor, FileTreePurgingCommand command, CancellationToken cancellationToken)
        {
            var originals = FileTreePurgingValidation.Selects(command.Scope, "Originals"); var capture = _captures[command.CaptureId];
            return Task.FromResult(new FileTreePurgingPlan(command, new('D', 64), originals ? new(1, 2) : null, new('A', 64), new(1, 3), new('B', 64), new('C', 64), new('D', 64),
                new(1, 4), capture.ArchiveBytes, capture.ArchiveSha256, capture.ContentSha256 ?? new('E', 64), capture.EntryCount, capture.TotalBytes,
                originals ? new(1, 5) : null, originals ? new(1, 6) : null, originals ? new('F', 64) : null, new('8', 64)));
        }
        public Task<FileTreePurgingReceipt?> InspectTreePurgingAsync(FileTreePurgingOperation operation, CancellationToken cancellationToken) => ReceiptUnavailable
            ? throw new IOException("test-only unavailable purge receipt") : Task.FromResult(_receipts.GetValueOrDefault(operation.ExecutionJobId!.Value));
        public Task<FileTreePurgingReceipt> ApplyTreePurgingAsync(FileTreePurgingOperation operation, CancellationToken cancellationToken)
        {
            Applies++; var receipt = Result(operation, Unknown ? "Unknown" : "Completed"); _receipts[receipt.OperationId] = receipt;
            if (LoseResponse) { LoseResponse = false; throw new IOException("test-only lost purge response"); } return Task.FromResult(receipt);
        }
        public Task<FileTreePurgingReceipt> DiscardTreePurgingAsync(FileTreePurgingOperation operation, CancellationToken cancellationToken)
        { var receipt = _receipts.GetValueOrDefault(operation.ExecutionJobId!.Value) ?? Result(operation, "Aborted"); _receipts[receipt.OperationId] = receipt; return Task.FromResult(receipt); }
        private static FileTreePurgingReceipt Result(FileTreePurgingOperation row, string state)
        {
            var plan = FileTreePurgingValidation.ReadPlan(row); var scope = plan.Command.Scope;
            return new(row.ExecutionJobId!.Value, "DirectoryPurge", state, plan.Fingerprint, row.CaptureId, scope, plan.EntryCount, plan.TotalBytes, plan.ArchiveBytes,
                state != "Aborted", state == "Completed" && FileTreePurgingValidation.Selects(scope, "Archive"), state != "Aborted" && FileTreePurgingValidation.Selects(scope, "Originals"));
        }
    }
}
