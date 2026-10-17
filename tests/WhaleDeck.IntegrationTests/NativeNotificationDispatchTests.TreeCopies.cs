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
    private static async Task FileTreeCopyWorkflowBoundaries(DbContextOptions<PlatformDbContext> options)
    {
        await using var db = new PlatformDbContext(options);
        var keyPath = Path.Combine(Path.GetTempPath(), "wd-native-tree-" + Guid.NewGuid().ToString("N")); await File.WriteAllBytesAsync(keyPath, RandomNumberGenerator.GetBytes(32));
        try
        {
            IFileTreeCopyProtector protector = new FilePlanProtector(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:CapabilityKeyFile"] = keyPath }).Build());
            var clock = new NativeTreeClock(); var identity = new NativeTreeIdentity(); var gateway = new NativeTreeGateway();
            var repository = new FileTreeCopyRepository(db, protector, clock);
            var authorization = new FileManagementService(null!, null!, null!, identity, clock);
            var service = new FileTreeCopyService(authorization, repository, protector, gateway, clock);
            var worker = new FileTreeCopyDispatcher(repository, gateway, identity, protector, clock);
            var actor = new TerminalActor("native-tree-admin", null, [], new('A', 64), "native-tree", null);
            FileTreeCopyCommand Command(string slug) => new(new("test.treeflow", "source-" + slug), new('B', 64), new("test.treeflow", "target-" + slug));
            async Task<FileTreeCopyIntentDto> Ready(string slug)
            {
                var intent = await service.QueueAsync(actor, Command(slug), Guid.NewGuid().ToString("D"), default);
                Assert.Equal("Queued", intent.State); Assert.Null(intent.ExecutionJobId); Assert.Null(intent.Confirmation);
                Assert.True(await worker.TickAsync(default)); return await service.GetAsync(actor, intent.Id, default);
            }
            async Task<FileTreeCopyIntentDto> Confirm(FileTreeCopyIntentDto ready) => await service.ConfirmAsync(actor, ready.Id, new(ready.Confirmation!.PlanHash, true, ready.Version), default);
            async Task Due(Guid id) => await db.FileTreeCopyOperations.Where(item => item.Id == id).ExecuteUpdateAsync(setters => setters.SetProperty(item => item.NextAttemptAtUtc, clock.GetUtcNow()));
            identity.Admin = false;
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.QueueAsync(actor, Command("denied"), Guid.NewGuid().ToString("D"), default)); identity.Admin = true;
            var requestKey = Guid.NewGuid().ToString("D"); var queued = await service.QueueAsync(actor, Command("one"), requestKey, default);
            Assert.Equal(queued.Id, (await service.QueueAsync(actor, Command("one"), requestKey, default)).Id);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.QueueAsync(actor, Command("different"), requestKey, default));
            Assert.Null((await repository.OwnedAsync(actor with { LoginBinding = new('C', 64) }, queued.Id, default)));
            Assert.Empty(await db.FilePathLocks.Where(item => item.OperationId == queued.Id).ToArrayAsync());
            Assert.True(await worker.TickAsync(default)); var ready = await service.GetAsync(actor, queued.Id, default);
            Assert.Equal("Ready", ready.State); Assert.NotNull(ready.Confirmation); Assert.True(ready.TotalBytes > FileManagementService.MaximumBytes);
            Assert.Equal("Succeeded", (await db.OperationJobs.AsNoTracking().SingleAsync(item => item.Id == ready.PlanningJobId)).State);
            Assert.Equal(0, gateway.Applies); Assert.Null(ready.ExecutionJobId);
            await using (var reloaded = new PlatformDbContext(options))
            {
                var reloadedService = new FileTreeCopyService(authorization, new FileTreeCopyRepository(reloaded, protector, clock), protector, gateway, clock);
                Assert.Equal(ready.Confirmation.PlanHash, (await reloadedService.GetAsync(actor, ready.Id, default)).Confirmation!.PlanHash);
                await Assert.ThrowsAsync<KeyNotFoundException>(() => reloadedService.GetAsync(actor with { LoginBinding = new('C', 64) }, ready.Id, default));
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => reloadedService.ConfirmAsync(actor with { LoginBinding = new('C', 64) }, ready.Id, new(ready.Confirmation.PlanHash, true, ready.Version), default));
            }
            var confirmations = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
            {
                await using var concurrent = new PlatformDbContext(options);
                return await new FileTreeCopyService(authorization, new FileTreeCopyRepository(concurrent, protector, clock), protector, gateway, clock)
                    .ConfirmAsync(actor, ready.Id, new(ready.Confirmation.PlanHash, true, ready.Version), default);
            }));
            Assert.Single(confirmations.Select(item => item.ExecutionJobId).Distinct()); var copy = confirmations[0];
            Assert.NotEqual(copy.PlanningJobId, copy.ExecutionJobId); Assert.Equal(2, await db.FilePathLocks.CountAsync(item => item.OperationId == copy.ExecutionJobId));
            Assert.Equal("files.tree-copy", (await db.OperationJobs.AsNoTracking().SingleAsync(item => item.Id == copy.ExecutionJobId)).JobType);
            var childPlan = new FileMutationPlan(new("copy", new("test.treeflow", "source-one/child"), new("test.treeflow", "elsewhere"), new('A', 64), ""),
                new(1, 2, 0x81A4, 1000, 1000, 1, 7, -1), new(1, 3), new(1, 3), 7, new('B', 64), new('D', 64));
            var conflict = await Assert.ThrowsAsync<InvalidOperationException>(() => new FileMutationRepository(db).EnqueueAsync(actor, Guid.NewGuid(), Guid.NewGuid().ToString("D"), new('E', 64),
                new(actor.Subject, actor.LoginBinding, childPlan, clock.GetUtcNow().AddMinutes(5)), clock.GetUtcNow().AddMinutes(15), default));
            Assert.Equal("FILES_TARGET_BUSY", conflict.Message);
            Assert.True(await worker.TickAsync(default)); Assert.Equal("Succeeded", (await service.GetAsync(actor, copy.Id, default)).State); Assert.Equal(1, gateway.Applies);
            Assert.Empty(await db.FilePathLocks.Where(item => item.OperationId == copy.ExecutionJobId).ToArrayAsync());
            Assert.Equal(copy.ExecutionJobId, (await Confirm(ready)).ExecutionJobId); Assert.Equal(1, gateway.Applies);
            Assert.Equal(1, await db.AuditEvents.CountAsync(item => item.JobId == copy.ExecutionJobId && item.Action == "files.tree-copy.result"));
            Assert.Contains(await db.OutboxMessages.Where(item => item.MessageType == "FileTreeCopyRequested").ToArrayAsync(), item => item.ProcessedAtUtc is not null);
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
            clock.Now = clock.Now.AddSeconds(7); await Due(lost.Id); Assert.True(await worker.TickAsync(default));
            Assert.Equal(applied, gateway.Applies); Assert.Equal("Succeeded", (await service.GetAsync(actor, lost.Id, default)).State);
            var missing = await Confirm(await Ready("started-no-receipt")); var claim = Assert.IsType<FileTreeCopyOperation>(await repository.ClaimAsync(clock.GetUtcNow(), default));
            Assert.Equal(missing.Id, claim.Id); Assert.Equal(FileUploadStartResult.Started, await repository.StartedAsync(claim, default));
            clock.Now = clock.Now.AddMinutes(2); await Due(missing.Id); applied = gateway.Applies; Assert.True(await worker.TickAsync(default));
            Assert.Equal(applied, gateway.Applies); Assert.Equal("Failed", (await service.GetAsync(actor, missing.Id, default)).State);
            var late = await Confirm(await Ready("late-job-lock")); var lateClaim = Assert.IsType<FileTreeCopyOperation>(await repository.ClaimAsync(clock.GetUtcNow(), default));
            Assert.Equal(late.Id, lateClaim.Id);
            await using (var locker = new PlatformDbContext(options))
            {
                await using var transaction = await locker.Database.BeginTransactionAsync();
                await locker.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM operation_jobs WHERE id={late.ExecutionJobId!.Value} FOR UPDATE");
                var blocked = Task.Run(async () => { await using var contender = new PlatformDbContext(options); return await new FileTreeCopyRepository(contender, protector, clock).StartedAsync(lateClaim, default); });
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
            Assert.Equal(2, await db.FilePathLocks.CountAsync(item => item.OperationId == unknownRow.ExecutionJobId));
            var beforeEvents = await db.OperationJobEvents.CountAsync(item => item.JobId == unknownRow.ExecutionJobId);
            await Due(unknown.Id); Assert.True(await worker.TickAsync(default)); Assert.Equal(beforeEvents, await db.OperationJobEvents.CountAsync(item => item.JobId == unknownRow.ExecutionJobId));
            unknownRow = await service.GetAsync(actor, unknown.Id, default);
            await service.AcknowledgeAsync(actor, unknown.Id, new(unknownRow.Version, true), default); gateway.Unknown = false;
            Assert.Equal("Acknowledged", (await service.GetAsync(actor, unknown.Id, default)).State);
            Assert.Empty(await db.FilePathLocks.Where(item => item.OperationId == unknownRow.ExecutionJobId).ToArrayAsync());
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE audit_events ADD CONSTRAINT native_tree_audit_failure CHECK (action NOT LIKE 'files.tree-copy.%') NOT VALID");
            var rollbackKey = Guid.NewGuid().ToString("D"); var rollbackId = FileTreeCopyValidation.IntentId(actor, rollbackKey);
            try { await Assert.ThrowsAsync<DbUpdateException>(() => service.QueueAsync(actor, Command("audit-rollback"), rollbackKey, default)); }
            finally { await db.Database.ExecuteSqlRawAsync("ALTER TABLE audit_events DROP CONSTRAINT native_tree_audit_failure"); }
            Assert.False(await db.FileTreeCopyOperations.AnyAsync(item => item.Id == rollbackId)); Assert.False(await db.OperationJobs.AnyAsync(item => item.Id == rollbackId));
            var changed = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE file_tree_copy_operations SET request_hash={new string('F', 64)} WHERE id={copy.Id}"));
            Assert.Equal("23514", changed.SqlState);
            await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM file_tree_copy_operations WHERE id={copy.Id}"));
            var waiting = new List<FileTreeCopyIntentDto>();
            for (var index = 0; index < 4; index++) waiting.Add(await service.QueueAsync(actor, Command("budget-" + index), Guid.NewGuid().ToString("D"), default));
            var budget = await Assert.ThrowsAsync<InvalidOperationException>(() => service.QueueAsync(actor, Command("budget-denied"), Guid.NewGuid().ToString("D"), default));
            Assert.Equal("FILES_OPERATION_BUDGET_EXHAUSTED", budget.Message);
            foreach (var item in waiting) await service.CancelAsync(actor, item.Id, default);
            Assert.Equal(0, await db.FileTreeCopyOperations.CountAsync(item => item.CompletedAtUtc == null));
            Console.WriteLine("INBOX_PHASE=tree-copy-real-jsonb-two-jobs-confirmation-hierarchy-lease-audit-cancel-recovery-budget");
        }
        finally { File.Delete(keyPath); }
    }
    private sealed class NativeTreeClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = FileTreeCopyValidation.Timestamp(DateTimeOffset.UtcNow);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class NativeTreeIdentity : IIdentityDirectory
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
    private sealed class NativeTreeGateway : IFileTreeCopyGateway
    {
        private readonly Dictionary<Guid, FileTreeCopyReceipt> _receipts = [];
        public bool LoseResponse { get; set; } public bool Unknown { get; set; } public int Applies { get; private set; }
        public Task<FileTreeCopyPlan> PlanTreeCopyAsync(TerminalActor actor, FileTreeCopyCommand command, CancellationToken cancellationToken) => Task.FromResult(
            new FileTreeCopyPlan(command, new('C', 64), 4, 1073741831, new(ulong.MaxValue, 9007199254740993UL), new('D', 64), new('E', 64), new('F', 64)));
        public Task<FileTreeCopyReceipt?> InspectTreeCopyAsync(FileTreeCopyOperation operation, CancellationToken cancellationToken) => Task.FromResult(_receipts.GetValueOrDefault(operation.ExecutionJobId!.Value));
        public Task<FileTreeCopyReceipt> ApplyTreeCopyAsync(FileTreeCopyOperation operation, CancellationToken cancellationToken)
        {
            Applies++; var result = Result(operation, Unknown ? "Unknown" : "Completed"); _receipts[operation.ExecutionJobId!.Value] = result;
            if (LoseResponse) { LoseResponse = false; throw new IOException("test-only lost response"); } return Task.FromResult(result);
        }
        public Task<FileTreeCopyReceipt> DiscardTreeCopyAsync(FileTreeCopyOperation operation, CancellationToken cancellationToken)
        {
            var id = operation.ExecutionJobId!.Value; var result = _receipts.GetValueOrDefault(id) ?? Result(operation, "Aborted"); _receipts[id] = result; return Task.FromResult(result);
        }
        private static FileTreeCopyReceipt Result(FileTreeCopyOperation row, string state)
        {
            var plan = FileTreeCopyValidation.ReadPlan(row);
            return new(row.ExecutionJobId!.Value, state, plan.Fingerprint, plan.EntryCount, plan.TotalBytes, state == "Completed" ? new('A', 64) : "", state == "Unknown");
        }
    }
}
