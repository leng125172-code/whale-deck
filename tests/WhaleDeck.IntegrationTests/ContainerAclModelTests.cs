using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Persistence;

namespace WhaleDeck.IntegrationTests;

public sealed class ContainerAclModelTests
{
    [Fact]
    public void AclMigrationPreservesDoubleJobsImmutableHistoryAndAllTenSqlFences()
    {
        using var db = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql("Host=127.0.0.1;Database=offline_model_only").Options);
        Assert.False(db.Database.HasPendingModelChanges());
        var type = db.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>().Model.FindEntityType(typeof(ContainerAclIntent))!;
        Assert.True(type.FindProperty(nameof(ContainerAclIntent.Version))!.IsConcurrencyToken);
        Assert.Equal(2, type.GetForeignKeys().Count());
        Assert.All(type.GetForeignKeys(), fk => Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior));
        Assert.Contains(type.GetIndexes(), i => i.IsUnique && i.GetFilter() == "publish_barrier");
        Assert.Equal(18, type.GetCheckConstraints().Count());
        Assert.True(type.FindProperty(nameof(ContainerAclIntent.RestoreSourceJson))!.IsNullable);
        Assert.NotNull(type.FindProperty(nameof(ContainerAclIntent.AclConfirmed)));
        Assert.Null(type.FindProperty("ModeConfirmed"));
        Assert.NotNull(type.FindProperty(nameof(ContainerAclIntent.ReceiptHash)));
        var assembly = db.GetService<IMigrationsAssembly>();
        var entry = Assert.Single(assembly.Migrations, x => x.Key.EndsWith("_ContainerAclIntents", StringComparison.Ordinal));
        var migration = assembly.CreateMigration(entry.Value, db.Database.ProviderName!);
        Assert.DoesNotContain(migration.UpOperations, x => x is DropTableOperation or DropColumnOperation or DeleteDataOperation);
        Assert.Equal("container_acl_intents", Assert.Single(migration.UpOperations.OfType<CreateTableOperation>()).Name);
        var sql = string.Join('\n', migration.UpOperations.OfType<SqlOperation>().Select(x => x.Sql));
        foreach (var marker in new[] { "container_acl_request_immutable", "container_acl_plan_immutable", "container_acl_execution_immutable",
            "container_acl_started_immutable", "container_acl_fact_immutable", "container_acl_terminal_immutable", "container_acl_permanent_intent",
            "whaledeck_acl_host_path_fence", "whaledeck_acl_upload_fence", "whaledeck_acl_directory_fence", "whaledeck_acl_copy_fence",
            "whaledeck_acl_move_fence", "whaledeck_acl_permission_fence", "whaledeck_acl_publication_fence", "whaledeck_acl_ownership_fence", "whaledeck_acl_recursive_permission_fence", "whaledeck_acl_recursive_ownership_fence", "whaledeck_acl_entries_valid", "whaledeck_acl_mode", "NEW.receipt_hash", "pg_advisory_xact_lock(41307341)" })
            Assert.Contains(marker, sql, StringComparison.Ordinal);
        Assert.DoesNotContain("SECURITY DEFINER", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("container_acl_downgrade_requires_empty_table", Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql, StringComparison.Ordinal);
    }
}
