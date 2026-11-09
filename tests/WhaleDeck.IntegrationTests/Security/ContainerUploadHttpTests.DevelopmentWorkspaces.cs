using System.Net;
using System.Net.Http.Json;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Development;
using WhaleDeck.Application.Models;
using WhaleDeck.Infrastructure.Persistence;

namespace WhaleDeck.IntegrationTests.Security;

public sealed partial class ContainerUploadHttpTests
{
    internal static async Task RegisterDevelopmentWorkspacesInPostgres(DbContextOptions<PlatformDbContext> options)
    {
        var directory = new WorkspaceReportHttpFixture();
        var storage = new WorkspaceStorageFaultFixture(options);
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
        Task<Fixture> StartFixture() => Fixture.Start(services =>
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
            services.AddScoped<WorkspaceStorageAllocation>();
            services.AddSingleton<IWorkspaceStorageGateway>(storage);
        });
        await using var fixture = await StartFixture();
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

        var storagePlanPath = $"{path}/{owned.Id:D}/storage-plan";
        var applyPath = $"{path}/{owned.Id:D}/storage-allocation";
        var plan = (await developer.GetFromJsonAsync<WorkspaceStoragePlan>(storagePlanPath))!;
        Assert.Equal(owned.Id, plan.WorkspaceId);
        Assert.Equal(owned.EnvironmentSha256, plan.EnvironmentSha256);
        Assert.Equal(owned.StorageRootId, plan.StorageRootId);
        Assert.Equal(owned.Budget.DiskMiB, plan.DiskMiB);
        using (var foreignPlan = await admin.GetAsync(storagePlanPath))
            Assert.Equal(HttpStatusCode.NotFound, foreignPlan.StatusCode);
        using (var foreignApply = await Allocate(admin, adminToken, new WorkspaceStorageApplyCommand(plan.PlanSha256)))
            Assert.Equal(HttpStatusCode.NotFound, foreignApply.StatusCode);
        using (var tamper = await Allocate(developer, developerToken, new WorkspaceStorageApplyCommand(new string('0', 64))))
            Assert.Equal(HttpStatusCode.Conflict, tamper.StatusCode);
        using (var pathInjection = await Allocate(developer, developerToken, new { plan.PlanSha256, hostPath = "/data" }))
            Assert.Equal(HttpStatusCode.BadRequest, pathInjection.StatusCode);
        using (var csrf = await Allocate(developer, null, new WorkspaceStorageApplyCommand(plan.PlanSha256)))
            Assert.Equal(HttpStatusCode.BadRequest, csrf.StatusCode);
        directory.LiveMembership = false;
        using (var revoked = await Allocate(developer, developerToken, new WorkspaceStorageApplyCommand(plan.PlanSha256)))
            Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
        directory.LiveMembership = true;
        Assert.Equal(0, storage.Calls);

        var apply = new WorkspaceStorageApplyCommand(plan.PlanSha256);
        using (var lostReply = await Allocate(developer, developerToken, apply))
            Assert.Equal(HttpStatusCode.ServiceUnavailable, lostReply.StatusCode);
        var allocating = (await developer.GetFromJsonAsync<WorkspaceDefinition>($"{path}/{owned.Id:D}"))!;
        Assert.Equal(WorkspaceStorageAllocation.Allocating, allocating.State);
        Assert.Equal(2, allocating.Version);
        Assert.Equal(1, storage.Calls);
        directory.LiveMembership = false;
        using (var revokedRetry = await Allocate(developer, developerToken, apply))
            Assert.Equal(HttpStatusCode.Forbidden, revokedRetry.StatusCode);
        Assert.Equal(1, storage.Calls);
        directory.LiveMembership = true;

