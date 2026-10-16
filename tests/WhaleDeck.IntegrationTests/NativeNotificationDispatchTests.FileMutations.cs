using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Agent;
using WhaleDeck.Infrastructure.Persistence;

namespace WhaleDeck.IntegrationTests;

public sealed partial class NativeNotificationDispatchTests
{
    private static async Task FileMutationBoundaries(DbContextOptions<PlatformDbContext> options)
    {
        await using var db = new PlatformDbContext(options);
        var repository = new FileMutationRepository(db); var uploads = new FileUploadRepository(db);
        var key = Path.Combine(Path.GetTempPath(), "wd-native-mutation-" + Guid.NewGuid().ToString("N"));
        await File.WriteAllBytesAsync(key, RandomNumberGenerator.GetBytes(32));
        try
        {
            IFileMutationProtector protector = new FilePlanProtector(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:CapabilityKeyFile"] = key }).Build());
            var actor = new TerminalActor("native-mutation-admin", null, [], new('A', 64), "native-mutation-trace", null);
            var gateway = new NativeMutationGateway(); var identity = new NativeMutationIdentity();
            var authorization = new FileManagementService(null!, null!, null!, identity, TimeProvider.System);
            var service = new FileMutationService(authorization, gateway, repository, protector, TimeProvider.System);
            var worker = new FileMutationDispatcher(repository, gateway, identity, TimeProvider.System, protector);
            FileMutationCommand Command(string target) => new("copy", new("test.mutation", "source-" + target), new("test.mutation", target), new('B', 64), "");
            async Task<(FileMutationIntentDto Intent, SubmitFileMutationCommand Request)> Create(string target)
            {
                var plan = await service.PlanAsync(actor, Command(target), default); var request = new SubmitFileMutationCommand(plan.PlanHash, true, Guid.NewGuid().ToString("D"));
                return (await service.SubmitAsync(actor, request, default), request);
            }
            async Task Due(Guid id) => await db.FileMutationOperations.Where(item => item.Id == id).ExecuteUpdateAsync(setters => setters.SetProperty(item => item.NextAttemptAtUtc, DateTimeOffset.UtcNow.AddMinutes(-1)));
            async Task<FileUploadOperation> Upload(string path) => await uploads.EnqueueAsync(actor, Guid.NewGuid(), Guid.NewGuid().ToString("D"), new('C', 64),
                new(actor.Subject, actor.LoginBinding, new("test.mutation", path, 3, new('B', 64), "ABSENT"), DateTimeOffset.UtcNow.AddMinutes(5)), DateTimeOffset.UtcNow.AddMinutes(15), default);
            var first = await Create("roundtrip"); var id = first.Intent.Id;
            Assert.Equal("Pending", first.Intent.State); Assert.Equal(2, await db.FilePathLocks.CountAsync(item => item.OperationId == id));
            Assert.Equal(first.Intent, await service.SubmitAsync(actor, first.Request, default));
            Assert.Equal(first.Intent, await service.ResolveAsync(actor, first.Request, default));
            Assert.Null(await repository.OwnedAsync(actor with { LoginBinding = new('D', 64) }, id, default));
            await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetAsync(actor with { Subject = "other" }, id, default));
            var stored = (await repository.OwnedAsync(actor, id, default))!;
            Assert.True(FileMutationService.HasValidBinding(stored, protector)); // actual JSONB normalization + timestamps
            Assert.Equal(9007199254740993UL, FileMutationService.ReadPlan(stored).SourceIdentity!.Inode);
            Assert.DoesNotContain(actor.LoginBinding, (await db.OperationJobs.AsNoTracking().SingleAsync(item => item.Id == id)).RequestJson);
            Assert.DoesNotContain("roundtrip", (await db.OperationJobs.AsNoTracking().SingleAsync(item => item.Id == id)).RequestJson);
            await Assert.ThrowsAsync<InvalidOperationException>(() => Upload("roundtrip"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => Upload("source-roundtrip"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => Create("roundtrip"));
            identity.Admin = false;
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.PlanAsync(actor, Command("denied"), default)); identity.Admin = true;
            Assert.True(await worker.TickAsync(default)); Assert.Equal(1, gateway.Applied);
            Assert.Equal("Succeeded", (await service.GetAsync(actor, id, default)).State);
            Assert.Empty(await db.FilePathLocks.Where(item => item.OperationId == id).ToArrayAsync());
            Assert.Equal(3, await db.OperationJobEvents.CountAsync(item => item.JobId == id));
            Assert.Equal(2, await db.AuditEvents.CountAsync(item => item.JobId == id));
            Assert.False(await db.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT 1 FROM outbox_messages WHERE message_type='FileMutationRequested' AND payload_json->>'jobId'={id.ToString("D")} AND processed_at_utc IS NULL) AS \"Value\"").SingleAsync());
            // Two real connections race different targets sharing a source.
            // A per-table unique target index cannot enforce this boundary.
            var racer = actor with { Subject = "native-mutation-racer" };
            var competing = await Task.WhenAll(Enumerable.Range(0, 2).Select(index => Task.Run(async () =>
            {
                await using var contender = new PlatformDbContext(options);
                var contenderService = new FileMutationService(authorization, gateway, new FileMutationRepository(contender), protector, TimeProvider.System);
                var value = Command("concurrent-" + index) with { Source = new("test.mutation", "shared-concurrent-source") };
                var planned = await contenderService.PlanAsync(racer, value, default);
                try { return (Intent: await contenderService.SubmitAsync(racer, new(planned.PlanHash, true, Guid.NewGuid().ToString("D")), default), Busy: false); }
                catch (InvalidOperationException error) when (error.Message == "FILES_TARGET_BUSY") { return (Intent: (FileMutationIntentDto?)null, Busy: true); }
            })));
            var raceWinner = Assert.Single(competing, item => !item.Busy).Intent!;
            Assert.Single(competing, item => item.Busy); Assert.Equal(2, await db.FilePathLocks.CountAsync(item => item.OperationId == raceWinner.Id));
            await service.CancelAsync(racer, raceWinner.Id, default);
            var raceClaim = Assert.IsType<FileMutationOperation>(await repository.ClaimAsync(DateTimeOffset.UtcNow, default)); Assert.Equal(raceWinner.Id, raceClaim.Id);
            var raceReceipt = NativeMutationGateway.Complete(FileMutationService.ReadPlan(raceClaim)) with { State = "Aborted", Version = "", BindingCurrent = false };
            Assert.True(await repository.FinishAsync(raceClaim, "Canceled", "FILES_CANCELED", raceReceipt, default));
            // Inverse conflict: upload owns the source/target logical namespace.
            var upload = await Upload("source-upload-lock");
            await Assert.ThrowsAsync<InvalidOperationException>(() => Create("upload-lock"));
            await uploads.CancelAsync(actor, upload.Id, default);
            // Old file fixtures have unresolved uploads; claim this exact row by
            // moving their retry windows, never by deleting unrelated evidence.
            await db.FileUploadOperations.Where(item => item.CompletedAtUtc == null && item.Id != upload.Id).ExecuteUpdateAsync(setters => setters.SetProperty(item => item.NextAttemptAtUtc, DateTimeOffset.UtcNow.AddHours(1)));
            var uploadClaim = Assert.IsType<FileUploadOperation>(await uploads.ClaimAsync(DateTimeOffset.UtcNow, default)); Assert.Equal(upload.Id, uploadClaim.Id);
            Assert.True(await uploads.FinishAsync(uploadClaim, "Canceled", "FILES_CANCELED", new("Aborted", 0, new('0', 64), "", false, false, false), default));
            var canceled = await Create("cancel"); await service.CancelAsync(actor, canceled.Intent.Id, default);
            var applied = gateway.Applied; Assert.True(await worker.TickAsync(default)); Assert.Equal(applied, gateway.Applied);
            Assert.Equal("Canceled", (await service.GetAsync(actor, canceled.Intent.Id, default)).State); Assert.True(gateway.Discarded > 0);
            var lost = await Create("lost-response"); gateway.LoseResponse = true;
            Assert.True(await worker.TickAsync(default)); Assert.Equal("Running", (await service.GetAsync(actor, lost.Intent.Id, default)).State);
            applied = gateway.Applied; await Due(lost.Intent.Id); Assert.True(await worker.TickAsync(default)); Assert.Equal(applied, gateway.Applied);
            Assert.Equal("Succeeded", (await service.GetAsync(actor, lost.Intent.Id, default)).State);
            var unknown = await Create("unknown"); gateway.Unknown = true;
            Assert.True(await worker.TickAsync(default)); var unknownRow = await service.GetAsync(actor, unknown.Intent.Id, default);
            Assert.Equal("Unknown", unknownRow.State); Assert.Equal(2, await db.FilePathLocks.CountAsync(item => item.OperationId == unknownRow.Id));
            await Assert.ThrowsAsync<InvalidOperationException>(() => Upload("unknown"));
            var events = await db.OperationJobEvents.CountAsync(item => item.JobId == unknownRow.Id);
            await Due(unknownRow.Id); Assert.True(await worker.TickAsync(default));
            Assert.Equal(events, await db.OperationJobEvents.CountAsync(item => item.JobId == unknownRow.Id)); // repeated uncertainty is not a log/event storm
            unknownRow = await service.GetAsync(actor, unknownRow.Id, default);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.AcknowledgeAsync(actor, unknownRow.Id, new(unknownRow.Version - 1, true), default));
            await service.AcknowledgeAsync(actor, unknownRow.Id, new(unknownRow.Version, true), default);
            Assert.Equal("Acknowledged", (await service.GetAsync(actor, unknownRow.Id, default)).State);
            Assert.Equal("Failed", (await db.OperationJobs.AsNoTracking().SingleAsync(item => item.Id == unknownRow.Id)).State);
            Assert.Empty(await db.FilePathLocks.Where(item => item.OperationId == unknownRow.Id).ToArrayAsync()); gateway.Unknown = false;
            var validClaims = new FileMutationPlanClaims(actor.Subject, actor.LoginBinding, await gateway.PlanMutationAsync(actor, Command("unregistered"), default), DateTimeOffset.UtcNow.AddMinutes(5));
            var absentKey = Guid.NewGuid().ToString("D");
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ResolveAsync(actor, new(protector.Sign(validClaims), true, absentKey), default));
            var expiredToken = protector.Sign(validClaims with { ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1) });
            Assert.Null(await service.ResolveAsync(actor, new(expiredToken, true, absentKey), default));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.SubmitAsync(actor, new(expiredToken, true, absentKey), default));
            var missing = await Create("started-no-receipt");
            var claim = Assert.IsType<FileMutationOperation>(await repository.ClaimAsync(DateTimeOffset.UtcNow, default)); Assert.Equal(missing.Intent.Id, claim.Id);
            Assert.Equal(FileUploadStartResult.Started, await repository.StartedAsync(claim, default));
            await db.FileMutationOperations.Where(item => item.Id == claim.Id).ExecuteUpdateAsync(setters => setters.SetProperty(item => item.LeaseExpiresAtUtc, DateTimeOffset.UtcNow.AddSeconds(-1)));
            Assert.False(await repository.FinishAsync(claim, "Succeeded", "FILES_MUTATION_COMPLETED", NativeMutationGateway.Complete(FileMutationService.ReadPlan(claim)), default));
            applied = gateway.Applied; await Due(claim.Id); Assert.True(await worker.TickAsync(default)); Assert.Equal(applied, gateway.Applied);
            Assert.Equal("Failed", (await service.GetAsync(actor, claim.Id, default)).State);
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE audit_events ADD CONSTRAINT native_mutation_audit_failure CHECK (action NOT LIKE 'files.mutation.%') NOT VALID");
            try { await Assert.ThrowsAsync<DbUpdateException>(() => Create("audit-failure")); }
            finally { await db.Database.ExecuteSqlRawAsync("ALTER TABLE audit_events DROP CONSTRAINT native_mutation_audit_failure"); }
            Assert.False(await db.FilePathLocks.AnyAsync(item => item.PathHash == FileManagementService.Hash("test.mutation:audit-failure")));
            Assert.DoesNotContain(await db.FileMutationOperations.AsNoTracking().Select(item => item.PlanJson).ToArrayAsync(), value => value.Contains("audit-failure", StringComparison.Ordinal));
            await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE file_mutation_operations SET state='imaginary' WHERE id={id}"));
            await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE file_mutation_operations SET completed_at_utc=NULL WHERE id={id}"));
            var late = await Create("late-job-lock");
            var lateClaim = Assert.IsType<FileMutationOperation>(await repository.ClaimAsync(DateTimeOffset.UtcNow, default)); Assert.Equal(late.Intent.Id, lateClaim.Id);
            await db.FileMutationOperations.Where(item => item.Id == lateClaim.Id).ExecuteUpdateAsync(setters => setters.SetProperty(item => item.LeaseExpiresAtUtc, DateTimeOffset.UtcNow.AddMilliseconds(500)));
            await using (var locker = new PlatformDbContext(options))
            {
                await using var transaction = await locker.Database.BeginTransactionAsync();
                await locker.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM operation_jobs WHERE id={lateClaim.Id} FOR UPDATE");
                var blocked = Task.Run(async () => { await using var contender = new PlatformDbContext(options); return await new FileMutationRepository(contender).StartedAsync(lateClaim, default); });
                var observedWait = false;
                for (var attempt = 0; attempt < 40; attempt++)
                {
                    observedWait = await db.Database.SqlQueryRaw<bool>("SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE usename=current_user AND pid<>pg_backend_pid() AND wait_event_type='Lock' AND query LIKE '%operation_jobs%' AND query LIKE '%FOR UPDATE%') AS \"Value\"").SingleAsync();
                    if (observedWait) break;
                    await Task.Delay(25);
                }
                Assert.True(observedWait, "The contender must actually be blocked on the Job row, not merely return after a scheduling delay.");
                await Task.Delay(1200); await transaction.CommitAsync(); Assert.Equal(FileUploadStartResult.LeaseLost, await blocked);
            }
            Assert.Null((await repository.OwnedAsync(actor, lateClaim.Id, default))!.RootStartedAtUtc);
            await service.CancelAsync(actor, lateClaim.Id, default); await Due(lateClaim.Id); Assert.True(await worker.TickAsync(default));
            Assert.Equal("Canceled", (await service.GetAsync(actor, lateClaim.Id, default)).State);
            var seal = await Create("tampered-seal");
            await db.FileMutationOperations.Where(item => item.Id == seal.Intent.Id).ExecuteUpdateAsync(setters => setters.SetProperty(item => item.ExpiresAtUtc, DateTimeOffset.UtcNow.AddHours(1)));
            applied = gateway.Applied; Assert.True(await worker.TickAsync(default)); Assert.Equal(applied, gateway.Applied);
            Assert.Equal("Unknown", (await repository.OwnedAsync(actor, seal.Intent.Id, default))!.State);
            Assert.Equal(2, await db.FilePathLocks.CountAsync(item => item.OperationId == seal.Intent.Id));
            Console.WriteLine("INBOX_PHASE=file-mutations-real-jsonb-shared-locks-seal-jobs-audit-cancel-lost-response-unknown");
        }
        finally { File.Delete(key); }
    }
    private sealed class NativeMutationIdentity : IIdentityDirectory
    {
        public bool Admin { get; set; } = true;
        public Task<CurrentUserDto> ResolveCurrentAsync(string subject, string? name, IReadOnlyCollection<string> claimGroups, CancellationToken cancellationToken) =>
            Task.FromResult(new CurrentUserDto(subject, name, claimGroups, Admin, DateTimeOffset.UtcNow.AddHours(2)));
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListUsersAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListGroupsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListSsoApplicationsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    // Explicit transport stub: proves SQL/Application boundaries, NOT root or
    // network execution. Installed root/UDS has its separate native probe.
    private sealed class NativeMutationGateway : IFileMutationGateway
    {
        private readonly Dictionary<Guid, FileMutationReceipt> _receipts = [];
        public int Applied { get; private set; }
        public int Discarded { get; private set; }
        public bool LoseResponse { get; set; }
        public bool Unknown { get; set; }
        public Task<FileMutationPlan> PlanMutationAsync(TerminalActor actor, FileMutationCommand command, CancellationToken cancellationToken) => Task.FromResult(new FileMutationPlan(command,
            new(ulong.MaxValue, 9007199254740993UL, 0x81A4, uint.MaxValue, 1000, 1, 3, -1), new(1, 2), new(1, 3), 3, new('C', 64), new('D', 64)));
        public static FileMutationReceipt Complete(FileMutationPlan plan) => new(plan.Command.Kind, "Completed", plan.Fingerprint, new('E', 64), plan.SizeBytes, plan.Sha256, false, true);
        public Task<FileMutationReceipt?> InspectMutationAsync(FileMutationOperation operation, CancellationToken cancellationToken) => Task.FromResult(_receipts.GetValueOrDefault(operation.Id));
        public Task<FileMutationReceipt> ApplyMutationAsync(FileMutationOperation operation, CancellationToken cancellationToken)
        {
            Applied++; var result = Complete(FileMutationService.ReadPlan(operation));
            if (Unknown) result = result with { State = "Unknown", Version = "", BindingCurrent = false };
            _receipts[operation.Id] = result;
            if (LoseResponse) { LoseResponse = false; throw new IOException("synthetic response loss after receipt"); }
            return Task.FromResult(result);
        }
        public Task<FileMutationReceipt> DiscardMutationAsync(FileMutationOperation operation, CancellationToken cancellationToken)
        {
            Discarded++;
            if (!_receipts.TryGetValue(operation.Id, out var result))
            { result = Complete(FileMutationService.ReadPlan(operation)) with { State = "Aborted", Version = "", BindingCurrent = false }; _receipts[operation.Id] = result; }
            return Task.FromResult(result);
        }
    }
}
