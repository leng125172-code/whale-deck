using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Persistence;

namespace WhaleDeck.IntegrationTests;

public sealed class ContainerRecursiveOwnershipModelTests
{
    [Fact]
    public void MigrationKeepsPartialFactsDoubleJobsAndEveryOldPublisherBehindTheSharedFence()
    {
        using var db = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql("Host=127.0.0.1;Database=offline_model_only").Options);
        Assert.False(db.Database.HasPendingModelChanges());
        var model = db.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>().Model.FindEntityType(typeof(ContainerRecursiveOwnershipIntent))!;
        Assert.Equal(16, model.GetCheckConstraints().Count());
        Assert.True(model.FindProperty(nameof(ContainerRecursiveOwnershipIntent.Version))!.IsConcurrencyToken);
        Assert.Equal(2, model.GetForeignKeys().Count()); Assert.All(model.GetForeignKeys(), fk => Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior));
        Assert.Contains(model.GetIndexes(), i => i.IsUnique && i.GetFilter() == "publish_barrier");
        foreach (var field in new[] { "ReceiptJson", "RootState", "CompletedCount", "ChangedCount", "AbortedCount", "NotStartedCount", "UnknownCount", "OutcomeDigest", "HistoryAvailable" }) Assert.NotNull(model.FindProperty(field));
        Assert.Null(model.FindProperty("PublishedVersion"));
        var assembly = db.GetService<IMigrationsAssembly>(); var entry = Assert.Single(assembly.Migrations, x => x.Key.EndsWith("_ContainerRecursiveOwnershipIntents", StringComparison.Ordinal));
        var migration = assembly.CreateMigration(entry.Value, db.Database.ProviderName!);
        Assert.DoesNotContain(migration.UpOperations, x => x is DropTableOperation or DropColumnOperation or DeleteDataOperation);
        Assert.Equal("container_recursive_ownership_intents", Assert.Single(migration.UpOperations.OfType<CreateTableOperation>()).Name);
        var sql = string.Join('\n', migration.UpOperations.OfType<SqlOperation>().Select(x => x.Sql));
        foreach (var marker in new[] { "request_immutable", "plan_immutable", "execution_immutable", "started_immutable", "fact_immutable", "terminal_immutable", "permanent_intent", "resolution_required",
            "host_path_fence", "upload_fence", "directory_fence", "copy_fence", "move_fence", "permission_fence", "ownership_fence", "recursive_permission_fence", "publication_fence", "pg_advisory_xact_lock(41307341)" }) Assert.Contains(marker, sql, StringComparison.Ordinal);
        Assert.DoesNotContain("SECURITY DEFINER", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("container_recursive_ownership_downgrade_requires_empty_table", Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql, StringComparison.Ordinal);
    }
}
