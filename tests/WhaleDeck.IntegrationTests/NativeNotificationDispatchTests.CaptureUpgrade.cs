using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Agent;
using WhaleDeck.Infrastructure.Persistence;

namespace WhaleDeck.IntegrationTests;

public sealed partial class NativeNotificationDispatchTests
{
    private static async Task FileTreeCaptureUpgrade(PlatformDbContext db)
    {
        await db.GetService<IMigrator>().MigrateAsync("20261020020401_FileTreeRestoringWorkflow");
        Assert.Equal(0, await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM information_schema.columns WHERE table_schema='public' AND table_name='file_tree_captures' AND column_name IN ('kind','content_sha256')").SingleAsync());
        var keyPath = Path.Combine(Path.GetTempPath(), "wd-native-capture-upgrade-" + Guid.NewGuid().ToString("N"));
        await File.WriteAllBytesAsync(keyPath, RandomNumberGenerator.GetBytes(32));
        try
        {
            var protector = new FilePlanProtector(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:CapabilityKeyFile"] = keyPath }).Build());
            var actor = new TerminalActor("native-capture-upgrade", null, [], new('A', 64), "native-upgrade", null);
            var key = Guid.NewGuid().ToString("D"); var id = FileTreeRecyclingValidation.IntentId(actor, key); var execution = FileTreeRecyclingValidation.ExecutionId(id);
            var now = FileTreeRecyclingValidation.Timestamp(DateTimeOffset.UtcNow);
            var command = new FileTreeRecyclingCommand(new("test.capture-upgrade", "历史目录"), new('B', 64));
            var plan = new FileTreeRecyclingPlan(command, new(1, 2), new('C', 64), new('D', 64), 3, 1073741831, new('E', 64));
            var owner = FileManagementService.Hash("file-owner-v1:" + actor.Subject + ":" + actor.LoginBinding); var sourceHash = new FilePathScope(command.Source).Hash;
            var capture = new FileTreeCaptureBinding(execution, id, owner, sourceHash, command, plan.ManifestSha256, plan.EntryCount, plan.TotalBytes, 1073756160, new('F', 64), now);
            var proof = protector.FingerprintCapture(capture); var sourceJson = JsonSerializer.Serialize(command);
            var row = new FileTreeRecyclingOperation { Id = id, ActorSubject = actor.Subject, LoginBinding = actor.LoginBinding, IdempotencyKey = key, OwnerHash = owner,
                RequestHash = protector.FingerprintRequest(new FileTreeRecyclingRequestBinding(id, actor.Subject, actor.LoginBinding, command, now.AddMinutes(15))), SourceHash = sourceHash,
                CommandJson = sourceJson, CreatedAtUtc = now, PlanningExpiresAtUtc = now.AddMinutes(15), PlanJson = JsonSerializer.Serialize(plan), ReadyExpiresAtUtc = now.AddMinutes(5),
                ExecutionJobId = execution, ExecutionExpiresAtUtc = now.AddMinutes(15), State = "Succeeded", RootStartedAtUtc = now, NextAttemptAtUtc = now, CompletedAtUtc = now,
                CaptureId = execution, CaptureHash = proof, CaptureCreatedAtUtc = now, ArchiveBytes = capture.ArchiveBytes, ArchiveSha256 = capture.ArchiveSha256,
                CaptureVerified = true, CaptureMayBeRetained = true, SourceMayHaveChanged = true, SourceRemoved = true, OriginalsRetained = true };
            row.PlanHash = protector.FingerprintPlan(FileTreeRecyclingValidation.PlanBinding(row));
            Assert.True(FileTreeRecyclingValidation.HasValidBinding(row, protector));
            db.OperationJobs.Add(new() { Id = id, JobType = "files.tree-trash-plan", ActorSubject = actor.Subject, IdempotencyKey = key, State = "Succeeded", CompletedAtUtc = now });
            db.OperationJobs.Add(new() { Id = execution, JobType = "files.tree-trash", ActorSubject = actor.Subject, IdempotencyKey = execution.ToString("D"), State = "Succeeded", CompletedAtUtc = now });
            db.FileTreeRecyclingOperations.Add(row); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            // Deliberately omit both new columns: this is an actual old-schema INSERT.
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO file_tree_captures (id,intent_id,owner_hash,source_hash,source_json,manifest_sha256,entry_count,total_bytes,archive_bytes,archive_sha256,proof_hash,created_at_utc) VALUES ({execution},{id},{owner},{sourceHash},{sourceJson}::jsonb,{capture.ManifestSha256},{capture.EntryCount},{capture.TotalBytes},{capture.ArchiveBytes},{capture.ArchiveSha256},{proof},{now})");
            await db.Database.MigrateAsync();
            var upgraded = await db.FileTreeCaptures.AsNoTracking().SingleAsync(item => item.Id == execution);
            Assert.Equal("Trash", upgraded.Kind); Assert.Null(upgraded.ContentSha256); Assert.Equal(proof, upgraded.ProofHash);
            Assert.Equal(capture, FileTreeRecyclingValidation.CaptureBinding(upgraded));
            Assert.Equal("Trash", FileTreeCaptureValidation.Verify(upgraded, protector, protector).Kind);
            var persisted = await db.FileTreeRecyclingOperations.AsNoTracking().SingleAsync(item => item.Id == id);
            Assert.True(FileTreeRecyclingValidation.HasValidBinding(persisted, protector));
            // Complete the fixture's actual notification side effects before
            // later fanout tests establish their own exact event inventory.
            // Do not delete facts or weaken those tests' expected counts.
            Assert.Equal(2, await new NotificationDispatchStore(db).RouteBatchAsync(now, default));
            Assert.Equal(2, await db.NotificationDispatchReceipts.CountAsync(receipt => receipt.Outcome == "Personal" &&
                db.NotificationEvents.Any(item => item.Id == receipt.NotificationId && (item.SourceId == id || item.SourceId == execution))));
            Console.WriteLine("INBOX_PHASE=tree-capture-old-schema-upgrade-preserves-original-signature");
        }
        finally { File.Delete(keyPath); }
    }
}
