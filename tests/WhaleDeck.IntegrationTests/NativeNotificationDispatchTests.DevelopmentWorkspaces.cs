using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using WhaleDeck.Application.Development;
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
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261109101257_DevelopmentWorkspaceRegistry");
        var legacyId = Guid.NewGuid();
        var registrationKey = Guid.NewGuid();
        var created = DateTimeOffset.UtcNow;
        var manifest = DevelopmentWorkspaces.EncodeEnvironment(new(1, []));
        var sha = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(manifest)));
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO development_workspaces (id, slug, owner_subject, storage_root_id, state, version,
                cpu_millis, memory_mi_b, disk_mi_b, environment_json, environment_sha256, registration_key, request_sha256, created_at_utc)
            VALUES ({legacyId}, 'legacy-registered', 'migration-fixture', 'qa-workspace-root', 'Registered', 1,
                200, 256, 512, {manifest}, {sha}, {registrationKey}, {sha}, {created})
            """);
        var registrationDowngrade = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync("20261109100356_WorkspaceReportGrants"));
        Assert.Equal("development_workspace_history_present", registrationDowngrade.MessageText);
        await db.Database.MigrateAsync();
        var upgraded = await db.DevelopmentWorkspaces.AsNoTracking().SingleAsync(row => row.Id == legacyId);
        Assert.Equal(WorkspaceStorageAllocation.Registered, upgraded.State);
        Assert.Equal(1, upgraded.Version);
        Assert.Equal(sha, upgraded.EnvironmentSha256);
        Assert.Null(upgraded.StoragePlanSha256);
        Assert.Null(upgraded.StorageAllocationStartedAtUtc);
        Assert.Null(upgraded.StorageAllocatedAtUtc);
        var invalid = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE development_workspaces SET state = 'StorageAllocated', version = 3 WHERE id = {legacyId}
            """));
        Assert.Equal("ck_development_workspace_storage_state", invalid.ConstraintName);
        await db.DevelopmentWorkspaces.Where(row => row.Id == legacyId).ExecuteDeleteAsync();
        await ContainerUploadHttpTests.RegisterDevelopmentWorkspacesInPostgres(options);
        Assert.Equal(2, await db.DevelopmentWorkspaces.CountAsync());
        Assert.Equal(2, await db.AuditEvents.CountAsync(row => row.Action == "development.workspace.register"));
        var downgrade = await Assert.ThrowsAsync<PostgresException>(() =>
            migrator.MigrateAsync("20261109101257_DevelopmentWorkspaceRegistry"));
        Assert.Equal("development_workspace_storage_history_present", downgrade.MessageText);
        await db.Database.MigrateAsync();
        Assert.False(db.Database.HasPendingModelChanges());
        Console.WriteLine("INBOX_PHASE=development-workspace-registration-real-http-postgres-pins-idempotency-quota-owner");
        await ContainerUploadHttpTests.RunWorkspaceJobsInPostgres(options);
        var runDowngrade = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync("20261109105105_DevelopmentWorkspaceStorageAllocation"));
        Assert.Equal("workspace_report_workflow_history_present", runDowngrade.MessageText);
        await db.Database.MigrateAsync();
        await ContainerUploadHttpTests.ImportWorkspaceSourcesInPostgres(options);
        var sourceDowngrade = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync("20261109140111_WorkspaceReportWorkflow"));
        Assert.Equal("workspace_source_import_history_present", sourceDowngrade.MessageText);
        await db.Database.MigrateAsync();
        await ContainerUploadHttpTests.ImportWorkspaceGitInPostgres(options);
        var updateDowngrade = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync("20261109162712_DevelopmentWorkspaceGitSources"));
        Assert.Equal("workspace_source_update_history_present", updateDowngrade.MessageText);
    }
}
