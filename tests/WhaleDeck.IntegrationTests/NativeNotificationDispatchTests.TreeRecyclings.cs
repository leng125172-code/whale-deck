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
    private static async Task FileTreeRecyclingWorkflowBoundaries(DbContextOptions<PlatformDbContext> options)
    {
        await using var db = new PlatformDbContext(options);
        var keyPath = Path.Combine(Path.GetTempPath(), "wd-native-recycling-" + Guid.NewGuid().ToString("N")); await File.WriteAllBytesAsync(keyPath, RandomNumberGenerator.GetBytes(32));
        try
        {
            IFileTreeRecyclingProtector protector = new FilePlanProtector(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:CapabilityKeyFile"] = keyPath }).Build());
            var clock = new NativeRecyclingClock(); var identity = new NativeRecyclingIdentity(); var gateway = new NativeRecyclingGateway();
            var repository = new FileTreeRecyclingRepository(db, protector, clock);
            var authorization = new FileManagementService(null!, null!, null!, identity, clock);
            var service = new FileTreeRecyclingService(authorization, repository, protector, gateway, clock);
            var worker = new FileTreeRecyclingDispatcher(repository, gateway, identity, protector, clock);
            var actor = new TerminalActor("native-recycling-admin", null, [], new('A', 64), "native-recycling", null);
            FileTreeRecyclingCommand Command(string slug) => new(new("test.treeflow", "source-" + slug), new('B', 64));
            async Task<FileTreeRecyclingIntentDto> Ready(string slug)
            {
                var intent = await service.QueueAsync(actor, Command(slug), Guid.NewGuid().ToString("D"), default);
                Assert.Equal("Queued", intent.State); Assert.Null(intent.ExecutionJobId); Assert.Null(intent.Confirmation);
                Assert.True(await worker.TickAsync(default)); return await service.GetAsync(actor, intent.Id, default);
            }
            async Task<FileTreeRecyclingIntentDto> Confirm(FileTreeRecyclingIntentDto ready) => await service.ConfirmAsync(actor, ready.Id, new(ready.Confirmation!.PlanHash, true, ready.Version), default);
            async Task Due(Guid id) => await db.FileTreeRecyclingOperations.Where(item => item.Id == id).ExecuteUpdateAsync(setters => setters.SetProperty(item => item.NextAttemptAtUtc, clock.GetUtcNow()));
            identity.Admin = false;
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.CapturesAsync(actor, Command("denied").Source, null, 25, default));
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
            Assert.Null(ready.ArchiveBytes); Assert.False(ready.CaptureVerified);
            Assert.Equal("Succeeded", (await db.OperationJobs.AsNoTracking().SingleAsync(item => item.Id == ready.PlanningJobId)).State);
            Assert.Equal(0, gateway.Applies); Assert.Null(ready.ExecutionJobId);
            await using (var reloaded = new PlatformDbContext(options))
            {
                var reloadedService = new FileTreeRecyclingService(authorization, new FileTreeRecyclingRepository(reloaded, protector, clock), protector, gateway, clock);
                Assert.Equal(ready.Confirmation.PlanHash, (await reloadedService.GetAsync(actor, ready.Id, default)).Confirmation!.PlanHash);
                await Assert.ThrowsAsync<KeyNotFoundException>(() => reloadedService.GetAsync(actor with { LoginBinding = new('C', 64) }, ready.Id, default));
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => reloadedService.ConfirmAsync(actor with { LoginBinding = new('C', 64) }, ready.Id, new(ready.Confirmation.PlanHash, true, ready.Version), default));
            }
            var confirmations = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
            {
                await using var concurrent = new PlatformDbContext(options);
                return await new FileTreeRecyclingService(authorization, new FileTreeRecyclingRepository(concurrent, protector, clock), protector, gateway, clock)
                    .ConfirmAsync(actor, ready.Id, new(ready.Confirmation.PlanHash, true, ready.Version), default);
            }));
            Assert.Single(confirmations.Select(item => item.ExecutionJobId).Distinct()); var copy = confirmations[0];
            Assert.NotEqual(copy.PlanningJobId, copy.ExecutionJobId); Assert.Equal(1, await db.FilePathLocks.CountAsync(item => item.OperationId == copy.ExecutionJobId));
            Assert.Equal("files.tree-trash", (await db.OperationJobs.AsNoTracking().SingleAsync(item => item.Id == copy.ExecutionJobId)).JobType);
            var childPlan = new FileMutationPlan(new("copy", new("test.treeflow", "source-one/child"), new("test.treeflow", "elsewhere"), new('A', 64), ""),
                new(1, 2, 0x81A4, 1000, 1000, 1, 7, -1), new(1, 3), new(1, 3), 7, new('B', 64), new('D', 64));
            var conflict = await Assert.ThrowsAsync<InvalidOperationException>(() => new FileMutationRepository(db).EnqueueAsync(actor, Guid.NewGuid(), Guid.NewGuid().ToString("D"), new('E', 64),
                new(actor.Subject, actor.LoginBinding, childPlan, clock.GetUtcNow().AddMinutes(5)), clock.GetUtcNow().AddMinutes(15), default));
            Assert.Equal("FILES_TARGET_BUSY", conflict.Message);
            Assert.True(await worker.TickAsync(default)); Assert.Equal("Succeeded", (await service.GetAsync(actor, copy.Id, default)).State); Assert.Equal(1, gateway.Applies);
            Assert.Empty(await db.FilePathLocks.Where(item => item.OperationId == copy.ExecutionJobId).ToArrayAsync());
            Assert.Equal(copy.ExecutionJobId, (await Confirm(ready)).ExecutionJobId); Assert.Equal(1, gateway.Applies);
            Assert.Equal(1, await db.AuditEvents.CountAsync(item => item.JobId == copy.ExecutionJobId && item.Action == "files.tree-trash.result"));
            Assert.Contains(await db.OutboxMessages.Where(item => item.MessageType == "FileTreeRecyclingRequested").ToArrayAsync(), item => item.ProcessedAtUtc is not null);
            var catalogue = await service.CapturesAsync(actor, Command("one").Source, null, 1, default);
            var captured = Assert.Single(catalogue.Items); Assert.Equal(copy.ExecutionJobId, captured.Id); Assert.Null(catalogue.Next);
            Assert.Equal(1073756160, captured.ArchiveBytes); Assert.Equal(new string('9', 64), captured.ArchiveSha256);
            Assert.Empty((await service.CapturesAsync(actor, Command("different").Source, null, 25, default)).Items);
            await Assert.ThrowsAsync<ArgumentException>(() => service.CapturesAsync(actor, Command("different").Source, captured.Id, 25, default));
            await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE file_tree_captures SET archive_bytes=archive_bytes+1 WHERE id={captured.Id}"));
            await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM file_tree_captures WHERE id={captured.Id}"));
            clock.Now = clock.Now.AddSeconds(1);
            var nextVersion = await service.QueueAsync(actor, Command("one") with { ExpectedVersion = new('8', 64) }, Guid.NewGuid().ToString("D"), default);
            Assert.True(await worker.TickAsync(default)); var nextReady = await service.GetAsync(actor, nextVersion.Id, default);
            var nextCapture = await Confirm(nextReady); Assert.True(await worker.TickAsync(default));
            var firstPage = await service.CapturesAsync(actor, Command("one").Source, null, 1, default);
            Assert.Equal(captured.Id, Assert.Single(firstPage.Items).Id); Assert.Equal(captured.Id, firstPage.Next);
            var secondPage = await service.CapturesAsync(actor, Command("one").Source, firstPage.Next, 1, default);
            Assert.Equal(nextCapture.ExecutionJobId, Assert.Single(secondPage.Items).Id); Assert.Null(secondPage.Next);
            Assert.Equal(new string('8', 64), secondPage.Items[0].Source.ExpectedVersion);
            var resultRollback = await Confirm(await Ready("result-audit-rollback")); var appliesBefore = gateway.Applies;
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE audit_events ADD CONSTRAINT native_recycling_result_audit_failure CHECK (action<>'files.tree-trash.result') NOT VALID");
            try { await Assert.ThrowsAsync<DbUpdateException>(() => worker.TickAsync(default)); }
            finally { await db.Database.ExecuteSqlRawAsync("ALTER TABLE audit_events DROP CONSTRAINT native_recycling_result_audit_failure"); }
            Assert.False(await db.FileTreeCaptures.AnyAsync(item => item.Id == resultRollback.ExecutionJobId));
            Assert.Equal(1, await db.FilePathLocks.CountAsync(item => item.OperationId == resultRollback.ExecutionJobId));
            // Both result commit and deferral deliberately failed with audit.
            // The original 90-second lease still owns this intent: a new
            // worker must not claim it until that actual lease expires.
            Assert.False(await worker.TickAsync(default));
            clock.Now = clock.Now.AddSeconds(91);
            await Due(resultRollback.Id); Assert.True(await worker.TickAsync(default));
            Assert.Equal(appliesBefore + 1, gateway.Applies); Assert.Equal("Succeeded", (await service.GetAsync(actor, resultRollback.Id, default)).State);
            Assert.Single((await service.CapturesAsync(actor, Command("result-audit-rollback").Source, null, 25, default)).Items);
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
            var unavailable = await service.GetAsync(actor, lost.Id, default);
            Assert.Null(unavailable.CaptureMayBeRetained); Assert.Null(unavailable.SourceMayHaveChanged); Assert.Null(unavailable.SourceRemoved); Assert.Null(unavailable.OriginalsRetained);
            Assert.Equal(1, await db.FilePathLocks.CountAsync(item => item.OperationId == lost.ExecutionJobId));
            Assert.Equal("AwaitingTreeReceipt", (await db.OperationJobs.AsNoTracking().SingleAsync(item => item.Id == lost.ExecutionJobId)).Phase);
            var lostEvents = await db.OperationJobEvents.CountAsync(item => item.JobId == lost.ExecutionJobId);
            var lostAudit = await db.AuditEvents.CountAsync(item => item.JobId == lost.ExecutionJobId);
            gateway.ReceiptUnavailable = true; clock.Now = clock.Now.AddSeconds(7); await Due(lost.Id); Assert.True(await worker.TickAsync(default));
            Assert.Equal(lostEvents, await db.OperationJobEvents.CountAsync(item => item.JobId == lost.ExecutionJobId));
            Assert.Equal(lostAudit, await db.AuditEvents.CountAsync(item => item.JobId == lost.ExecutionJobId));
            Assert.Equal(applied, gateway.Applies); Assert.Equal(1, await db.FilePathLocks.CountAsync(item => item.OperationId == lost.ExecutionJobId));
            gateway.ReceiptUnavailable = false;
            clock.Now = clock.Now.AddSeconds(7); await Due(lost.Id); Assert.True(await worker.TickAsync(default));
            Assert.Equal(applied, gateway.Applies); Assert.Equal("Succeeded", (await service.GetAsync(actor, lost.Id, default)).State);
            var missing = await Confirm(await Ready("started-no-receipt")); var claim = Assert.IsType<FileTreeRecyclingOperation>(await repository.ClaimAsync(clock.GetUtcNow(), default));
            Assert.Equal(missing.Id, claim.Id); Assert.Equal(FileUploadStartResult.Started, await repository.StartedAsync(claim, default));
            clock.Now = clock.Now.AddMinutes(2); await Due(missing.Id); applied = gateway.Applies; Assert.True(await worker.TickAsync(default));
            Assert.Equal(applied, gateway.Applies); Assert.Equal("Failed", (await service.GetAsync(actor, missing.Id, default)).State);
            var late = await Confirm(await Ready("late-job-lock")); var lateClaim = Assert.IsType<FileTreeRecyclingOperation>(await repository.ClaimAsync(clock.GetUtcNow(), default));
            Assert.Equal(late.Id, lateClaim.Id);
            await using (var locker = new PlatformDbContext(options))
            {
                await using var transaction = await locker.Database.BeginTransactionAsync();
                await locker.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM operation_jobs WHERE id={late.ExecutionJobId!.Value} FOR UPDATE");
                var blocked = Task.Run(async () => { await using var contender = new PlatformDbContext(options); return await new FileTreeRecyclingRepository(contender, protector, clock).StartedAsync(lateClaim, default); });
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
            var unknownRow = await service.GetAsync(actor, unknown.Id, default); Assert.Equal("Unknown", unknownRow.State); Assert.True(unknownRow.SourceMayHaveChanged);
            Assert.Equal(1073756160, unknownRow.ArchiveBytes); Assert.Equal(new string('9', 64), unknownRow.ArchiveSha256);
            // Neither dropping nor changing the bound input proof is permitted,
            // even when the root result is still unknown and no target version exists.
            await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE file_tree_recycling_operations SET archive_bytes=NULL,archive_sha256=NULL WHERE id={unknown.Id}"));
            await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE file_tree_recycling_operations SET archive_bytes=archive_bytes+1 WHERE id={unknown.Id}"));
            Assert.Equal(1, await db.FilePathLocks.CountAsync(item => item.OperationId == unknownRow.ExecutionJobId));
            var beforeEvents = await db.OperationJobEvents.CountAsync(item => item.JobId == unknownRow.ExecutionJobId);
            await Due(unknown.Id); Assert.True(await worker.TickAsync(default)); Assert.Equal(beforeEvents, await db.OperationJobEvents.CountAsync(item => item.JobId == unknownRow.ExecutionJobId));
            unknownRow = await service.GetAsync(actor, unknown.Id, default);
            await service.AcknowledgeAsync(actor, unknown.Id, new(unknownRow.Version, true), default); gateway.Unknown = false;
            var acknowledged = await service.GetAsync(actor, unknown.Id, default);
            Assert.Equal("Acknowledged", acknowledged.State); Assert.Equal(1073756160, acknowledged.ArchiveBytes); Assert.Equal(new string('9', 64), acknowledged.ArchiveSha256);
            Assert.Empty(await db.FilePathLocks.Where(item => item.OperationId == unknownRow.ExecutionJobId).ToArrayAsync());
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE audit_events ADD CONSTRAINT native_recycling_audit_failure CHECK (action NOT LIKE 'files.tree-trash.%') NOT VALID");
            var rollbackKey = Guid.NewGuid().ToString("D"); var rollbackId = FileTreeRecyclingValidation.IntentId(actor, rollbackKey);
            try { await Assert.ThrowsAsync<DbUpdateException>(() => service.QueueAsync(actor, Command("audit-rollback"), rollbackKey, default)); }
            finally { await db.Database.ExecuteSqlRawAsync("ALTER TABLE audit_events DROP CONSTRAINT native_recycling_audit_failure"); }
            Assert.False(await db.FileTreeRecyclingOperations.AnyAsync(item => item.Id == rollbackId)); Assert.False(await db.OperationJobs.AnyAsync(item => item.Id == rollbackId));
            var changed = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE file_tree_recycling_operations SET request_hash={new string('F', 64)} WHERE id={copy.Id}"));
            Assert.Equal("23514", changed.SqlState);
            await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM file_tree_recycling_operations WHERE id={copy.Id}"));
            var waiting = new List<FileTreeRecyclingIntentDto>();
            for (var index = 0; index < 4; index++) waiting.Add(await service.QueueAsync(actor, Command("budget-" + index), Guid.NewGuid().ToString("D"), default));
            var budget = await Assert.ThrowsAsync<InvalidOperationException>(() => service.QueueAsync(actor, Command("budget-denied"), Guid.NewGuid().ToString("D"), default));
            Assert.Equal("FILES_OPERATION_BUDGET_EXHAUSTED", budget.Message);
            foreach (var item in waiting) await service.CancelAsync(actor, item.Id, default);
            Assert.Equal(0, await db.FileTreeRecyclingOperations.CountAsync(item => item.CompletedAtUtc == null));
            Console.WriteLine("INBOX_PHASE=tree-trash-real-jsonb-two-jobs-confirmation-hierarchy-lease-audit-cancel-recovery-budget");
        }
        finally { File.Delete(keyPath); }
    }
    private sealed class NativeRecyclingClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = FileTreeRecyclingValidation.Timestamp(DateTimeOffset.UtcNow);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class NativeRecyclingIdentity : IIdentityDirectory
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
    private sealed class NativeRecyclingGateway : IFileTreeRecyclingGateway
    {
        private readonly Dictionary<Guid, FileTreeRecyclingReceipt> _receipts = [];
        public bool LoseResponse { get; set; } public bool Unknown { get; set; } public bool ReceiptUnavailable { get; set; } public int Applies { get; private set; }
        public Task<FileTreeRecyclingPlan> PlanTreeRecyclingAsync(TerminalActor actor, FileTreeRecyclingCommand command, CancellationToken cancellationToken) => Task.FromResult(
            new FileTreeRecyclingPlan(command, new(ulong.MaxValue, 9007199254740993UL), new('D', 64), new('C', 64), 4, 1073741831, new('F', 64)));
        public Task<FileTreeRecyclingReceipt?> InspectTreeRecyclingAsync(FileTreeRecyclingOperation operation, CancellationToken cancellationToken) => ReceiptUnavailable
            ? throw new IOException("test-only receipt transport unavailable") : Task.FromResult(_receipts.GetValueOrDefault(operation.ExecutionJobId!.Value));
        public Task<FileTreeRecyclingReceipt> ApplyTreeRecyclingAsync(FileTreeRecyclingOperation operation, CancellationToken cancellationToken)
        {
            Applies++; var result = Result(operation, Unknown ? "Unknown" : "Completed"); _receipts[operation.ExecutionJobId!.Value] = result;
            if (LoseResponse) { LoseResponse = false; throw new IOException("test-only lost response"); } return Task.FromResult(result);
        }
        public Task<FileTreeRecyclingReceipt> DiscardTreeRecyclingAsync(FileTreeRecyclingOperation operation, CancellationToken cancellationToken)
        {
            var id = operation.ExecutionJobId!.Value; var result = _receipts.GetValueOrDefault(id) ?? Result(operation, "Aborted"); _receipts[id] = result; return Task.FromResult(result);
        }
        private static FileTreeRecyclingReceipt Result(FileTreeRecyclingOperation row, string state)
        {
            var plan = FileTreeRecyclingValidation.ReadPlan(row);
            var verified = state is "Completed" or "Unknown";
            return new(row.ExecutionJobId!.Value, state, plan.Fingerprint, row.ExecutionJobId.Value, plan.ManifestSha256, plan.EntryCount, plan.TotalBytes,
                verified, verified, verified ? 1073756160 : 0, verified ? new('9', 64) : "", verified, state == "Completed", state == "Completed");
        }
    }
}
