using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using WhaleDeck.Infrastructure.Persistence;
using WhaleDeck.IntegrationTests.Security;

namespace WhaleDeck.IntegrationTests;

public sealed partial class NativeNotificationDispatchTests
{
    private static async Task DevelopmentWorkspaceRegistryBoundaries(DbContextOptions<PlatformDbContext> options)
    {
        await using var db = new PlatformDbContext(options);
        // An earlier downgrade guard intentionally stops after empty newer tables are removed.
        await db.Database.MigrateAsync();
        await ContainerUploadHttpTests.RegisterDevelopmentWorkspacesInPostgres(options);
        Assert.Equal(2, await db.DevelopmentWorkspaces.CountAsync());
        Assert.Equal(2, await db.AuditEvents.CountAsync(row => row.Action == "development.workspace.register"));
        var downgrade = await Assert.ThrowsAsync<PostgresException>(() =>
            db.GetService<IMigrator>().MigrateAsync("20261109100356_WorkspaceReportGrants"));
        Assert.Equal("development_workspace_history_present", downgrade.MessageText);
        Assert.False(db.Database.HasPendingModelChanges());
        Console.WriteLine("INBOX_PHASE=development-workspace-registration-real-http-postgres-pins-idempotency-quota-owner");
    }
}
