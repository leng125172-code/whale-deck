using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Persistence;

namespace WhaleDeck.IntegrationTests;

public sealed class FileHierarchyLockModelTests
{
    [Fact]
    public void HierarchyMigrationRetainsOpaqueLocksAndAddsExplicitLegacyScopes()
    {
        using var db = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql("Host=127.0.0.1;Database=offline_model_only").Options);
        Assert.False(db.Database.HasPendingModelChanges());
        var assembly = db.GetService<IMigrationsAssembly>();
        var type = Assert.Single(assembly.Migrations, item => item.Key.EndsWith("_FileHierarchyLockScopes", StringComparison.Ordinal));
        var migration = assembly.CreateMigration(type.Value, db.Database.ProviderName!);
        Assert.DoesNotContain(migration.UpOperations, item => item is DropTableOperation or DropColumnOperation or DeleteDataOperation or SqlOperation);
        var added = migration.UpOperations.OfType<AddColumnOperation>().ToArray(); Assert.Equal(3, added.Length);
        var kind = Assert.Single(added, item => item.Name == "scope_kind"); Assert.Equal("Legacy", kind.DefaultValue); Assert.False(kind.IsNullable);
        Assert.All(added.Where(item => item.Name != "scope_kind"), item => Assert.True(item.IsNullable));
        var model = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(FilePathLock))!;
        Assert.Equal(nameof(FilePathLock.PathHash), Assert.Single(model.FindPrimaryKey()!.Properties).Name);
        Assert.Equal(DeleteBehavior.Restrict, Assert.Single(model.GetForeignKeys()).DeleteBehavior);
        Assert.Contains(model.GetCheckConstraints(), item => item.Name == "ck_file_path_scope");
    }
}