        await using var recovery = await StartFixture();
        using var recoveredDeveloper = await recovery.Client("developer-subject", false);
        var recoveredToken = await Csrf(recoveredDeveloper);
        var retries = await Task.WhenAll(Allocate(recoveredDeveloper, recoveredToken, apply),
            Allocate(recoveredDeveloper, recoveredToken, apply));
        foreach (var response in retries)
        {
            using (response)
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var allocated = (await response.Content.ReadFromJsonAsync<WorkspaceDefinition>())!;
                Assert.Equal(WorkspaceStorageAllocation.Allocated, allocated.State);
                Assert.Equal(3, allocated.Version);
                Assert.Equal(owned.EnvironmentSha256, allocated.EnvironmentSha256);
                Assert.Equal(owned.Budget, allocated.Budget);
            }
        }
        var calls = storage.Calls;
        Assert.InRange(calls, 2, 3);
        using (var replay = await Allocate(developer, developerToken, apply))
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(calls, storage.Calls);
        Assert.Equal(plan, await developer.GetFromJsonAsync<WorkspaceStoragePlan>(storagePlanPath));
        await using var verification = new PlatformDbContext(options);
        var stored = await verification.DevelopmentWorkspaces.AsNoTracking().SingleAsync(row => row.Id == owned.Id);
        Assert.Equal(plan.PlanSha256, stored.StoragePlanSha256);
        Assert.NotNull(stored.StorageAllocatedAtUtc);
        Assert.True(stored.CreatedAtUtc <= stored.StorageAllocationStartedAtUtc);
        Assert.True(stored.StorageAllocationStartedAtUtc <= stored.StorageAllocatedAtUtc);
        var audit = await verification.AuditEvents.AsNoTracking()
            .Where(row => row.TargetId == owned.Id.ToString("D") && row.Action.StartsWith("development.workspace.storage."))
            .OrderBy(row => row.Action).ToArrayAsync();
        Assert.Equal(["development.workspace.storage.begin", "development.workspace.storage.complete"], audit.Select(row => row.Action));
        Assert.All(audit, row => Assert.Equal("developer-subject", row.ActorSubject));
        Console.WriteLine("INBOX_PHASE=development-workspace-allocation-real-http-postgres-lost-reply-concurrency");

        async Task<HttpResponseMessage> Register(HttpClient client, string? token, WorkspaceRegistrationCommand body, Guid requestKey)
        {
            using var request = Write(client, HttpMethod.Post, path, token, JsonContent.Create(body), requestKey.ToString("D"));
            return await client.SendAsync(request);
        }

        async Task<HttpResponseMessage> Allocate(HttpClient client, string? token, object body)
        {
            using var request = Write(client, HttpMethod.Post, applyPath, token, JsonContent.Create(body));
            return await client.SendAsync(request);
        }
    }

    private sealed class WorkspaceStorageFaultFixture(DbContextOptions<PlatformDbContext> options) : IWorkspaceStorageGateway
    {
        private int _calls;
        private WorkspaceStoragePlan? _published;
        public int Calls => Volatile.Read(ref _calls);

        public async Task AllocateAsync(TerminalActor actor, WorkspaceDefinition workspace, CancellationToken token)
        {
            await using var observer = new PlatformDbContext(options);
            var row = await observer.DevelopmentWorkspaces.AsNoTracking().SingleAsync(record => record.Id == workspace.Id, token);
            var plan = WorkspaceStorageAllocation.BuildPlan(workspace);
            Assert.Equal(WorkspaceStorageAllocation.Allocating, workspace.State);
            Assert.Contains(row.State, new[] { WorkspaceStorageAllocation.Allocating, WorkspaceStorageAllocation.Allocated });
            Assert.Equal(plan.PlanSha256, row.StoragePlanSha256);
            Assert.Equal(actor.Subject, row.OwnerSubject);
            Interlocked.Increment(ref _calls);
            var previous = Interlocked.CompareExchange(ref _published, plan, null);
            if (previous is null)
                throw new RpcException(new Status(StatusCode.Unavailable, "fixture lost reply after publish"));
            Assert.Equal(previous, plan);
        }
    }
}
