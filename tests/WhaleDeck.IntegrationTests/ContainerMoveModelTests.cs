using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Primitives;
using WhaleDeck.Api.Controllers;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Persistence;

namespace WhaleDeck.IntegrationTests;

public sealed class ContainerMoveModelTests
{
    [Fact]
    public void MoveIntentHasRestrictedDoubleJobsConcurrencyConstraintsAndPermanentHistory()
    {
        using var db = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql("Host=127.0.0.1;Database=offline_model_only").Options);
        Assert.False(db.Database.HasPendingModelChanges());
        var type = db.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>().Model.FindEntityType(typeof(ContainerMoveIntent))!;
        Assert.True(type.FindProperty(nameof(ContainerMoveIntent.Version))!.IsConcurrencyToken);
        Assert.Equal(2, type.GetForeignKeys().Count()); Assert.All(type.GetForeignKeys(), fk => Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior));
        Assert.Contains(type.GetIndexes(), i => i.IsUnique && i.GetFilter() == "publish_barrier");
        Assert.Equal(13, type.GetCheckConstraints().Count());
        Assert.DoesNotContain(type.GetProperties(), p => p.Name.Contains("Password", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("Content", StringComparison.OrdinalIgnoreCase));
        var assembly = db.GetService<IMigrationsAssembly>(); var entry = Assert.Single(assembly.Migrations, x => x.Key.EndsWith("_ContainerMoveIntents", StringComparison.Ordinal));
        var migration = assembly.CreateMigration(entry.Value, db.Database.ProviderName!);
        Assert.DoesNotContain(migration.UpOperations, x => x is DropTableOperation or DropColumnOperation or DeleteDataOperation);
        Assert.Equal("container_move_intents", Assert.Single(migration.UpOperations.OfType<CreateTableOperation>()).Name);
        var sql = string.Join('\n', migration.UpOperations.OfType<SqlOperation>().Select(x => x.Sql));
        foreach (var marker in new[] { "container_move_request_immutable", "container_move_plan_immutable", "container_move_execution_immutable", "container_move_started_immutable",
            "container_move_terminal_immutable", "container_move_permanent_intent", "whaledeck_move_host_path_fence", "whaledeck_move_upload_fence", "whaledeck_move_directory_fence", "pg_advisory_xact_lock(41307341)", "container_move_fact_immutable", "whaledeck_move_copy_fence", "container_copy_intents" })
            Assert.Contains(marker, sql, StringComparison.Ordinal);
        Assert.DoesNotContain("SECURITY DEFINER", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("container_move_downgrade_requires_empty_table", Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql, StringComparison.Ordinal);
    }
}
