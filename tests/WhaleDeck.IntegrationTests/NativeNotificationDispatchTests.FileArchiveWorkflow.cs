using System.Security.Cryptography;
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
    private static async Task FileArchiveWorkflowBoundaries(DbContextOptions<PlatformDbContext> options)
    {
        await using var db = new PlatformDbContext(options);
        var key = Path.Combine(Path.GetTempPath(), "wd-native-archive-" + Guid.NewGuid().ToString("N"));
        await File.WriteAllBytesAsync(key, RandomNumberGenerator.GetBytes(32));
        try
        {
            IFileArchiveProtector protector = new FilePlanProtector(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:CapabilityKeyFile"] = key }).Build());
            var repository = new FileArchiveRepository(db, protector); var identity = new NativeArchiveIdentity(); var gateway = new NativeArchiveGateway();
            var authorization = new FileManagementService(null!, null!, null!, identity, TimeProvider.System);
            var service = new FileArchiveService(authorization, gateway, repository, protector, TimeProvider.System);
            var worker = new FileArchiveDispatcher(repository, gateway, identity, protector, TimeProvider.System);
            var actor = new TerminalActor("native-archive-flow-admin", null, [], new('A', 64), "native-archive-flow", null);
            FileArchiveCommand Command(string path, string kind = "trash", Guid? version = null) => new(kind, new("test.archiveflow", path), version, kind == "trash" ? new('A', 64) : "ABSENT");
            async Task<(FileArchiveIntentDto Intent, SubmitFileArchiveCommand Submit)> Create(FileArchiveCommand command)
            {
                var plan = await service.PlanAsync(actor, command, default);
                var submit = new SubmitFileArchiveCommand(plan.PlanHash, true, Guid.NewGuid().ToString("D"));
                return (await service.SubmitAsync(actor, submit, default), submit);
            }
            identity.Admin = false;
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.PlanAsync(actor, Command("denied"), default)); identity.Admin = true;
            var one = await Create(Command("one"));
            Assert.Equal(one.Intent.Id, (await service.SubmitAsync(actor, one.Submit, default)).Id);
            Assert.Null(await repository.OwnedAsync(actor with { LoginBinding = new('B', 64) }, one.Intent.Id, default));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SubmitAsync(actor with { LoginBinding = new('B', 64) }, one.Submit, default));
            Assert.Single(await db.FilePathLocks.Where(item => item.OperationId == one.Intent.Id).ToArrayAsync());
            Assert.Equal("files.archive", (await db.OperationJobs.SingleAsync(item => item.Id == one.Intent.Id)).JobType);
            var mutations = new FileMutationRepository(db);
            var competing = new FileMutationPlan(new("copy", new("test.archiveflow", "one"), new("test.archiveflow", "elsewhere"), new('A', 64), ""),
                new(1, 2, 0x81A4, 1000, 1000, 1, 7, -1), new(1, 3), new(1, 3), 7, new('B', 64), new('D', 64));
            var busy = await Assert.ThrowsAsync<InvalidOperationException>(() => mutations.EnqueueAsync(actor, Guid.NewGuid(), Guid.NewGuid().ToString("D"), new('E', 64),
                new(actor.Subject, actor.LoginBinding, competing, DateTimeOffset.UtcNow.AddMinutes(5)), DateTimeOffset.UtcNow.AddMinutes(15), default));
            Assert.Equal("FILES_TARGET_BUSY", busy.Message);
            Assert.True(await worker.TickAsync(default));
            var version = await db.FileVersions.AsNoTracking().SingleAsync(item => item.Id == one.Intent.Id);
            Assert.Equal(one.Intent.Id, version.SourceJobId); Assert.Equal(actor.Subject, version.CreatedBySubject); Assert.Equal("Trashed", version.CaptureState);
            Assert.Equal("Succeeded", (await service.GetAsync(actor, one.Intent.Id, default)).State);
            Assert.Empty(await db.FilePathLocks.Where(item => item.OperationId == one.Intent.Id).ToArrayAsync());
            Assert.True(await db.OutboxMessages.AnyAsync(item => item.MessageType == "FileArchiveRequested" && item.ProcessedAtUtc != null));
            Assert.Equal(1, await db.AuditEvents.CountAsync(item => item.JobId == one.Intent.Id && item.Action == "files.archive.result"));
            var legacyId = Guid.Parse("00000000-0000-0000-0000-000000000001"); gateway.AddLegacy(legacyId, "legacy");
            var legacyPlan = await service.PlanAsync(actor, Command("legacy", "restore", legacyId), default);
            Assert.Equal("", gateway.LastAfter);
            var legacy = await db.FileVersions.AsNoTracking().SingleAsync(item => item.Id == legacyId);
            Assert.Null(legacy.SourceJobId); Assert.Null(legacy.CreatedBySubject); Assert.Equal("restore", legacyPlan.Command.Kind);
            await Assert.ThrowsAsync<InvalidOperationException>(() => repository.ObserveAsync([gateway.Versions[legacyId] with { Sha256 = new('F', 64) }], default));
            var canceled = await Create(Command("one", "restore", one.Intent.Id));
            await service.CancelAsync(actor, canceled.Intent.Id, default); var applied = gateway.Applied;
            Assert.True(await worker.TickAsync(default)); Assert.Equal(applied, gateway.Applied);
            Assert.Equal("Canceled", (await service.GetAsync(actor, canceled.Intent.Id, default)).State);
            var downgraded = await Create(Command("downgraded")); identity.Admin = false;
            Assert.True(await worker.TickAsync(default)); Assert.Equal(applied, gateway.Applied); identity.Admin = true;
            Assert.Equal("Canceled", (await service.GetAsync(actor, downgraded.Intent.Id, default)).State);
            var lost = await Create(Command("lost")); gateway.LoseResponse = true;
            Assert.True(await worker.TickAsync(default)); var beforeReplay = gateway.Applied;
            await db.FileArchiveOperations.Where(item => item.Id == lost.Intent.Id).ExecuteUpdateAsync(setters => setters.SetProperty(item => item.NextAttemptAtUtc, DateTimeOffset.UtcNow));
            Assert.True(await worker.TickAsync(default)); Assert.Equal(beforeReplay, gateway.Applied);
            Assert.Equal("Succeeded", (await service.GetAsync(actor, lost.Intent.Id, default)).State);
            var purge = await Create(Command("one", "purge", one.Intent.Id)); var stale = gateway.Versions[one.Intent.Id];
            Assert.True(await worker.TickAsync(default)); await repository.ObserveAsync([stale], default);
            var purged = await db.FileVersions.AsNoTracking().SingleAsync(item => item.Id == one.Intent.Id);
            Assert.Equal("Purged", purged.CaptureState); Assert.Equal("Purged", purged.Availability); Assert.Equal(actor.Subject, purged.CreatedBySubject);
            var unknown = await Create(Command("unknown")); gateway.Unknown = true;
            Assert.True(await worker.TickAsync(default)); var uncertain = await service.GetAsync(actor, unknown.Intent.Id, default); Assert.Equal("Unknown", uncertain.State);
            Assert.Single(await db.FilePathLocks.Where(item => item.OperationId == unknown.Intent.Id).ToArrayAsync());
            await service.AcknowledgeAsync(actor, unknown.Intent.Id, new(uncertain.Version, true), default);
            Assert.Equal("Acknowledged", (await service.GetAsync(actor, unknown.Intent.Id, default)).State);
            Assert.Empty(await db.FilePathLocks.Where(item => item.OperationId == unknown.Intent.Id).ToArrayAsync()); gateway.Unknown = false;
            var tampered = await Create(Command("tampered"));
            await db.FileArchiveOperations.Where(item => item.Id == tampered.Intent.Id).ExecuteUpdateAsync(setters => setters.SetProperty(item => item.ExpiresAtUtc, DateTimeOffset.UtcNow.AddHours(1)));
            applied = gateway.Applied; Assert.True(await worker.TickAsync(default)); Assert.Equal(applied, gateway.Applied);
            Assert.Equal("Unknown", (await repository.OwnedAsync(actor, tampered.Intent.Id, default))!.State);
            Assert.Single(await db.FilePathLocks.Where(item => item.OperationId == tampered.Intent.Id).ToArrayAsync());
            Console.WriteLine("INBOX_PHASE=file-archive-real-jsonb-catalog-shared-locks-seal-jobs-audit-cancel-recovery");
        }
        finally { File.Delete(key); }
    }
    private sealed class NativeArchiveIdentity : IIdentityDirectory
    {
        public bool Admin { get; set; } = true;
        public Task<CurrentUserDto> ResolveCurrentAsync(string subject, string? name, IReadOnlyCollection<string> groups, CancellationToken cancellationToken) => Task.FromResult(new CurrentUserDto(subject, null, groups, Admin, DateTimeOffset.UtcNow.AddHours(2)));
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListUsersAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListGroupsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListSsoApplicationsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    // Explicit transport stub: exercises real SQL/Application workflow, NOT
    // physical disk, installed Agent, actual BFF identity or browser evidence.
    private sealed class NativeArchiveGateway : IFileArchiveGateway
    {
        public Dictionary<Guid, FileVersionDto> Versions { get; } = [];
        private readonly Dictionary<Guid, FileArchiveReceipt> _receipts = [];
        public int Applied { get; private set; } public bool LoseResponse { get; set; } public bool Unknown { get; set; } public string LastAfter { get; private set; } = "";
        private static readonly DateTimeOffset CaptureTime = DateTimeOffset.FromUnixTimeMilliseconds(1760000000000);
        public void AddLegacy(Guid id, string path) => Versions[id] = new(id, "test.archiveflow", path, new('A', 64), 7, new('B', 64), CaptureTime, "Overwrite", "Available", "Present", false);
        public Task<FileHistoryPage> ListHistoryAsync(FileHistoryCommand command, CancellationToken cancellationToken)
        {
            LastAfter = command.After;
            var rows = Versions.Values.Where(item => item.RootId == command.RootId && (command.Path == "" || item.Path == command.Path) && string.CompareOrdinal(item.Id.ToString("N"), command.After) > 0)
                .OrderBy(item => item.Id.ToString("N"), StringComparer.Ordinal).ToArray();
            var chosen = rows.Take(command.Take).ToArray(); var complete = rows.Length <= chosen.Length;
            return Task.FromResult(new FileHistoryPage(chosen, complete ? "" : chosen[^1].Id.ToString("N"), new('F', 64), chosen.Length, 0, complete));
        }
        public Task<FileArchivePlan> PlanArchiveAsync(TerminalActor actor, FileArchiveCommand command, CancellationToken cancellationToken)
        {
            if (command.VersionId is { } id && !Versions.ContainsKey(id)) throw new KeyNotFoundException("FILES_ARCHIVE_NOT_FOUND");
            return Task.FromResult(new FileArchivePlan(command, command.Kind == "purge" ? null : new(1, 2), command.ExpectedVersion == "ABSENT" ? null : new(1, 3, 0x81A4, 1000, 1000, 1, 7, -1),
                command.ExpectedVersion == "ABSENT" ? "" : new('B', 64), command.Kind == "trash" ? null : new(new('C', 64), new(2, 4, 0x8180, 0, 0, 1, 7, -1)),
                command.Kind == "trash" ? null : new(1, 3, 0x81A4, 1000, 1000, 1, 7, -1), 7, new('B', 64), new('D', 64)));
        }
        public Task<FileArchiveReceipt?> InspectArchiveAsync(FileArchiveOperation operation, CancellationToken cancellationToken) => Task.FromResult(_receipts.GetValueOrDefault(operation.Id));
        public Task<FileArchiveReceipt> DiscardArchiveAsync(FileArchiveOperation operation, CancellationToken cancellationToken)
        { var receipt = _receipts.GetValueOrDefault(operation.Id) ?? Result(operation, "Aborted"); _receipts[operation.Id] = receipt; return Task.FromResult(receipt); }
        public Task<FileArchiveReceipt> ApplyArchiveAsync(FileArchiveOperation operation, CancellationToken cancellationToken)
        {
            Applied++; var result = Result(operation, Unknown ? "Unknown" : "Completed"); _receipts[operation.Id] = result;
            if (result.State == "Completed")
            {
                if (result.HddBackup is not null) Versions[operation.Id] = new(operation.Id, operation.RootId, operation.RelativePath, result.HddBackup.SourceVersion, result.HddBackup.SizeBytes,
                    result.HddBackup.Sha256, CaptureTime, operation.Kind == "trash" ? "Trash" : "Overwrite", operation.Kind == "trash" ? "Trashed" : "Available", "Present", false);
                if (operation.Kind == "purge") Versions.Remove(operation.ArchiveVersionId!.Value);
            }
            if (LoseResponse) { LoseResponse = false; throw new IOException("owned fake lost response"); }
            return Task.FromResult(result);
        }
        private static FileArchiveReceipt Result(FileArchiveOperation row, string state)
        {
            var plan = FileArchiveValidation.ReadPlan(row); var retained = row.Kind == "trash" || row.Kind == "restore" && plan.SourceIdentity is not null;
            return new(row.Kind, state, plan.Fingerprint, state == "Completed" ? new('E', 64) : "", 7, plan.Sha256, state == "Completed" && retained, state == "Completed", row.ArchiveVersionId ?? row.Id,
                state == "Completed" && retained ? new(row.Id.ToString("N"), "database-platform.backup.hdd", row.ExpectedVersion, 7, plan.SourceSha256, CaptureTime.ToUnixTimeMilliseconds(), false) : null);
        }
    }
}
