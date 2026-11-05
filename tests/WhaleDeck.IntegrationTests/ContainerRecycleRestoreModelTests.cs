using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Persistence;

namespace WhaleDeck.IntegrationTests;

public sealed class ContainerRecycleRestoreModelTests
{
    [Fact]
    public void IndependentRestoreModelAndAdditiveMigrationPreserveSourceAndAllWriterFences()
    {
        using var db=new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql("Host=localhost;Database=model_only;Username=model_only").Options);
        var model=db.Model.FindEntityType(typeof(ContainerRecycleRestoreIntent))!;Assert.Equal("container_recycle_restore_intents",model.GetTableName());
        Assert.Contains(model.GetForeignKeys(),k=>k.PrincipalEntityType.ClrType==typeof(ContainerRecycleIntent));
        foreach(var property in new[]{"SourceIntentId","SourceProofHash","SourceState","RestoredByThisOperation","ReceiptHash","PublishBarrier"})Assert.NotNull(model.FindProperty(property));
        Assert.Null(model.FindProperty("HistoryMayExist"));Assert.Equal(19,db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(ContainerRecycleRestoreIntent))!.GetCheckConstraints().Count());
        var assembly=db.GetService<IMigrationsAssembly>();var entry=Assert.Single(assembly.Migrations,x=>x.Key.EndsWith("_ContainerRecycleRestoreIntents",StringComparison.Ordinal));var migration=assembly.CreateMigration(entry.Value,db.Database.ProviderName!);
        Assert.DoesNotContain(migration.UpOperations,x=>x is DropTableOperation or DropColumnOperation or DeleteDataOperation);
        Assert.Equal("container_recycle_restore_intents",Assert.Single(migration.UpOperations.OfType<CreateTableOperation>()).Name);
        var sql=string.Join('\n',migration.UpOperations.OfType<SqlOperation>().Select(x=>x.Sql));
        foreach(var marker in new[]{"request_immutable","plan_immutable","execution_immutable","started_immutable","fact_immutable","terminal_immutable","receipt_authority_required","source_proof_hash","whaledeck_recycle_restore_fence_13","RestoredByThisOperation","pg_advisory_xact_lock(41307341)"})Assert.Contains(marker,sql,StringComparison.Ordinal);
        Assert.DoesNotContain("SECURITY DEFINER",sql,StringComparison.OrdinalIgnoreCase);Assert.Contains("container_recycle_restore_downgrade_requires_empty_table",Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql,StringComparison.Ordinal);
        Assert.False(db.Database.HasPendingModelChanges());
    }
}
