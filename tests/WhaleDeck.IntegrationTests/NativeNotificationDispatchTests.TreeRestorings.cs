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
    private static async Task FileTreeRestoringWorkflowBoundaries(DbContextOptions<PlatformDbContext> options)
    {
        await using var db = new PlatformDbContext(options);
        var keyPath = Path.Combine(Path.GetTempPath(), "wd-native-restoring-" + Guid.NewGuid().ToString("N")); await File.WriteAllBytesAsync(keyPath, RandomNumberGenerator.GetBytes(32));
        try
        {
            var protector = new FilePlanProtector(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:CapabilityKeyFile"] = keyPath }).Build());
            var clock = new NativeRestoringClock(); var identity = new NativeRestoringIdentity(); var gateway = new NativeRestoringGateway();
            var repository = new FileTreeRestoringRepository(db, protector, clock, protector);
            var authorization = new FileManagementService(null!, null!, null!, identity, clock);
            var service = new FileTreeRestoringService(authorization, repository, protector, gateway, clock);
            var worker = new FileTreeRestoringDispatcher(repository, gateway, identity, protector, clock);
            var actor = new TerminalActor("native-restoring-admin", null, [], new('A', 64), "native-restoring", null);
            var captureActor = actor with { Subject = "native-restoring-original-capture-owner" };
            var captureGateway = new NativeRecyclingGateway();
            var captureRepository = new FileTreeRecyclingRepository(db, protector, clock);
            var captureService = new FileTreeRecyclingService(authorization, captureRepository, protector, captureGateway, clock);
            var captureWorker = new FileTreeRecyclingDispatcher(captureRepository, captureGateway, identity, protector, clock);
            var captureQueued = await captureService.QueueAsync(captureActor, new(new("test.treeflow", "source-one"), new('B', 64)), Guid.NewGuid().ToString("D"), default);
            Assert.True(await captureWorker.TickAsync(default));
            var captureReady = await captureService.GetAsync(captureActor, captureQueued.Id, default);
            var captureConfirmed = await captureService.ConfirmAsync(captureActor, captureReady.Id, new(captureReady.Confirmation!.PlanHash, true, captureReady.Version), default);
            Assert.True(await captureWorker.TickAsync(default));
            var captureId = captureConfirmed.ExecutionJobId!.Value;
            CreateFileTreeRestoringCommand Command(string slug) => new(captureId, new("test.treeflow", "target-" + slug));
            async Task<FileTreeRestoringIntentDto> Ready(string slug)
            {
                var intent = await service.QueueAsync(actor, Command(slug), Guid.NewGuid().ToString("D"), default);
                Assert.Equal("Queued", intent.State); Assert.Null(intent.ExecutionJobId); Assert.Null(intent.Confirmation);
                Assert.True(await worker.TickAsync(default)); return await service.GetAsync(actor, intent.Id, default);
            }
            async Task<FileTreeRestoringIntentDto> Confirm(FileTreeRestoringIntentDto ready) => await service.ConfirmAsync(actor, ready.Id, new(ready.Confirmation!.PlanHash, true, ready.Version), default);
            async Task Due(Guid id) => await db.FileTreeRestoringOperations.Where(item => item.Id == id).ExecuteUpdateAsync(setters => setters.SetProperty(item => item.NextAttemptAtUtc, clock.GetUtcNow()));
            identity.Admin = false;
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.QueueAsync(actor, Command("denied"), Guid.NewGuid().ToString("D"), default)); identity.Admin = true;
            var requestKey = Guid.NewGuid().ToString("D"); var queued = await service.QueueAsync(actor, Command("one"), requestKey, default);
            Assert.Equal(queued.Id, (await service.QueueAsync(actor, Command("one"), requestKey, default)).Id);
            Assert.Equal(queued.Id, (await service.GetByKeyAsync(actor, requestKey, default)).Id);
            await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetByKeyAsync(actor with { LoginBinding = new('C', 64) }, requestKey, default));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.QueueAsync(actor, Command("different"), requestKey, default));
            Assert.Null((await repository.OwnedAsync(actor with { LoginBinding = new('C', 64) }, queued.Id, default)));
            Assert.Empty(await db.FilePathLocks.Where(item => item.OperationId == queued.Id).ToArrayAsync());
            Assert.True(await worker.TickAsync(default)); var ready = await service.GetAsync(actor, queued.Id, default);
            Assert.Equal("Ready", ready.State); Assert.NotNull(ready.Confirmation); Assert.True(ready.TotalBytes > FileManagementService.MaximumBytes);
            Assert.Equal(1073756160, ready.ArchiveBytes); Assert.Equal(new string('9', 64), ready.ArchiveSha256);
            Assert.Equal("Succeeded", (await db.OperationJobs.AsNoTracking().SingleAsync(item => item.Id == ready.PlanningJobId)).State);
            Assert.Equal(0, gateway.Applies); Assert.Null(ready.ExecutionJobId);
            await using (var reloaded = new PlatformDbContext(options))
            {
                var reloadedService = new FileTreeRestoringService(authorization, new FileTreeRestoringRepository(reloaded, protector, clock, protector), protector, gateway, clock);
                Assert.Equal(ready.Confirmation.PlanHash, (await reloadedService.GetAsync(actor, ready.Id, default)).Confirmation!.PlanHash);
                await Assert.ThrowsAsync<KeyNotFoundException>(() => reloadedService.GetAsync(actor with { LoginBinding = new('C', 64) }, ready.Id, default));
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => reloadedService.ConfirmAsync(actor with { LoginBinding = new('C', 64) }, ready.Id, new(ready.Confirmation.PlanHash, true, ready.Version), default));
            }
            var confirmations = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
            {
                await using var concurrent = new PlatformDbContext(options);
                return await new FileTreeRestoringService(authorization, new FileTreeRestoringRepository(concurrent, protector, clock, protector), protector, gateway, clock)
                    .ConfirmAsync(actor, ready.Id, new(ready.Confirmation.PlanHash, true, ready.Version), default);
            }));
            Assert.Single(confirmations.Select(item => item.ExecutionJobId).Distinct()); var copy = confirmations[0];
            Assert.NotEqual(copy.PlanningJobId, copy.ExecutionJobId); Assert.Equal(3, await db.FilePathLocks.CountAsync(item => item.OperationId == copy.ExecutionJobId));
            Assert.Equal("files.tree-restore", (await db.OperationJobs.AsNoTracking().SingleAsync(item => item.Id == copy.ExecutionJobId)).JobType);
            var childPlan = new FileMutationPlan(new("copy", new("test.treeflow", "source-one/child"), new("test.treeflow", "elsewhere"), new('A', 64), ""),
                new(1, 2, 0x81A4, 1000, 1000, 1, 7, -1), new(1, 3), new(1, 3), 7, new('B', 64), new('D', 64));
            var conflict = await Assert.ThrowsAsync<InvalidOperationException>(() => new FileMutationRepository(db).EnqueueAsync(actor, Guid.NewGuid(), Guid.NewGuid().ToString("D"), new('E', 64),
                new(actor.Subject, actor.LoginBinding, childPlan, clock.GetUtcNow().AddMinutes(5)), clock.GetUtcNow().AddMinutes(15), default));
            Assert.Equal("FILES_TARGET_BUSY", conflict.Message);
            Assert.True(await worker.TickAsync(default)); Assert.Equal("Succeeded", (await service.GetAsync(actor, copy.Id, default)).State); Assert.Equal(1, gateway.Applies);
            Assert.Empty(await db.FilePathLocks.Where(item => item.OperationId == copy.ExecutionJobId).ToArrayAsync());
            Assert.Equal(copy.ExecutionJobId, (await Confirm(ready)).ExecutionJobId); Assert.Equal(1, gateway.Applies);
            var samePath = await service.QueueAsync(actor, new(captureId, new("test.treeflow", "source-one")), Guid.NewGuid().ToString("D"), default);
            Assert.True(await worker.TickAsync(default)); var samePathConfirmed = await Confirm(await service.GetAsync(actor, samePath.Id, default));
            Assert.Equal(2, await db.FilePathLocks.CountAsync(item => item.OperationId == samePathConfirmed.ExecutionJobId));
            Assert.Single(await db.FilePathLocks.Where(item => item.OperationId == samePathConfirmed.ExecutionJobId && item.ScopeKind == "Path").ToArrayAsync());
            Assert.True(await worker.TickAsync(default)); Assert.Equal("Succeeded", (await service.GetAsync(actor, samePath.Id, default)).State);
            await Assert.ThrowsAsync<KeyNotFoundException>(() => service.QueueAsync(actor, new(Guid.NewGuid(), new("test.treeflow", "missing-capture")), Guid.NewGuid().ToString("D"), default));
            Assert.Equal(1, await db.AuditEvents.CountAsync(item => item.JobId == copy.ExecutionJobId && item.Action == "files.tree-restore.result"));
            Assert.Contains(await db.OutboxMessages.Where(item => item.MessageType == "FileTreeRestoringRequested").ToArrayAsync(), item => item.ProcessedAtUtc is not null);
            var fence = await Ready("cancel-ready"); await service.CancelAsync(actor, fence.Id, default);
            await Assert.ThrowsAsync<InvalidOperationException>(() => Confirm(fence)); Assert.Equal("Canceled", (await service.GetAsync(actor, fence.Id, default)).State);
            var queuedCancel = await service.QueueAsync(actor, Command("cancel-queued"), Guid.NewGuid().ToString("D"), default);
            await service.CancelAsync(actor, queuedCancel.Id, default); Assert.Equal("Canceled", (await service.GetAsync(actor, queuedCancel.Id, default)).State);
            var expired = await Ready("expired"); clock.Now = clock.Now.AddMinutes(6);
            await Assert.ThrowsAsync<InvalidOperationException>(() => Confirm(expired)); Assert.True(await worker.TickAsync(default));
            Assert.Equal("Expired", (await service.GetAsync(actor, expired.Id, default)).State);
            var downgrade = await Confirm(await Ready("downgrade")); var applied = gateway.Applies; identity.Admin = false;
            Assert.True(await worker.TickAsync(default)); identity.Admin = true; Assert.Equal(applied, gateway.Applies);
            Assert.Equal("Canceled", (await service.GetAsync(actor, downgrade.Id, default)).State);
            var lost = await Confirm(await Ready("lost")); gateway.LoseResponse = true;
            Assert.True(await worker.TickAsync(default)); applied = gateway.Applies; Assert.NotNull((await repository.OwnedAsync(actor, lost.Id, default))!.RootStartedAtUtc);
            Assert.Equal("Unknown", (await service.GetAsync(actor, lost.Id, default)).State);
            Assert.Equal(3, await db.FilePathLocks.CountAsync(item => item.OperationId == lost.ExecutionJobId));
            Assert.Equal("AwaitingTreeReceipt", (await db.OperationJobs.AsNoTracking().SingleAsync(item => item.Id == lost.ExecutionJobId)).Phase);
            var lostEvents = await db.OperationJobEvents.CountAsync(item => item.JobId == lost.ExecutionJobId);
            var lostAudit = await db.AuditEvents.CountAsync(item => item.JobId == lost.ExecutionJobId);
            gateway.ReceiptUnavailable = true; clock.Now = clock.Now.AddSeconds(7); await Due(lost.Id); Assert.True(await worker.TickAsync(default));
            Assert.Equal(lostEvents, await db.OperationJobEvents.CountAsync(item => item.JobId == lost.ExecutionJobId));
            Assert.Equal(lostAudit, await db.AuditEvents.CountAsync(item => item.JobId == lost.ExecutionJobId));
            Assert.Equal(applied, gateway.Applies); Assert.Equal(3, await db.FilePathLocks.CountAsync(item => item.OperationId == lost.ExecutionJobId));
            gateway.ReceiptUnavailable = false;
            clock.Now = clock.Now.AddSeconds(7); await Due(lost.Id); Assert.True(await worker.TickAsync(default));
            Assert.Equal(applied, gateway.Applies); Assert.Equal("Succeeded", (await service.GetAsync(actor, lost.Id, default)).State);
            var missing = await Confirm(await Ready("started-no-receipt")); var claim = Assert.IsType<FileTreeRestoringOperation>(await repository.ClaimAsync(clock.GetUtcNow(), default));
            Assert.Equal(missing.Id, claim.Id); Assert.Equal(FileUploadStartResult.Started, await repository.StartedAsync(claim, default));
            clock.Now = clock.Now.AddMinutes(2); await Due(missing.Id); applied = gateway.Applies; Assert.True(await worker.TickAsync(default));
            Assert.Equal(applied, gateway.Applies); Assert.Equal("Failed", (await service.GetAsync(actor, missing.Id, default)).State);
            var late = await Confirm(await Ready("late-job-lock")); var lateClaim = Assert.IsType<FileTreeRestoringOperation>(await repository.ClaimAsync(clock.GetUtcNow(), default));
            Assert.Equal(late.Id, lateClaim.Id);
            await using (var locker = new PlatformDbContext(options))
            {
                await using var transaction = await locker.Database.BeginTransactionAsync();
                await locker.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM operation_jobs WHERE id={late.ExecutionJobId!.Value} FOR UPDATE");
                var blocked = Task.Run(async () => { await using var contender = new PlatformDbContext(options); return await new FileTreeRestoringRepository(contender, protector, clock, protector).StartedAsync(lateClaim, default); });
                var observedWait = false;
                for (var attempt = 0; attempt < 40; attempt++)
                {
                    observedWait = await db.Database.SqlQueryRaw<bool>("SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE usename=current_user AND pid<>pg_backend_pid() AND wait_event_type='Lock' AND query LIKE '%operation_jobs%' AND query LIKE '%FOR UPDATE%') AS \"Value\"").SingleAsync();
                    if (observedWait) break;
                    await Task.Delay(25);
                }
                Assert.True(observedWait, "Observe the actual Job-row lock wait before expiring the claimed lease.");
                clock.Now = clock.Now.AddMinutes(2); await transaction.CommitAsync(); Assert.Equal(FileUploadStartResult.LeaseLost, await blocked);
            }
            Assert.Null((await repository.OwnedAsync(actor, late.Id, default))!.RootStartedAtUtc);
            await service.CancelAsync(actor, late.Id, default); await Due(late.Id); Assert.True(await worker.TickAsync(default));
            Assert.Equal("Canceled", (await service.GetAsync(actor, late.Id, default)).State);
            var unknown = await Confirm(await Ready("unknown")); gateway.Unknown = true; Assert.True(await worker.TickAsync(default));
            var unknownRow = await service.GetAsync(actor, unknown.Id, default); Assert.Equal("Unknown", unknownRow.State); Assert.True(unknownRow.PartialRetained);
            Assert.Equal(1073756160, unknownRow.ArchiveBytes); Assert.Equal(new string('9', 64), unknownRow.ArchiveSha256);
            // Neither dropping nor changing the bound input proof is permitted,
            // even when the root result is still unknown and no target version exists.
            await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE file_tree_restoring_operations SET archive_bytes=NULL,archive_sha256=NULL WHERE id={unknown.Id}"));
            await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE file_tree_restoring_operations SET archive_bytes=archive_bytes+1 WHERE id={unknown.Id}"));
            var inventory = new FileRecoveryInventory(db, protector, protector, protector, protector, protector, protector, protector, protector, protector);
            Assert.Equal("Verified", (await inventory.GetAsync(FileRecoveryKinds.TreeRestoring, unknown.Id, default))!.Integrity);
            Assert.Contains((await inventory.ListAsync(null, 25, default)).Entries, item => item.Kind == FileRecoveryKinds.TreeRestoring && item.IntentId == unknown.Id);
            Assert.Equal(3, await db.FilePathLocks.CountAsync(item => item.OperationId == unknownRow.ExecutionJobId));
            var beforeEvents = await db.OperationJobEvents.CountAsync(item => item.JobId == unknownRow.ExecutionJobId);
            await Due(unknown.Id); Assert.True(await worker.TickAsync(default)); Assert.Equal(beforeEvents, await db.OperationJobEvents.CountAsync(item => item.JobId == unknownRow.ExecutionJobId));
            unknownRow = await service.GetAsync(actor, unknown.Id, default);
            await service.AcknowledgeAsync(actor, unknown.Id, new(unknownRow.Version, true), default); gateway.Unknown = false;
            var acknowledged = await service.GetAsync(actor, unknown.Id, default);
            Assert.Equal("Acknowledged", acknowledged.State); Assert.Equal(1073756160, acknowledged.ArchiveBytes); Assert.Equal(new string('9', 64), acknowledged.ArchiveSha256);
            Assert.Empty(await db.FilePathLocks.Where(item => item.OperationId == unknownRow.ExecutionJobId).ToArrayAsync());
            foreach (var completedRecovery in new[] { false, true })
            {
                var adopted = await Confirm(await Ready(completedRecovery ? "current-admin-completed" : "current-admin-unknown"));
                gateway.Unknown = !completedRecovery; gateway.LoseResponse = completedRecovery;
                Assert.True(await worker.TickAsync(default)); gateway.Unknown = false;
                Assert.Equal("Unknown", (await service.GetAsync(actor, adopted.Id, default)).State);
                await FileRecoveryWorkflowBoundaries(options, protector, FileRecoveryKinds.TreeRestoring, adopted.Id);
                await FileRecoveryWorkflowBoundaries(options, protector, FileRecoveryKinds.TreeRestoring, adopted.Id, true, false, completedRecovery);
                var disposed = await service.GetAsync(actor, adopted.Id, default);
                Assert.Equal(completedRecovery ? "Succeeded" : "Acknowledged", disposed.State);
                Assert.Equal(captureId, disposed.Command.CaptureId);
                Assert.Empty(await db.FilePathLocks.Where(item => item.OperationId == disposed.ExecutionJobId).ToArrayAsync());
            }
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE audit_events ADD CONSTRAINT native_restoring_audit_failure CHECK (action NOT LIKE 'files.tree-restore.%') NOT VALID");
            var rollbackKey = Guid.NewGuid().ToString("D"); var rollbackId = FileTreeRestoringValidation.IntentId(actor, rollbackKey);
            try { await Assert.ThrowsAsync<DbUpdateException>(() => service.QueueAsync(actor, Command("audit-rollback"), rollbackKey, default)); }
            finally { await db.Database.ExecuteSqlRawAsync("ALTER TABLE audit_events DROP CONSTRAINT native_restoring_audit_failure"); }
            Assert.False(await db.FileTreeRestoringOperations.AnyAsync(item => item.Id == rollbackId)); Assert.False(await db.OperationJobs.AnyAsync(item => item.Id == rollbackId));
            var changed = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE file_tree_restoring_operations SET request_hash={new string('F', 64)} WHERE id={copy.Id}"));
            Assert.Equal("23514", changed.SqlState);
            await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM file_tree_restoring_operations WHERE id={copy.Id}"));
            var waiting = new List<FileTreeRestoringIntentDto>();
            for (var index = 0; index < 4; index++) waiting.Add(await service.QueueAsync(actor, Command("budget-" + index), Guid.NewGuid().ToString("D"), default));
            var budget = await Assert.ThrowsAsync<InvalidOperationException>(() => service.QueueAsync(actor, Command("budget-denied"), Guid.NewGuid().ToString("D"), default));
            Assert.Equal("FILES_OPERATION_BUDGET_EXHAUSTED", budget.Message);
            foreach (var item in waiting) await service.CancelAsync(actor, item.Id, default);
            Assert.Equal(0, await db.FileTreeRestoringOperations.CountAsync(item => item.CompletedAtUtc == null));
            Console.WriteLine("INBOX_PHASE=tree-restore-real-jsonb-two-jobs-confirmation-hierarchy-lease-audit-cancel-recovery-budget");
        }
        finally { File.Delete(keyPath); }
    }
    private sealed class NativeRestoringClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = FileTreeRestoringValidation.Timestamp(DateTimeOffset.UtcNow);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class NativeRestoringIdentity : IIdentityDirectory
    {
        public bool Admin { get; set; } = true;
        public Task<CurrentUserDto> ResolveCurrentAsync(string subject, string? name, IReadOnlyCollection<string> groups, CancellationToken cancellationToken) =>
            Task.FromResult(new CurrentUserDto(subject, name, groups, Admin, DateTimeOffset.UtcNow.AddHours(2)));
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListUsersAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListGroupsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListSsoApplicationsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    // Deliberate transport stub: real PostgreSQL/Application proof only. The
    // installed Agent/UDS/bytes and future human UI have separate evidence.
    private sealed class NativeRestoringGateway : IFileTreeRestoringGateway
    {
        private readonly Dictionary<Guid, FileTreeRestoringReceipt> _receipts = [];
        public bool LoseResponse { get; set; } public bool Unknown { get; set; } public bool ReceiptUnavailable { get; set; } public int Applies { get; private set; }
        public Task<FileTreeRestoringPlan> PlanTreeRestoringAsync(TerminalActor actor, FileTreeRestoringCommand command, CancellationToken cancellationToken) => Task.FromResult(
            new FileTreeRestoringPlan(command, new('9', 64), 1073756160, new('C', 64), 4, 1073741831, new(ulong.MaxValue, 9007199254740993UL), new('D', 64), new('E', 64), new('F', 64)));
        public Task<FileTreeRestoringReceipt?> InspectTreeRestoringAsync(FileTreeRestoringOperation operation, CancellationToken cancellationToken) => ReceiptUnavailable
            ? throw new IOException("test-only receipt transport unavailable") : Task.FromResult(_receipts.GetValueOrDefault(operation.ExecutionJobId!.Value));
        public Task<FileTreeRestoringReceipt> ApplyTreeRestoringAsync(FileTreeRestoringOperation operation, CancellationToken cancellationToken)
        {
            Applies++; var result = Result(operation, Unknown ? "Unknown" : "Completed"); _receipts[operation.ExecutionJobId!.Value] = result;
            if (LoseResponse) { LoseResponse = false; throw new IOException("test-only lost response"); } return Task.FromResult(result);
        }
        public Task<FileTreeRestoringReceipt> DiscardTreeRestoringAsync(FileTreeRestoringOperation operation, CancellationToken cancellationToken)
        {
            var id = operation.ExecutionJobId!.Value; var result = _receipts.GetValueOrDefault(id) ?? Result(operation, "Aborted"); _receipts[id] = result; return Task.FromResult(result);
        }
        private static FileTreeRestoringReceipt Result(FileTreeRestoringOperation row, string state)
        {
            var plan = FileTreeRestoringValidation.ReadPlan(row);
            return new(row.ExecutionJobId!.Value, state, plan.Fingerprint, plan.Command.Format, plan.EntryCount, plan.TotalBytes, plan.ArchiveBytes, plan.ArchiveSha256, state == "Completed" ? new('A', 64) : "", state == "Unknown", "DirectoryRestore", plan.Command.CaptureId);
        }
    }
}
