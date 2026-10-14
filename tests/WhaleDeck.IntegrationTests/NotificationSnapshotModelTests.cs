using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Persistence;

namespace WhaleDeck.IntegrationTests;

/// <summary>Offline metadata evidence only; does not replace real PostgreSQL concurrency tests.</summary>
public sealed class NotificationSnapshotModelTests
{
    [Fact]
    public void CurrentIntentIndexAndDefaultsMatchMigrationSnapshot()
    {
        using var db = CreateContext();
        var model = db.GetService<IDesignTimeModel>().Model;
        var entity = model.FindEntityType(typeof(NotificationReadSnapshot))!;
        Assert.True((bool)entity.FindProperty(nameof(NotificationReadSnapshot.IsCurrent))!.GetDefaultValue()!);
        var index = Assert.Single(entity.GetIndexes(), item => item.Properties.Count == 1 && item.Properties[0].Name == nameof(NotificationReadSnapshot.Subject));
        Assert.True(index.IsUnique); Assert.Equal("is_current", index.GetFilter());
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public void UpgradePreservesOldHeadersAndDowngradeGuardsAgainstDeletingHistory()
    {
        using var db = CreateContext();
        var assembly = db.GetService<IMigrationsAssembly>();
        var migration = assembly.CreateMigration(assembly.Migrations["20261014022643_NotificationSnapshotBoundedRetention"], db.Database.ProviderName!);
        var added = Assert.Single(migration.UpOperations.OfType<AddColumnOperation>());
        Assert.Equal("is_current", added.Name); Assert.Equal(true, added.DefaultValue); Assert.False(added.IsNullable);
        Assert.DoesNotContain(migration.UpOperations, operation => operation is DropTableOperation or DeleteDataOperation or SqlOperation);
        var index = Assert.Single(migration.UpOperations.OfType<CreateIndexOperation>());
        Assert.True(index.IsUnique); Assert.Equal("is_current", index.Filter);
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("RAISE EXCEPTION", guard.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE FROM", guard.Sql, StringComparison.OrdinalIgnoreCase);
    }

    private static PlatformDbContext CreateContext() => new(new DbContextOptionsBuilder<PlatformDbContext>()
        .UseNpgsql("Host=127.0.0.1;Database=offline_model_only").Options);
}
