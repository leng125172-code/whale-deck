using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Primitives;
using WhaleDeck.Api.Controllers;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Persistence;

namespace WhaleDeck.IntegrationTests;

public sealed class FileTreeOverwriteRecoveryIntegrationTests
{
    [Fact]
    public void TenthKindExpandsWithoutRemovingHistoricalEvidenceAndGuardsDowngrade()
    {
        using var db = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql("Host=127.0.0.1;Database=offline_model_only").Options);
        Assert.False(db.Database.HasPendingModelChanges());
        var model = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(FileRecoveryOperation))!;
        Assert.Equal(32, model.FindProperty(nameof(FileRecoveryOperation.Kind))!.GetMaxLength());
        Assert.Contains(FileRecoveryKinds.TreeOverwriteRestoring, Assert.Single(model.GetCheckConstraints(), constraint => constraint.Name == "ck_file_recovery_kind").Sql, StringComparison.Ordinal);
        var assembly = db.GetService<IMigrationsAssembly>();
        var entry = Assert.Single(assembly.Migrations, row => row.Key.EndsWith("_FileTreeOverwriteAdministratorRecovery", StringComparison.Ordinal));
        var migration = assembly.CreateMigration(entry.Value, db.Database.ProviderName!);
        Assert.DoesNotContain(migration.UpOperations, row => row is DropTableOperation or DropColumnOperation or DeleteDataOperation or UpdateDataOperation);
        var column = Assert.Single(migration.UpOperations.OfType<AlterColumnOperation>());
        Assert.Equal(16, column.OldColumn.MaxLength); Assert.Equal(32, column.MaxLength);
        Assert.Contains("tree_overwrite_recovery_downgrade_requires_empty_kind", Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OverwriteRoutesAreSeparateAdministratorOnlyNoStoreAndRejectAmbiguousWriteKeys()
    {
        var type = typeof(FileTreeOverwriteRestoringsController);
        Assert.Equal("api/v1/files/tree-overwrite-restorings", Assert.Single(type.GetCustomAttributes(typeof(RouteAttribute), true).Cast<RouteAttribute>()).Template);
        Assert.Contains(type.GetCustomAttributes(typeof(AuthorizeAttribute), true), item => ((AuthorizeAttribute)item).Policy == "Administrator");
        var cache = Assert.Single(type.GetCustomAttributes(typeof(ResponseCacheAttribute), true).Cast<ResponseCacheAttribute>());
        Assert.True(cache.NoStore); Assert.Equal(ResponseCacheLocation.None, cache.Location);
        var controller = new FileTreeOverwriteRestoringsController(null!, null!) { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        var id = Guid.NewGuid();
        Assert.IsType<BadRequestResult>((await controller.Queue(null!, default)).Result);
        Assert.IsType<BadRequestResult>((await controller.Confirm(id, new("unused", true, 0), default)).Result);
        Assert.IsType<BadRequestResult>((await controller.Cancel(id, default)).Result);
        Assert.IsType<BadRequestResult>(await controller.Acknowledge(id, new(0, true), default));
        controller.Request.Headers["Idempotency-Key"] = new StringValues([id.ToString("D"), id.ToString("D")]);
        Assert.IsType<BadRequestResult>((await controller.Cancel(id, default)).Result);
        controller.Request.Headers["Idempotency-Key"] = Guid.NewGuid().ToString("D");
        Assert.IsType<BadRequestResult>((await controller.Confirm(id, new("unused", true, 0), default)).Result);
    }
}
