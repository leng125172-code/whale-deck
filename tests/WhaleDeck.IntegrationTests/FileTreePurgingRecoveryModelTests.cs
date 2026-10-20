using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Persistence;
using WhaleDeck.Application.Models;

namespace WhaleDeck.IntegrationTests;

public sealed class FileTreePurgingRecoveryModelTests
{
    [Fact]
    public void EleventhKindExpandsConstraintWithoutReplacingAnyHistoricalEvidence()
    {
        using var db = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql("Host=127.0.0.1;Database=offline_model_only").Options);
        Assert.False(db.Database.HasPendingModelChanges()); Assert.Equal(11, FileRecoveryKinds.All.Count);
        var model = db.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>().Model.FindEntityType(typeof(FileRecoveryOperation))!;
        Assert.Contains(FileRecoveryKinds.TreePurging, Assert.Single(model.GetCheckConstraints(), constraint => constraint.Name == "ck_file_recovery_kind").Sql, StringComparison.Ordinal);
        var assembly = db.GetService<IMigrationsAssembly>(); var entry = Assert.Single(assembly.Migrations, row => row.Key.EndsWith("_FileTreePurgingAdministratorRecovery", StringComparison.Ordinal));
        var migration = assembly.CreateMigration(entry.Value, db.Database.ProviderName!);
        Assert.Equal(2, migration.UpOperations.Count);
        Assert.IsType<DropCheckConstraintOperation>(migration.UpOperations[0]); Assert.IsType<AddCheckConstraintOperation>(migration.UpOperations[1]);
        Assert.Contains("tree_purging_recovery_downgrade_requires_empty_kind", Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql, StringComparison.Ordinal);
    }
}
