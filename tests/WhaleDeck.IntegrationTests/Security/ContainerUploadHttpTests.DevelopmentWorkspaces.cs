using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Development;
using WhaleDeck.Infrastructure.Persistence;

namespace WhaleDeck.IntegrationTests.Security;

public sealed partial class ContainerUploadHttpTests
{
    internal static async Task RegisterDevelopmentWorkspacesInPostgres(DbContextOptions<PlatformDbContext> options)
    {
        var directory = new WorkspaceReportHttpFixture();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DevelopmentWorkspaces:StorageRootIds:0"] = "qa-workspace-root",
            ["DevelopmentWorkspaces:CreateGroupIds:0"] = directory.Group.ToString("D"),
            ["DevelopmentWorkspaces:Capacity:CpuMillis"] = "2000",
            ["DevelopmentWorkspaces:Capacity:MemoryMiB"] = "2048",
            ["DevelopmentWorkspaces:Capacity:DiskMiB"] = "4096",
            ["DevelopmentWorkspaces:Runtimes:dotnet:Version"] = "10.0.402",
            ["DevelopmentWorkspaces:Runtimes:dotnet:Image"] = "mcr.microsoft.com/dotnet/sdk@sha256:0d99ebf1ccad15037b5c98c9fe8979d6b8dbac23812feae2277a0b277b5c5ffa",
            ["DevelopmentWorkspaces:Runtimes:node:Version"] = "24.15.0",
            ["DevelopmentWorkspaces:Runtimes:node:Image"] = "docker.io/library/node@sha256:152aceace5c03e2597988763165ee33e3fd3633636db0fc983cd2e126b02cfde"
        }).Build();
        var policy = configuration.GetSection("DevelopmentWorkspaces").Get<WorkspaceRegistrationPolicy>()!;
        DevelopmentWorkspaces.ValidatePolicy(policy);
        await using var fixture = await Fixture.Start(services =>
        {
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton(policy);
            services.AddSingleton<IPrivilegedIdentityDirectory>(directory);
            services.AddScoped(_ => new PlatformDbContext(options));
            services.AddScoped<IWorkspaceReportAccess, WorkspaceReportAccessRepository>();
            services.AddScoped<WorkspaceReportAuthorization>();
            services.AddScoped<WorkspaceReportGrantManagement>();
            services.AddScoped<IDevelopmentWorkspaceRegistry, DevelopmentWorkspaceRegistry>();
            services.AddScoped<DevelopmentWorkspaces>();
        });
        using var admin = await fixture.Client("admin-subject", true);
        using var developer = await fixture.Client("developer-subject", false);
        using var stranger = await fixture.Client("ordinary-subject", false);
        const string path = "/api/v1/development/workspaces";
        var adminToken = await Csrf(admin);
        var developerToken = await Csrf(developer);
        var strangerToken = await Csrf(stranger);
        var manifest = await developer.GetFromJsonAsync<WorkspaceCatalog>(path + "/catalog");
        Assert.Equal(2, manifest!.Runtimes.Length);
        var command = new WorkspaceRegistrationCommand("alpha", "qa-workspace-root", ["node", "dotnet"], new(1000, 1024, 2048));
        using (var denied = await Register(stranger, strangerToken, command, Guid.NewGuid()))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var unsafeRoot = await Register(admin, adminToken, command with { StorageRootId = "unapproved" }, Guid.NewGuid()))
            Assert.Equal(HttpStatusCode.BadRequest, unsafeRoot.StatusCode);
        using (var unpinned = await Register(admin, adminToken, command with { Runtimes = ["node-latest"] }, Guid.NewGuid()))
            Assert.Equal(HttpStatusCode.BadRequest, unpinned.StatusCode);
        using (var csrf = await Register(admin, null, command, Guid.NewGuid()))
            Assert.Equal(HttpStatusCode.BadRequest, csrf.StatusCode);

        var key = Guid.NewGuid();
        var concurrent = await Task.WhenAll(Register(admin, adminToken, command, key), Register(admin, adminToken, command, key));
        using var first = concurrent[0];
        using var second = concurrent[1];
        Assert.Contains(concurrent, response => response.StatusCode == HttpStatusCode.Created);
        Assert.Contains(concurrent, response => response.StatusCode == HttpStatusCode.OK);
        var original = (await first.Content.ReadFromJsonAsync<WorkspaceRegistrationReceipt>())!;
        var duplicate = (await second.Content.ReadFromJsonAsync<WorkspaceRegistrationReceipt>())!;
        Assert.Equal(original.Workspace.Id, duplicate.Workspace.Id);
        Assert.Equal(original.Workspace.CreatedAtUtc, duplicate.Workspace.CreatedAtUtc);
        Assert.Equal(original.Workspace.EnvironmentSha256, duplicate.Workspace.EnvironmentSha256);
        Assert.Equal("Registered", original.Workspace.State);
        Assert.Collection(original.Workspace.Environment.Runtimes,
            runtime => Assert.Equal("dotnet", runtime.Key), runtime => Assert.Equal("node", runtime.Key));
        using (var keyConflict = await Register(admin, adminToken, command with { Slug = "different" }, key))
            Assert.Equal(HttpStatusCode.Conflict, keyConflict.StatusCode);
        using (var slugConflict = await Register(developer, developerToken, command, Guid.NewGuid()))
            Assert.Equal(HttpStatusCode.Conflict, slugConflict.StatusCode);

        var contenders = await Task.WhenAll(
            Register(developer, developerToken, command with { Slug = "beta" }, Guid.NewGuid()),
            Register(developer, developerToken, command with { Slug = "gamma" }, Guid.NewGuid()));
        using var created = Assert.Single(contenders, response => response.StatusCode == HttpStatusCode.Created);
        using var limited = Assert.Single(contenders, response => response.StatusCode == HttpStatusCode.Conflict);
        var owned = (await created.Content.ReadFromJsonAsync<WorkspaceRegistrationReceipt>())!.Workspace;
        Assert.Equal("developer-subject", owned.OwnerSubject);
        using (var capacity = await Register(admin, adminToken, command with { Slug = "over-capacity" }, Guid.NewGuid()))
            Assert.Equal(HttpStatusCode.Conflict, capacity.StatusCode);

        var page = (await admin.GetFromJsonAsync<WorkspacePage>(path + "?take=1"))!;
        Assert.Equal("alpha", Assert.Single(page.Items).Slug);
        Assert.Equal("alpha", page.NextAfter);
        var next = (await admin.GetFromJsonAsync<WorkspacePage>(path + "?take=1&after=alpha"))!;
        Assert.Equal(owned.Slug, Assert.Single(next.Items).Slug);
        Assert.Null(next.NextAfter);
        var mine = (await developer.GetFromJsonAsync<WorkspacePage>(path))!;
        Assert.Equal(owned.Id, Assert.Single(mine.Items).Id);
        using (var foreign = await developer.GetAsync($"{path}/{original.Workspace.Id:D}"))
            Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);

        var grantPath = $"{path}/{owned.Id:D}/report-access/{directory.Group:D}";
        using var put = Write(admin, HttpMethod.Put, grantPath, adminToken, JsonContent.Create(new { enabled = true, expectedVersion = 0 }));
        using var grant = await admin.SendAsync(put);
        Assert.Equal(HttpStatusCode.OK, grant.StatusCode);

        async Task<HttpResponseMessage> Register(HttpClient client, string? token, WorkspaceRegistrationCommand body, Guid requestKey)
        {
            using var request = Write(client, HttpMethod.Post, path, token, JsonContent.Create(body), requestKey.ToString("D"));
            return await client.SendAsync(request);
        }
    }
}
