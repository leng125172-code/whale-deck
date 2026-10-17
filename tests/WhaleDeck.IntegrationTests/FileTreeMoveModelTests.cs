using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using WhaleDeck.Api.Controllers;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Persistence;

namespace WhaleDeck.IntegrationTests;

public sealed class FileTreeMoveModelTests
{
    [Fact]
    public void DirectoryWorkflowHasTwoRestrictedJobBindingsAndAdditiveImmutableIntentMigration()
    {
        using var db = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql("Host=127.0.0.1;Database=offline_model_only").Options);
        Assert.False(db.Database.HasPendingModelChanges());
        var model = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(FileTreeMoveOperation))!;
        Assert.True(model.FindProperty(nameof(FileTreeMoveOperation.Version))!.IsConcurrencyToken);
        Assert.All(new[] { nameof(FileTreeMoveOperation.DestinationPublished), nameof(FileTreeMoveOperation.SourceRemoved), nameof(FileTreeMoveOperation.SourceRetained) },
            name => Assert.True(model.FindProperty(name)!.IsNullable));
        Assert.Equal(2, model.GetForeignKeys().Count()); Assert.All(model.GetForeignKeys(), item => Assert.Equal(DeleteBehavior.Restrict, item.DeleteBehavior));
        Assert.DoesNotContain(model.GetProperties(), item => item.Name.Contains("Password", StringComparison.OrdinalIgnoreCase) || item.Name.Contains("Content", StringComparison.OrdinalIgnoreCase));
        var assembly = db.GetService<IMigrationsAssembly>(); var type = Assert.Single(assembly.Migrations, item => item.Key.EndsWith("_FileTreeMoveWorkflow", StringComparison.Ordinal));
        var migration = assembly.CreateMigration(type.Value, db.Database.ProviderName!);
        Assert.DoesNotContain(migration.UpOperations, item => item is DropTableOperation or DropColumnOperation or DeleteDataOperation);
        Assert.Equal("file_tree_move_operations", Assert.Single(migration.UpOperations.OfType<CreateTableOperation>()).Name);
        var sql = Assert.Single(migration.UpOperations.OfType<SqlOperation>()).Sql;
        Assert.Contains("tree_move_request_immutable", sql, StringComparison.Ordinal); Assert.Contains("tree_move_plan_immutable", sql, StringComparison.Ordinal);
        Assert.Contains("tree_move_terminal_immutable", sql, StringComparison.Ordinal); Assert.DoesNotContain("SECURITY DEFINER", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tree_move_downgrade_requires_empty_table", Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql, StringComparison.Ordinal);
    }
    [Fact]
    public async Task DirectoryRoutesAreAdministratorOnlyAndRequireOneExactUuidWriteKey()
    {
        Assert.Contains(typeof(FileTreeMovesController).GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true),
            item => ((Microsoft.AspNetCore.Authorization.AuthorizeAttribute)item).Policy == "Administrator");
        var controller = new FileTreeMovesController(null!, null!) { ControllerContext = new() { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() } };
        var id = Guid.NewGuid();
        Assert.IsType<Microsoft.AspNetCore.Mvc.BadRequestResult>((await controller.Queue(null!, default)).Result);
        Assert.IsType<Microsoft.AspNetCore.Mvc.BadRequestResult>((await controller.Confirm(id, new("unused", true, 0), default)).Result);
        Assert.IsType<Microsoft.AspNetCore.Mvc.BadRequestResult>((await controller.Cancel(id, default)).Result);
        controller.Request.Headers["Idempotency-Key"] = new Microsoft.Extensions.Primitives.StringValues([id.ToString("D"), id.ToString("D")]);
        Assert.IsType<Microsoft.AspNetCore.Mvc.BadRequestResult>((await controller.Cancel(id, default)).Result);
    }
}
