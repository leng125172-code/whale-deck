using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Persistence;

namespace WhaleDeck.IntegrationTests;

public sealed class ContainerOwnershipModelTests
{
    [Fact]
    public void OwnershipMigrationPreservesDoubleJobsImmutableHistoryAndAllSevenSqlFences()
    {
        using var db = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql("Host=127.0.0.1;Database=offline_model_only").Options);
        Assert.False(db.Database.HasPendingModelChanges());
        var type = db.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>().Model.FindEntityType(typeof(ContainerOwnershipIntent))!;
        Assert.True(type.FindProperty(nameof(ContainerOwnershipIntent.Version))!.IsConcurrencyToken);
        Assert.Equal(2, type.GetForeignKeys().Count());
        Assert.All(type.GetForeignKeys(), fk => Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior));
        Assert.Contains(type.GetIndexes(), i => i.IsUnique && i.GetFilter() == "publish_barrier");
        Assert.Equal(13, type.GetCheckConstraints().Count());
        Assert.NotNull(type.FindProperty(nameof(ContainerOwnershipIntent.OwnerConfirmed)));
        Assert.Null(type.FindProperty("ModeConfirmed"));
        var assembly = db.GetService<IMigrationsAssembly>();
        var entry = Assert.Single(assembly.Migrations, x => x.Key.EndsWith("_ContainerOwnershipIntents", StringComparison.Ordinal));
        var migration = assembly.CreateMigration(entry.Value, db.Database.ProviderName!);
        Assert.DoesNotContain(migration.UpOperations, x => x is DropTableOperation or DropColumnOperation or DeleteDataOperation);
        Assert.Equal("container_ownership_intents", Assert.Single(migration.UpOperations.OfType<CreateTableOperation>()).Name);
        var sql = string.Join('\n', migration.UpOperations.OfType<SqlOperation>().Select(x => x.Sql));
        foreach (var marker in new[] { "container_ownership_request_immutable", "container_ownership_plan_immutable", "container_ownership_execution_immutable",
            "container_ownership_started_immutable", "container_ownership_fact_immutable", "container_ownership_terminal_immutable", "container_ownership_permanent_intent",
            "whaledeck_ownership_host_path_fence", "whaledeck_ownership_upload_fence", "whaledeck_ownership_directory_fence", "whaledeck_ownership_copy_fence",
            "whaledeck_ownership_move_fence", "whaledeck_ownership_permission_fence", "whaledeck_ownership_publication_fence", "pg_advisory_xact_lock(41307341)" })
            Assert.Contains(marker, sql, StringComparison.Ordinal);
        Assert.DoesNotContain("SECURITY DEFINER", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("container_ownership_downgrade_requires_empty_table", Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql, StringComparison.Ordinal);
    }
}
