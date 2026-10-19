using System.Text.Json;
using Microsoft.EntityFrameworkCore;
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
    // Real SQL, immutable source material and transactions. Root transport is
    // deliberately a stub; this is not evidence of filesystem or browser work.
    private static async Task FileRecoveryWorkflowBoundaries(DbContextOptions<PlatformDbContext> options,
        FilePlanProtector protector, string kind, Guid originalId, bool dispose = false, bool exclusiveMove = false)
    {
        await using var db = new PlatformDbContext(options);
        var material = new FileRecoveryMaterial(protector, protector, protector, protector, protector, protector);
        var clock = new NativeRecoveryClock();
        FileRecoveryRepository Repository(PlatformDbContext context) => new(context, new(context, material, clock), material, new(context, protector), protector, clock);
        var repository = Repository(db); var identity = new NativeRecoveryIdentity();
        var authorization = new FileManagementService(null!, null!, null!, identity, clock);
        var service = new FileRecoveryService(authorization, repository, protector, clock);
        var gateway = new NativeRecoveryGateway(material); var worker = new FileRecoveryDispatcher(repository, gateway, identity, protector, clock);
        var actor = new TerminalActor(NativeRecoveryIdentity.CurrentSubject, null, [], new('F', 64), "native-current-recovery", null);
        var before = (await new FileRecoveryInventory(db, protector, protector, protector, protector, protector, protector).GetAsync(kind, originalId, default))!;
        var originalExecution = before.ExecutionJobId!.Value;
        var lockCount = await db.FilePathLocks.CountAsync(row => row.OperationId == originalExecution);
        var key = Guid.NewGuid().ToString("D"); var command = new QueueFileRecoveryCommand(kind, originalId, before.Version, "AcknowledgeUnknown", "OriginalSessionLost");
        var queued = await service.QueueAsync(actor, command, key, default);
        Assert.Equal(queued.Id, (await service.GetByKeyAsync(actor, key, default)).Id);
        Assert.Equal(queued.Id, (await service.QueueAsync(actor, command, key, default)).Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.QueueAsync(actor, command with { ReasonCode = "OperatorHandover" }, key, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.QueueAsync(actor with { LoginBinding = new('E', 64) }, command, Guid.NewGuid().ToString("D"), default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetAsync(actor with { LoginBinding = new('E', 64) }, queued.Id, default));
        var stored = (await repository.OwnedAsync(actor, queued.Id, default))!;
        Assert.True(FileRecoveryValidation.Bound(stored, protector));
        Assert.NotEqual(actor.Subject, JsonDocument.Parse(stored.OriginalJson).RootElement.GetProperty("ActorSubject").GetString());
        Assert.Equal(before, await new FileRecoveryInventory(db, protector, protector, protector, protector, protector, protector).GetAsync(kind, originalId, default));
        Assert.Equal(lockCount, await db.FilePathLocks.CountAsync(row => row.OperationId == originalExecution));
        if (exclusiveMove)
        {
            // At this point the original move is the only nonterminal move.
            Assert.Null(await new FileTreeMoveRepository(db, protector, clock).ClaimAsync(clock.GetUtcNow(), default));
        }
        if (kind == FileRecoveryKinds.TreePacking)
            Assert.Null(await new FileTreePackingRepository(db, protector, clock).ClaimAsync(clock.GetUtcNow(), default));
        Assert.True(await worker.TickAsync(default)); var ready = await service.GetAsync(actor, queued.Id, default);
        Assert.Equal("Ready", ready.State); Assert.NotNull(ready.Confirmation); Assert.Equal(0, gateway.Fences);
        await using (var reloaded = new PlatformDbContext(options))
        {
            var reloadedService = new FileRecoveryService(authorization, Repository(reloaded), protector, clock);
            Assert.Equal(ready.Confirmation.PlanHash, (await reloadedService.GetAsync(actor, ready.Id, default)).Confirmation!.PlanHash);
        }
        Assert.Equal(before, await new FileRecoveryInventory(db, protector, protector, protector, protector, protector, protector).GetAsync(kind, originalId, default));
        if (!dispose)
        {
            await service.CancelAsync(actor, queued.Id, default);
            Assert.Equal("Canceled", (await service.GetAsync(actor, queued.Id, default)).State);
            Assert.Equal(before, await new FileRecoveryInventory(db, protector, protector, protector, protector, protector, protector).GetAsync(kind, originalId, default));
            Assert.Equal(lockCount, await db.FilePathLocks.CountAsync(row => row.OperationId == originalExecution));
            Assert.Equal("Succeeded", (await db.OperationJobs.AsNoTracking().SingleAsync(row => row.Id == ready.PlanningJobId)).State);
        }
        else
        {
            var confirmed = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
            {
                await using var concurrent = new PlatformDbContext(options);
                return await new FileRecoveryService(authorization, Repository(concurrent), protector, clock)
                    .ConfirmAsync(actor, ready.Id, new(ready.Confirmation.PlanHash, true, ready.Version), default);
            }));
            Assert.Single(confirmed.Select(row => row.ExecutionJobId).Distinct());
            var execution = confirmed[0].ExecutionJobId!.Value;
            Assert.NotEqual(originalExecution, execution); Assert.NotEqual(ready.PlanningJobId, execution);
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE audit_events ADD CONSTRAINT native_recovery_audit_failure CHECK (action<>'files.recovery.original-disposed') NOT VALID");
            try { Assert.True(await worker.TickAsync(default)); }
            finally { await db.Database.ExecuteSqlRawAsync("ALTER TABLE audit_events DROP CONSTRAINT native_recovery_audit_failure"); }
            // Root fence happened, but audit failure must roll back the original
            // result, its events, and its lock release in the same transaction.
            Assert.Equal(1, gateway.Fences);
            Assert.Equal("Unknown", (await service.GetAsync(actor, ready.Id, default)).State);
            Assert.Equal(before, await new FileRecoveryInventory(db, protector, protector, protector, protector, protector, protector).GetAsync(kind, originalId, default));
            Assert.Equal(lockCount, await db.FilePathLocks.CountAsync(row => row.OperationId == originalExecution));
            await db.FileRecoveryOperations.Where(row => row.Id == ready.Id).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.NextAttemptAtUtc, clock.GetUtcNow()));
            if (exclusiveMove)
            {
                await using var locker = new PlatformDbContext(options);
                await using var transaction = await locker.Database.BeginTransactionAsync();
                await locker.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM operation_jobs WHERE id={originalExecution} FOR UPDATE");
                var waiting = Task.Run(async () =>
                {
                    await using var concurrent = new PlatformDbContext(options);
                    return await new FileRecoveryDispatcher(Repository(concurrent), gateway, identity, protector, clock).TickAsync(default);
                });
                var observed = false;
                for (var attempt = 0; attempt < 40; attempt++)
                {
                    observed = await db.Database.SqlQueryRaw<bool>("SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE usename=current_user AND pid<>pg_backend_pid() AND wait_event_type='Lock' AND query LIKE '%operation_jobs%' AND query LIKE '%FOR UPDATE%') AS \"Value\"").SingleAsync();
                    if (observed) break;
                    await Task.Delay(25);
                }
                Assert.True(observed, "Observe the real original Job lock wait before expiring the recovery lease.");
                clock.Now = clock.Now.AddMinutes(2); await transaction.CommitAsync(); Assert.True(await waiting);
            }
            // Even after the source result and path release were staged, the
            // post-wait lease check must roll back all original changes.
            Assert.Equal(before, await new FileRecoveryInventory(db, protector, protector, protector, protector, protector, protector).GetAsync(kind, originalId, default));
            Assert.Equal(lockCount, await db.FilePathLocks.CountAsync(row => row.OperationId == originalExecution));
            Assert.True(await worker.TickAsync(default)); var disposed = await service.GetAsync(actor, ready.Id, default);
            var resolution = kind == FileRecoveryKinds.Upload ? "Failed" : "Acknowledged";
            Assert.Equal("Disposed", disposed.State); Assert.Equal(resolution, disposed.OriginalResolution); Assert.Equal(1, gateway.Fences);
            Assert.Empty(await db.FilePathLocks.Where(row => row.OperationId == originalExecution).ToArrayAsync());
            var original = kind switch {
                FileRecoveryKinds.Upload => await db.FileUploadOperations.Where(row => row.Id == originalId).Select(row => new RecoveryOriginalFacts(row.State, row.ActorSubject, row.LoginBinding, row.PublishedVersion)).SingleAsync(),
                FileRecoveryKinds.Mutation => await db.FileMutationOperations.Where(row => row.Id == originalId).Select(row => new RecoveryOriginalFacts(row.State, row.ActorSubject, row.LoginBinding, row.PublishedVersion)).SingleAsync(),
                FileRecoveryKinds.Archive => await db.FileArchiveOperations.Where(row => row.Id == originalId).Select(row => new RecoveryOriginalFacts(row.State, row.ActorSubject, row.LoginBinding, row.PublishedVersion)).SingleAsync(),
                FileRecoveryKinds.TreeCopy => await db.FileTreeCopyOperations.Where(row => row.Id == originalId).Select(row => new RecoveryOriginalFacts(row.State, row.ActorSubject, row.LoginBinding, row.PublishedVersion)).SingleAsync(),
                FileRecoveryKinds.TreePacking => await db.FileTreePackingOperations.Where(row => row.Id == originalId).Select(row => new RecoveryOriginalFacts(row.State, row.ActorSubject, row.LoginBinding, row.PublishedVersion)).SingleAsync(),
                _ => await db.FileTreeMoveOperations.Where(row => row.Id == originalId).Select(row => new RecoveryOriginalFacts(row.State, row.ActorSubject, row.LoginBinding, row.PublishedVersion)).SingleAsync()
            };
            Assert.Equal(resolution, original.State); Assert.NotEqual(actor.Subject, original.ActorSubject);
            Assert.NotEqual(actor.LoginBinding, original.LoginBinding); Assert.Null(original.PublishedVersion);
            Assert.Equal("Failed", (await db.OperationJobs.AsNoTracking().SingleAsync(row => row.Id == originalExecution)).State);
            Assert.Equal("Succeeded", (await db.OperationJobs.AsNoTracking().SingleAsync(row => row.Id == execution)).State);
            var audit = await db.AuditEvents.AsNoTracking().SingleAsync(row => row.JobId == execution && row.Action == "files.recovery.original-disposed");
            Assert.Equal(actor.Subject, audit.ActorSubject); Assert.DoesNotContain("LoginBinding", audit.DetailJson, StringComparison.Ordinal);
            Assert.DoesNotContain("OriginalJson", audit.DetailJson, StringComparison.Ordinal);
            Assert.Equal(execution, (await service.ConfirmAsync(actor, ready.Id, new(ready.Confirmation.PlanHash, true, ready.Version), default)).ExecutionJobId);
        }
        Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM file_recovery_operations WHERE id={ready.Id}"))).SqlState);
        Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE file_recovery_operations SET request_hash={new string('9', 64)} WHERE id={ready.Id}"))).SqlState);
        Console.WriteLine("INBOX_PHASE=file-recovery-" + kind + (dispose ? "-atomic-current-admin-disposition" : "-reservation-plan-cancel-immutable"));
    }
    private sealed record RecoveryOriginalFacts(string State, string ActorSubject, string LoginBinding, string? PublishedVersion);
    private sealed class NativeRecoveryClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = FileTreeCopyValidation.Timestamp(DateTimeOffset.UtcNow);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class NativeRecoveryIdentity : IIdentityDirectory
    {
        internal const string CurrentSubject = "new-current-recovery-admin";
        public Task<CurrentUserDto> ResolveCurrentAsync(string subject, string? name, IReadOnlyCollection<string> groups, CancellationToken cancellationToken) =>
            Task.FromResult(new CurrentUserDto(subject, name, groups, subject == CurrentSubject, DateTimeOffset.UtcNow.AddHours(2)));
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListUsersAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListGroupsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListSsoApplicationsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    private sealed class NativeRecoveryGateway(FileRecoveryMaterial material) : IFileRecoveryGateway
    {
        public int Fences { get; private set; }
        public Task<FileRecoveryObservation> InspectForRecoveryAsync(FileRecoverySnapshot original, TerminalActor currentActor, CancellationToken cancellationToken) => Task.FromResult(Observe(original, currentActor));
        public Task<FileRecoveryObservation> FenceForRecoveryAsync(FileRecoverySnapshot original, TerminalActor currentActor, CancellationToken cancellationToken)
        { Fences++; return Task.FromResult(Observe(original, currentActor)); }
        private FileRecoveryObservation Observe(FileRecoverySnapshot original, TerminalActor actor)
        {
            Assert.Equal(NativeRecoveryIdentity.CurrentSubject, actor.Subject); object receipt;
            switch (original.Kind)
            {
                case FileRecoveryKinds.Upload:
                    var upload = material.Read<FileUploadOperation>(original); receipt = new FileUploadReceiptDto("Unknown", upload.SizeBytes, upload.Sha256, "", false, false, false); break;
                case FileRecoveryKinds.Mutation:
                    var mutation = FileMutationService.ReadPlan(material.Read<FileMutationOperation>(original));
                    receipt = new FileMutationReceipt(mutation.Command.Kind, "Unknown", mutation.Fingerprint, "", mutation.SizeBytes, mutation.Sha256, false, false); break;
                case FileRecoveryKinds.Archive:
                    var archive = FileArchiveValidation.ReadPlan(material.Read<FileArchiveOperation>(original));
                    receipt = new FileArchiveReceipt(archive.Command.Kind, "Unknown", archive.Fingerprint, "", archive.SizeBytes, archive.Sha256, false, false,
                        archive.Command.Kind == "trash" ? original.Binding.IntentId : archive.Command.VersionId!.Value, null); break;
                case FileRecoveryKinds.TreeCopy:
                    var copy = FileTreeCopyValidation.ReadPlan(material.Read<FileTreeCopyOperation>(original));
                    receipt = new FileTreeCopyReceipt(original.Binding.ExecutionJobId, "Unknown", copy.Fingerprint, copy.EntryCount, copy.TotalBytes, "", true); break;
                case FileRecoveryKinds.TreePacking:
                    var packing = FileTreePackingValidation.ReadPlan(material.Read<FileTreePackingOperation>(original));
                    receipt = new FileTreePackingReceipt(original.Binding.ExecutionJobId, "Unknown", packing.Fingerprint, packing.Command.Format, packing.EntryCount, packing.TotalBytes, 0, "", "", true); break;
                default:
                    var move = FileTreeMoveValidation.ReadPlan(material.Read<FileTreeMoveOperation>(original));
                    receipt = new FileTreeMoveReceipt(original.Binding.ExecutionJobId, "Unknown", move.Fingerprint, move.EntryCount, move.TotalBytes, "", true, false, false, true); break;
            }
            var json = JsonSerializer.Serialize(receipt, receipt.GetType());
            var observation = new FileRecoveryObservation("Unknown", FileRecoveryValidation.ReceiptHash(original.Kind, original.Binding.ExecutionJobId, "Unknown", json), false) { ReceiptJson = json };
            material.ValidateObservation(original, observation); return observation;
        }
    }
}
