using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Infrastructure.Persistence;

namespace WhaleDeck.IntegrationTests;

public sealed partial class NativeNotificationDispatchTests
{
    private static async Task ContainerFileAuditBoundaries(DbContextOptions<PlatformDbContext> options)
    {
        await using var db = new PlatformDbContext(options);
        var audit = new ContainerFileReadAudit(db);
        var trace = "native-container-read-" + Guid.NewGuid().ToString("N");
        var actor = new TerminalActor("native-container-file-admin", null, [], new('A', 64), trace, null);
        var location = new ContainerFileLocationDto(new('a', 64), "777:2026-10-22T01:02:03.123456789Z", "资料/private-name.bin");
        await audit.RecordAsync(actor, location, "list", "Validated", new('B', 64), null, null, default);
        await audit.RecordAsync(actor, location, "download", "Started", new('B', 64), null, null, default);
        await audit.RecordAsync(actor, location, "download", "Validated", new('B', 64), 3, new('C', 64), default);
        db.ChangeTracker.Clear();
        var rows = await db.AuditEvents.AsNoTracking().Where(item => item.TraceId == trace).ToArrayAsync();
        Assert.Equal(3, rows.Length);
        foreach (var row in rows)
        {
            Assert.Equal(actor.Subject, row.ActorSubject); Assert.Equal("container", row.TargetType); Assert.Equal(location.ContainerId, row.TargetId);
            Assert.DoesNotContain("private-name", row.DetailJson); Assert.DoesNotContain(location.InstanceId, row.DetailJson); Assert.DoesNotContain(actor.LoginBinding, row.DetailJson);
            using var detail = JsonDocument.Parse(row.DetailJson!);
            Assert.Equal(5, detail.RootElement.EnumerateObject().Count());
            Assert.Equal(FileManagementService.Hash(location.Path), detail.RootElement.GetProperty("pathHash").GetString());
            Assert.Equal(FileManagementService.Hash(location.InstanceId), detail.RootElement.GetProperty("instanceHash").GetString());
        }
        await Assert.ThrowsAsync<ArgumentException>(() => audit.RecordAsync(actor, location with { Path = "../outside" }, "list", "Validated", null, null, null, default));
        await Assert.ThrowsAsync<ArgumentException>(() => audit.RecordAsync(actor, location, "upload", "Validated", null, null, null, default));
        Assert.Equal(3, await db.AuditEvents.CountAsync(item => item.TraceId == trace));
        Console.WriteLine("INBOX_PHASE=container-file-private-audit");
    }
}
