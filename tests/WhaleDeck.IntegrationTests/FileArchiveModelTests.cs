using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Persistence;

namespace WhaleDeck.IntegrationTests;

/// <summary>Offline schema evidence only. SQL triggers are also exercised in the isolated native suite.</summary>
public sealed class FileArchiveModelTests
{
    [Fact]
    public void PlanReferenceMigrationBackfillsOldJobsBeforeAddingRestrictAndNeverDropsEvidence()
    {
        using var db = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql("Host=127.0.0.1;Database=offline_model_only").Options);
        Assert.False(db.Database.HasPendingModelChanges());
        var assembly = db.GetService<IMigrationsAssembly>();
        var migration = assembly.CreateMigration(assembly.Migrations["20261017064445_FileArchiveRegistrations"], db.Database.ProviderName!);
        Assert.Single(migration.UpOperations.OfType<CreateTableOperation>());
        var sql = Assert.Single(migration.UpOperations.OfType<SqlOperation>()).Sql;
        Assert.Contains("FROM file_archive_operations a JOIN operation_jobs", sql, StringComparison.Ordinal);
        Assert.Contains("file_archive_registration_immutable", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE FROM", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(migration.UpOperations, item => item is DropTableOperation or DropColumnOperation or DeleteDataOperation);
        Assert.Contains("archive_registration_downgrade_requires_empty_table", Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql, StringComparison.Ordinal);
    }
    [Fact]
    public void HistorySchemaIsAdditiveTypedAndMatchesMigrationSnapshot()
    {
        using var db = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql("Host=127.0.0.1;Database=offline_model_only").Options);
        Assert.False(db.Database.HasPendingModelChanges());
        var assembly = db.GetService<IMigrationsAssembly>();
        var migration = assembly.CreateMigration(assembly.Migrations["20261017044943_FileHistoryAndRecycle"], db.Database.ProviderName!);
        Assert.DoesNotContain(migration.UpOperations, item => item is DropTableOperation or DropColumnOperation or DeleteDataOperation);
        var tables = migration.UpOperations.OfType<CreateTableOperation>().ToArray(); Assert.Equal(2, tables.Length);
        Assert.Contains(tables, item => item.Name == "file_versions"); Assert.Contains(tables, item => item.Name == "file_archive_operations");
        var sql = Assert.Single(migration.UpOperations.OfType<SqlOperation>()).Sql;
        Assert.Contains("file_version_identity_immutable", sql, StringComparison.Ordinal);
        Assert.Contains("file_version_actor_mismatch", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("SECURITY DEFINER", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM", sql, StringComparison.OrdinalIgnoreCase);
        var model = db.GetService<IDesignTimeModel>().Model;
        foreach (var type in new[] { typeof(FileVersion), typeof(FileArchiveOperation) })
        {
            var entity = model.FindEntityType(type)!; Assert.True(entity.FindProperty("Version")!.IsConcurrencyToken);
            Assert.DoesNotContain(entity.GetProperties(), item => item.Name.Contains("Password", StringComparison.OrdinalIgnoreCase) || item.Name.Contains("Content", StringComparison.OrdinalIgnoreCase));
        }
    }
}
