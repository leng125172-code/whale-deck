using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Infrastructure.Agent;
using WhaleDeck.Infrastructure.Persistence;

namespace WhaleDeck.IntegrationTests;

public sealed partial class NativeNotificationDispatchTests
{
    private static async Task FileRecoveryInventoryReadOnly(DbContextOptions<PlatformDbContext> options,
        FilePlanProtector protector, string kind, Guid id)
    {
        await using var db = new PlatformDbContext(options);
        var inventory = new FileRecoveryInventory(db, protector, protector, protector, protector, protector, protector);
        var authorization = new FileManagementService(null!, null!, null!, new NativeMoveTreeIdentity(), TimeProvider.System);
        var service = new FileRecoveryInventoryService(authorization, inventory);
        var currentActor = new TerminalActor("new-current-recovery-admin", null, [], new('F', 64), "native-recovery-inventory", null);
        var beforeLocks = await db.FilePathLocks.CountAsync();
        var first = await service.GetAsync(currentActor, kind, id, default);
        Assert.Equal("Verified", first.Integrity); Assert.Equal(id, first.IntentId);
        Assert.True(first.Source is not null || first.Target is not null);
        Assert.DoesNotContain(JsonSerializer.SerializeToElement(first).EnumerateObject(), property =>
            property.Name is "LoginBinding" or "OwnerHash" or "PlanJson" or "PlanHash" or "RequestHash" or "CommandJson");
        List<(string Kind, Guid Id)> seen = []; string? after = null; FileRecoveryCursor? previous = null;
        for (var page = 0; page < 128; page++)
        {
            var result = await service.ListAsync(currentActor, after, 1, default);
            Assert.InRange(result.Entries.Count, 0, 1);
            foreach (var entry in result.Entries)
            {
                Assert.DoesNotContain((entry.Kind, entry.IntentId), seen); seen.Add((entry.Kind, entry.IntentId));
                var cursor = new FileRecoveryCursor(entry.CreatedAtUtc, entry.Kind, entry.IntentId);
                if (previous is not null)
                {
                    var order = cursor.CreatedAtUtc.CompareTo(previous.CreatedAtUtc);
                    if (order == 0) order = FileRecoveryValidation.Rank(cursor.Kind).CompareTo(FileRecoveryValidation.Rank(previous.Kind));
                    if (order == 0) order = cursor.IntentId.CompareTo(previous.IntentId);
                    Assert.True(order > 0, "Stable seek order must match PostgreSQL UUID and equal-timestamp ordering.");
                }
                previous = cursor;
            }
            after = result.NextCursor;
            if (after is null) break;
            Assert.NotNull(FileRecoveryValidation.DecodeCursor(after)); Assert.True(page < 127, "Bounded inventory did not finish.");
        }
        Assert.Contains((kind, id), seen);
        var key = Path.Combine(Path.GetTempPath(), "wd-wrong-recovery-key-" + Guid.NewGuid().ToString("N"));
        await File.WriteAllBytesAsync(key, RandomNumberGenerator.GetBytes(32));
        try
        {
            var wrong = new FilePlanProtector(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:CapabilityKeyFile"] = key }).Build());
            var invalid = await new FileRecoveryInventory(db, wrong, wrong, wrong, wrong, wrong, wrong).GetAsync(kind, id, default);
            Assert.NotNull(invalid); Assert.Equal("InvalidBinding", invalid.Integrity);
            Assert.Null(invalid.Source); Assert.Null(invalid.Target); Assert.Null(invalid.HasRetainedObjects);
        }
        finally { File.Delete(key); }
        Assert.Equal(first, await service.GetAsync(currentActor, kind, id, default));
        Assert.Equal(beforeLocks, await db.FilePathLocks.CountAsync());
        Assert.Empty(db.ChangeTracker.Entries());
        await FileRecoveryWorkflowBoundaries(options, protector, kind, id);
        var clone = await CloneRecoverySource(options, protector, kind, id);
        await FileRecoveryWorkflowBoundaries(options, protector, kind, clone, true);
    }
}
