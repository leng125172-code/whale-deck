using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Persistence;

namespace WhaleDeck.IntegrationTests;

public sealed class FileTreePurgingModelTests
{
    private static readonly string[] CaptureColumns = ["CaptureId", "Id"];
    [Fact]
    public void PermanentCleanupEvidenceHasRestrictedBindingsAndAdditiveImmutableMigration()
    {
        using var db = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql("Host=127.0.0.1;Database=offline_model_only").Options);
        Assert.False(db.Database.HasPendingModelChanges());
        var model = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(FileTreePurgingOperation))!;
        Assert.True(model.FindProperty(nameof(FileTreePurgingOperation.Version))!.IsConcurrencyToken);
        Assert.Equal(3, model.GetForeignKeys().Count()); Assert.All(model.GetForeignKeys(), item => Assert.Equal(DeleteBehavior.Restrict, item.DeleteBehavior));
        Assert.Contains(model.GetIndexes(), item => item.IsUnique && item.GetFilter() == "execution_job_id IS NOT NULL");
        Assert.Contains(model.GetIndexes(), item => item.Properties.Select(property => property.Name).SequenceEqual(CaptureColumns));
        foreach (var constraint in new[] { "state", "terminal", "hashes", "limits", "capture", "plan", "execution", "lease", "effects", "receipt", "outcome" })
            Assert.Contains(model.GetCheckConstraints(), item => item.Name == "ck_tree_purging_" + constraint);
        var assembly = db.GetService<IMigrationsAssembly>(); var type = Assert.Single(assembly.Migrations, item => item.Key.EndsWith("_FileTreePurgingWorkflow", StringComparison.Ordinal));
        var migration = assembly.CreateMigration(type.Value, db.Database.ProviderName!);
        Assert.DoesNotContain(migration.UpOperations, item => item is DropTableOperation or DropColumnOperation or DeleteDataOperation or UpdateDataOperation);
        Assert.Equal("file_tree_purging_operations", Assert.Single(migration.UpOperations.OfType<CreateTableOperation>()).Name);
        var sql = Assert.Single(migration.UpOperations.OfType<SqlOperation>()).Sql;
        foreach (var invariant in new[] { "permanent_intent", "request_immutable", "plan_immutable", "execution_immutable", "started_immutable", "receipt_immutable", "effects_monotonic", "terminal_immutable", "capture_origin_required" })
            Assert.Contains(invariant, sql, StringComparison.Ordinal);
        Assert.DoesNotContain("SECURITY DEFINER", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tree_purging_downgrade_requires_empty_kind", Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql, StringComparison.Ordinal);
    }
}
