using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Npgsql;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Agent;
using WhaleDeck.Infrastructure.Persistence;

namespace WhaleDeck.IntegrationTests;

public sealed partial class NativeNotificationDispatchTests
{
    // This is part of the fixture's forward-only schema upgrade, before other
    // scenarios start. It never downgrades a running application database.
    private static async Task ContainerRestorationAttributeUpgrade(PlatformDbContext db)
    {
        Assert.StartsWith("wdv_dispatch_", db.Database.GetDbConnection().Database, StringComparison.Ordinal);
        await db.GetService<IMigrator>().MigrateAsync("20261027074344_ContainerFileRestorationSources");
        var key = Path.GetTempFileName(); await File.WriteAllBytesAsync(key, RandomNumberGenerator.GetBytes(32));
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(key, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        var id = Guid.NewGuid();
        try
        {
            var protector = new FilePlanProtector(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:CapabilityKeyFile"] = key }).Build());
            var actor = new TerminalActor("native-legacy-container", null, [], new('A', 64), "native-container-upgrade", null);
            var now = ContainerFileUploadIntentValidation.CanonicalTime(DateTimeOffset.UtcNow);
            var source = new ContainerHistoryEntryDto(Guid.NewGuid().ToString("N"), new('c', 64), "888:2026-10-26T01:02:03.123456789Z",
                "legacy/source.bin", new('B', 64), 0, new('D', 64), now.AddDays(-1).ToUnixTimeMilliseconds(), false, "Present", new('E', 64));
            var binding = new ContainerFileUploadBinding(id, Guid.NewGuid(), actor.Subject, actor.LoginBinding, actor.TraceId,
                new(new(source.ContainerId, "999:2026-10-27T01:02:03.123456789Z", "legacy/copy.bin"), new('B', 64), new('C', 64), "ABSENT", 0),
                0, source.Sha256, new('F', 64), now, now.AddMinutes(15), ContainerFileRestoreValidation.Snapshot(source));
            var row = new ContainerFileUploadOperation { Id = id, IdempotencyKey = binding.IdempotencyKey, ActorSubject = actor.Subject,
                LoginBinding = actor.LoginBinding, TraceId = actor.TraceId, OwnerHash = ContainerFileUploadIntentValidation.Owner(actor.Subject, actor.LoginBinding),
                ContainerId = source.ContainerId, InstanceId = binding.Target.Location.InstanceId, RelativePath = binding.Target.Location.Path,
                TargetProof = binding.Target.TargetProof, ExpectedParentVersion = binding.Target.ExpectedParentVersion, ExpectedVersion = "ABSENT",
                PlanHash = binding.PlanHash, RequestHash = protector.FingerprintContainerUpload(binding), Sha256 = source.Sha256,
                HistorySourceJson = binding.HistorySourceJson, CreatedAtUtc = now, ExpiresAtUtc = binding.ExpiresAtUtc, NextAttemptAtUtc = now };
            var frozen = JsonSerializer.Serialize(row);
            Assert.DoesNotContain("RestoreAttributes", frozen, StringComparison.Ordinal);
            db.OperationJobs.Add(new() { Id = id, ActorSubject = actor.Subject, JobType = "containers.files.restore", IdempotencyKey = id.ToString("D") });
            await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO container_file_upload_operations (id,idempotency_key,actor_subject,login_binding,trace_id,owner_hash,container_id,instance_id,relative_path,target_proof,expected_parent_version,expected_version,previous_size_bytes,plan_hash,request_hash,sha256,history_source_json,size_bytes,state,publish_barrier,created_at_utc,expires_at_utc,next_attempt_at_utc,previous_retained,version) VALUES ({row.Id},{row.IdempotencyKey},{row.ActorSubject},{row.LoginBinding},{row.TraceId},{row.OwnerHash},{row.ContainerId},{row.InstanceId},{row.RelativePath},{row.TargetProof},{row.ExpectedParentVersion},{row.ExpectedVersion},0,{row.PlanHash},{row.RequestHash},{row.Sha256},{row.HistorySourceJson},0,'AwaitingUpload',false,{row.CreatedAtUtc},{row.ExpiresAtUtc},{row.NextAttemptAtUtc},false,0)");
            await db.Database.MigrateAsync();
            var loaded = await db.ContainerFileUploadOperations.AsNoTracking().SingleAsync(item => item.Id == id);
            Assert.False(loaded.RestoreAttributes); Assert.True(ContainerFileUploadService.HasValidBinding(loaded, protector));
            Assert.Equal(frozen, JsonSerializer.Serialize(loaded));
            var invalid = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE container_file_upload_operations SET restore_attributes=true, history_source_json=NULL WHERE id={id}"));
            Assert.Equal(PostgresErrorCodes.CheckViolation, invalid.SqlState);
            Console.WriteLine("INBOX_PHASE=container-attribute-forward-upgrade-preserves-legacy-mac-snapshot-and-content-policy");
        }
        finally
        {
            db.ChangeTracker.Clear(); await db.ContainerFileUploadOperations.Where(item => item.Id == id).ExecuteDeleteAsync();
            await db.OperationJobs.Where(item => item.Id == id).ExecuteDeleteAsync(); File.Delete(key);
        }
    }
}
