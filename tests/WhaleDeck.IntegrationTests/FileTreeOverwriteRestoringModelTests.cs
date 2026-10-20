using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Persistence;

namespace WhaleDeck.IntegrationTests;

public sealed class FileTreeOverwriteRestoringModelTests
{
    [Fact]
    public void OverwriteWorkflowAndBothCaptureKindsHaveRestrictedBindingsAndImmutableMigration()
    {
        using var db = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql("Host=127.0.0.1;Database=offline_model_only").Options);
        Assert.False(db.Database.HasPendingModelChanges());
        var model = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(FileTreeOverwriteRestoringOperation))!;
        Assert.True(model.FindProperty(nameof(FileTreeOverwriteRestoringOperation.Version))!.IsConcurrencyToken);
        Assert.Equal(4, model.GetForeignKeys().Count()); Assert.All(model.GetForeignKeys(), item => Assert.Equal(DeleteBehavior.Restrict, item.DeleteBehavior));
        Assert.True(model.FindProperty(nameof(FileTreeOverwriteRestoringOperation.State))!.GetMaxLength() >= "OverwriteRestoringQueued".Length);
        var captures = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(FileTreeCapture))!;
        Assert.Equal("Trash", captures.FindProperty(nameof(FileTreeCapture.Kind))!.GetDefaultValue());
        Assert.True(captures.FindProperty(nameof(FileTreeCapture.ContentSha256))!.IsNullable);
        Assert.All(captures.GetForeignKeys(), key => Assert.Equal(typeof(OperationJob), key.PrincipalEntityType.ClrType));
        var assembly = db.GetService<IMigrationsAssembly>(); var type = Assert.Single(assembly.Migrations, item => item.Key.EndsWith("_FileTreeOverwriteRestoringWorkflow", StringComparison.Ordinal));
        var migration = assembly.CreateMigration(type.Value, db.Database.ProviderName!);
        Assert.DoesNotContain(migration.UpOperations, item => item is DropTableOperation or DropColumnOperation or DeleteDataOperation or UpdateDataOperation);
        Assert.Equal("file_tree_overwrite_restoring_operations", Assert.Single(migration.UpOperations.OfType<CreateTableOperation>()).Name);
        var sql = Assert.Single(migration.UpOperations.OfType<SqlOperation>()).Sql;
        foreach (var invariant in new[] { "request_immutable", "plan_immutable", "execution_immutable", "started_immutable", "backup_immutable", "effects_monotonic", "terminal_immutable", "tree_capture_origin_required" })
            Assert.Contains(invariant, sql, StringComparison.Ordinal);
        Assert.DoesNotContain("SECURITY DEFINER", sql, StringComparison.OrdinalIgnoreCase);
        var down = Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql;
        Assert.Contains("tree_overwrite_restoring_downgrade_requires_empty_kind", down, StringComparison.Ordinal);
        Assert.Contains("command_json->>'CaptureKind'='Overwrite'", down, StringComparison.Ordinal);
    }
}
