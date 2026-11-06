using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Persistence;

namespace WhaleDeck.IntegrationTests;

public sealed class ContainerRecycleArchiveModelTests
{
    [Fact]
    public void ArchiveMigrationKeepsBothStagesAndAllWriterFencesWithoutRemovingExistingData()
    {
        using var db=new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql("Host=localhost;Database=model_only;Username=model_only").Options);
        var model=db.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>().Model.FindEntityType(typeof(ContainerRecycleArchiveIntent))!;
        Assert.Equal("container_recycle_archive_intents",model.GetTableName());Assert.Equal(21,model.GetCheckConstraints().Count());
        foreach(var field in new[]{"SourceProofHash","ReservationJson","ReservationHash","RootStartedAtUtc","ApplyStartedAtUtc","ArchiveCopied","PublishBarrier","BindingCurrent"})Assert.NotNull(model.FindProperty(field));
        Assert.Contains(model.GetForeignKeys(),x=>x.PrincipalEntityType.ClrType==typeof(ContainerRecyclePurgeIntent));
        var assembly=db.GetService<IMigrationsAssembly>();var entry=Assert.Single(assembly.Migrations,x=>x.Key.EndsWith("_ContainerRecycleArchiveIntents",StringComparison.Ordinal));var migration=assembly.CreateMigration(entry.Value,db.Database.ProviderName!);
        Assert.DoesNotContain(migration.UpOperations,x=>x is DropTableOperation or DropColumnOperation or DeleteDataOperation);Assert.Equal("container_recycle_archive_intents",Assert.Single(migration.UpOperations.OfType<CreateTableOperation>()).Name);
        var sql=string.Join('\n',migration.UpOperations.OfType<SqlOperation>().Select(x=>x.Sql));
        foreach(var marker in new[]{"pg_advisory_xact_lock(41307341)","whaledeck_recycle_archive_fence_15","container_recycle_archive_source_fence","reservation_immutable","fact_immutable","permanent_intent"})Assert.Contains(marker,sql,StringComparison.Ordinal);
        Assert.False(db.Database.HasPendingModelChanges());
    }
}
