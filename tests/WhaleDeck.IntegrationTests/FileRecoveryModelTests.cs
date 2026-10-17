using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Persistence;

namespace WhaleDeck.IntegrationTests;

public sealed class FileRecoveryModelTests
{
    [Fact]
    public void IndependentRecoveryHasThreeRestrictedJobsOneActiveReservationAndImmutableEvidence()
    {
        using var db = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql("Host=127.0.0.1;Database=offline_model_only").Options);
        Assert.False(db.Database.HasPendingModelChanges());
        var model = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(FileRecoveryOperation))!;
        Assert.True(model.FindProperty(nameof(FileRecoveryOperation.Version))!.IsConcurrencyToken);
        Assert.Equal(3, model.GetForeignKeys().Count()); Assert.All(model.GetForeignKeys(), key => Assert.Equal(DeleteBehavior.Restrict, key.DeleteBehavior));
        var reservation = Assert.Single(model.GetIndexes(), index => index.Properties.Select(property => property.Name).SequenceEqual([nameof(FileRecoveryOperation.Kind), nameof(FileRecoveryOperation.OriginalIntentId)]));
        Assert.True(reservation.IsUnique); Assert.Equal("completed_at_utc IS NULL", reservation.GetFilter());
        var assembly = db.GetService<IMigrationsAssembly>();
        var entry = Assert.Single(assembly.Migrations, migration => migration.Key.EndsWith("_FileAdministratorRecoveryWorkflow", StringComparison.Ordinal));
        var migration = assembly.CreateMigration(entry.Value, db.Database.ProviderName!);
        Assert.DoesNotContain(migration.UpOperations, operation => operation is DropTableOperation or DropColumnOperation or DeleteDataOperation);
        Assert.Equal("file_recovery_operations", Assert.Single(migration.UpOperations.OfType<CreateTableOperation>()).Name);
        var sql = Assert.Single(migration.UpOperations.OfType<SqlOperation>()).Sql;
        foreach (var guard in new[] { "request", "plan", "execution", "fence", "terminal" }) Assert.Contains("file_recovery_" + guard + "_immutable", sql, StringComparison.Ordinal);
        Assert.Contains("file_recovery_permanent_intent", sql, StringComparison.Ordinal); Assert.DoesNotContain("SECURITY DEFINER", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("file_recovery_downgrade_requires_empty_table", Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql, StringComparison.Ordinal);
    }
}
