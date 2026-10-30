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

public sealed class ContainerPermissionModelTests
{
    [Fact]
    public void CopyIntentHasRestrictedDoubleJobsConcurrencyConstraintsAndPermanentHistory()
    {
        using var db = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql("Host=127.0.0.1;Database=offline_model_only").Options);
        Assert.False(db.Database.HasPendingModelChanges());
        var type = db.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>().Model.FindEntityType(typeof(ContainerPermissionIntent))!;
        Assert.True(type.FindProperty(nameof(ContainerPermissionIntent.Version))!.IsConcurrencyToken);
        Assert.Equal(2, type.GetForeignKeys().Count()); Assert.All(type.GetForeignKeys(), fk => Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior));
        Assert.Contains(type.GetIndexes(), i => i.IsUnique && i.GetFilter() == "publish_barrier");
        Assert.Equal(13, type.GetCheckConstraints().Count());
        Assert.DoesNotContain(type.GetProperties(), p => p.Name.Contains("Password", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("Content", StringComparison.OrdinalIgnoreCase));
        var assembly = db.GetService<IMigrationsAssembly>(); var entry = Assert.Single(assembly.Migrations, x => x.Key.EndsWith("_ContainerPermissionIntents", StringComparison.Ordinal));
        var migration = assembly.CreateMigration(entry.Value, db.Database.ProviderName!);
        Assert.DoesNotContain(migration.UpOperations, x => x is DropTableOperation or DropColumnOperation or DeleteDataOperation);
        Assert.Equal("container_permission_intents", Assert.Single(migration.UpOperations.OfType<CreateTableOperation>()).Name);
        var sql = string.Join('\n', migration.UpOperations.OfType<SqlOperation>().Select(x => x.Sql));
        foreach (var marker in new[] { "container_permission_request_immutable", "container_permission_plan_immutable", "container_permission_execution_immutable", "container_permission_started_immutable",
            "container_permission_terminal_immutable", "container_permission_permanent_intent", "whaledeck_permission_host_path_fence", "whaledeck_permission_upload_fence", "whaledeck_permission_directory_fence", "pg_advisory_xact_lock(41307341)" })
            Assert.Contains(marker, sql, StringComparison.Ordinal);
        Assert.DoesNotContain("SECURITY DEFINER", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("container_permission_downgrade_requires_empty_table", Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql, StringComparison.Ordinal);
    }
    [Theory][InlineData(typeof(ContainerPermissionsController))][InlineData(typeof(ContainerPermissionRecoveryController))]
    public void AllCopyHttpSurfacesAreAdministratorOnlyAndNeverCached(Type controller)
    {
        Assert.Contains(controller.GetCustomAttributes(typeof(AuthorizeAttribute), true), a => ((AuthorizeAttribute)a).Policy == "Administrator");
        Assert.Contains(controller.GetCustomAttributes(typeof(ResponseCacheAttribute), true), a => ((ResponseCacheAttribute)a).NoStore);
    }
    [Fact]
    public async Task MissingDuplicateNonCanonicalAndWrongWriteKeysFailBeforeIdentityOrBusinessCalls()
    {
        var controller = new ContainerPermissionsController(null!, null!) { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        var recovery = new ContainerPermissionRecoveryController(null!, null!) { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        var id = Guid.Parse("a26533e0-029e-48d8-88b8-8273058b370d"); const string cid = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        foreach (var key in new[] { "", Guid.Empty.ToString("D"), id.ToString("D").ToUpperInvariant(), id.ToString("N") })
        {
            controller.Request.Headers["Idempotency-Key"] = key;
            var result = Assert.IsType<BadRequestObjectResult>((await controller.Queue(cid, null!, default)).Result);
            var problem = Assert.IsType<ProblemDetails>(result.Value);
            Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
            Assert.Equal("CONTAINER_PERMISSION_IDEMPOTENCY_KEY_REQUIRED", problem.Extensions["code"]);
            Assert.Equal(controller.HttpContext.TraceIdentifier, problem.Extensions["traceId"]);
            Assert.IsType<BadRequestObjectResult>((await controller.Confirm(cid, id, null!, default)).Result);
            Assert.IsType<BadRequestObjectResult>((await controller.Cancel(cid, id, default)).Result);
        }
        controller.Request.Headers["Idempotency-Key"] = new StringValues([id.ToString("D"), id.ToString("D")]);
        Assert.IsType<BadRequestObjectResult>((await controller.Queue(cid, null!, default)).Result);
        Assert.IsType<BadRequestObjectResult>((await controller.Cancel(cid, id, default)).Result);
        Assert.IsType<BadRequestObjectResult>(await recovery.Acknowledge(id, null!, default));
    }
}
