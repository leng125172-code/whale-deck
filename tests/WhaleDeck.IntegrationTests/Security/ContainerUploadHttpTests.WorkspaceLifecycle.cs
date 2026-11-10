using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Npgsql;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Development;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Persistence;

namespace WhaleDeck.IntegrationTests.Security;

public sealed partial class ContainerUploadHttpTests
{
    private static readonly JsonSerializerOptions LifecycleJson = new(JsonSerializerDefaults.Web);
    internal static async Task IdleWorkspaceLifecycleInPostgres(DbContextOptions<PlatformDbContext> options)
    {
        var directory = new LifecycleDirectory();
        using var key = new LifecycleKey();
        var clock = new WorkspaceRunClock();
        var host = new LifecycleHost();
        var source = new LifecycleSource();
        var reports = new WorkspaceRunAgentFixture();
        await using var inspection = new PlatformDbContext(options);
        var original = await inspection.DevelopmentWorkspaces.AsNoTracking().FirstAsync(row => row.State == WorkspaceStorageAllocation.Allocated);
        var policy = new WorkspaceRegistrationPolicy
        {
            StorageRootIds = [original.StorageRootId], CreateGroupIds = [directory.Group],
            Runtimes = new(StringComparer.Ordinal) { ["node"] = new("22", "registry.example/node@sha256:" + new string('a', 64)) },
            GitRepositories = new(StringComparer.Ordinal) { ["lifecycle-repo"] = new(["developer-subject"]) }
        };
        await using var server = await Fixture.Start(services =>
        {
            services.AddSingleton<TimeProvider>(clock);
            services.AddSingleton(policy);
            services.AddSingleton<IPrivilegedIdentityDirectory>(directory);
            services.AddScoped(_ => new PlatformDbContext(options));
            services.AddScoped<IDevelopmentWorkspaceRegistry, DevelopmentWorkspaceRegistry>();
            services.AddScoped<IWorkspaceReportAccess, WorkspaceReportAccessRepository>();
            services.AddScoped<WorkspaceReportAuthorization>();
            services.AddScoped<DevelopmentWorkspaces>();
            services.AddScoped<WorkspaceStorageAllocation>();
            services.AddSingleton<IWorkspaceStorageGateway>(new LifecycleStorage());
            services.AddScoped<WorkspaceRuns>();
            services.AddScoped<IWorkspaceRunStore, WorkspaceRunRepository>();
            services.AddScoped<IWorkspaceRunReportWork, WorkspaceRunRepository>();
            services.AddScoped<IWorkspaceCollectedReportStore, WorkspaceRunReportRepository>();
            services.AddSingleton<IWorkspaceTestArtifactGateway>(reports);
            services.AddScoped<WorkspaceRunReportPipeline>();
            services.AddScoped<WorkspaceSources>();
            services.AddScoped<IWorkspaceSourceStore, WorkspaceSourceRepository>();
            services.AddSingleton(_ => new WorkspaceSourceProtector(key.Configuration));
            services.AddSingleton<IWorkspaceSourceGateway>(source);
            services.AddScoped<WorkspaceSourceDispatcher>();
            services.AddScoped<IWorkspaceLifecycleStore, WorkspaceLifecycleRepository>();
            services.AddScoped<WorkspaceLifecycle>();
            services.AddScoped<WorkspaceLifecycleDispatcher>();
            services.AddSingleton<IWorkspaceLifecycleGateway>(host);
        });
        using var owner = await server.Client("developer-subject", false, TimeSpan.FromHours(1));
        using var admin = await server.Client("admin-subject", true, TimeSpan.FromHours(1));
        using var recovery = await server.Client("recovery-admin", true, TimeSpan.FromHours(1));
        using var outsider = await server.Client("outsider", false, TimeSpan.FromHours(1));
        var ownerCsrf = await Csrf(owner); var adminCsrf = await Csrf(admin); var recoveryCsrf = await Csrf(recovery);
        using var registration = await Send(owner, HttpMethod.Post, "/api/v1/development/workspaces", ownerCsrf,
            new WorkspaceRegistrationCommand("lifecycle-" + Guid.NewGuid().ToString("N")[..16], original.StorageRootId,
                ["node"], new(100, 128, 256)), Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        var registered = (await registration.Content.ReadFromJsonAsync<WorkspaceRegistrationReceipt>())!.Workspace;
        var path = $"/api/v1/development/workspaces/{registered.Id:D}";
        var allocation = (await owner.GetFromJsonAsync<WorkspaceStoragePlan>(path + "/storage-plan"))!;
        using (var response = await Send(owner, HttpMethod.Post, path + "/storage-allocation", ownerCsrf,
                   new WorkspaceStorageApplyCommand(allocation.PlanSha256)))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var baseline = (await owner.GetFromJsonAsync<WorkspaceDefinition>(path))!;
        Assert.Equal("Active", baseline.LifecycleState);

        var runKey = Guid.NewGuid();
        var input = new WorkspaceRunInput("node", "test");
        using var queued = await Send(owner, HttpMethod.Post, path + "/runs", ownerCsrf, input, runKey);
        var run = (await queued.Content.ReadFromJsonAsync<WorkspaceRunView>())!;
        using (var busy = await admin.GetAsync(path + "/lifecycle/plan?action=stop"))
            Assert.Equal(HttpStatusCode.Conflict, busy.StatusCode);
        using (var cancel = await Send(owner, HttpMethod.Post, path + $"/runs/{run.RunId:D}/cancel", ownerCsrf))
            Assert.Equal(HttpStatusCode.Accepted, cancel.StatusCode);
        await Tick<WorkspaceRunReportPipeline>(pipeline => pipeline.TickAsync(CancellationToken.None));

        var importId = Guid.NewGuid();
        var archive = new WorkspaceSourceInput("tar", 10, new string('a', 64));
        var sourcePath = path + $"/source-imports/{importId:D}";
        using (var begin = await Send(owner, HttpMethod.Put, sourcePath, ownerCsrf, archive))
            Assert.True(begin.IsSuccessStatusCode);
        using (var busy = await admin.GetAsync(path + "/lifecycle/plan?action=stop"))
            Assert.Equal(HttpStatusCode.Conflict, busy.StatusCode);
        using (var cancel = await Send(owner, HttpMethod.Post, sourcePath + "/cancel", ownerCsrf))
            Assert.Equal(HttpStatusCode.Accepted, cancel.StatusCode);
        await Tick<WorkspaceSourceDispatcher>(dispatcher => dispatcher.TickAsync(CancellationToken.None));
        Assert.Equal(1, source.Aborts);

        var gitId = Guid.NewGuid();
        var gitPath = path + $"/source-imports/{gitId:D}";
        using (var begin = await Send(owner, HttpMethod.Put, gitPath + "/git", ownerCsrf, new WorkspaceGitInput("lifecycle-repo", "HEAD")))
            Assert.Equal(HttpStatusCode.Accepted, begin.StatusCode);
        using (var cancel = await Send(owner, HttpMethod.Post, gitPath + "/cancel", ownerCsrf))
            Assert.Equal(HttpStatusCode.Accepted, cancel.StatusCode);
        using (var draining = await admin.GetAsync(path + "/lifecycle/plan?action=stop"))
        {
            Assert.Equal(HttpStatusCode.Conflict, draining.StatusCode);
            Assert.Equal("WORKSPACE_LIFECYCLE_GIT_DRAINING", (await draining.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }
        clock.Advance(181);

        using (var denied = await outsider.GetAsync(path + "/lifecycle/plan?action=stop"))
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        var stop = (await admin.GetFromJsonAsync<WorkspaceLifecyclePlan>(path + "/lifecycle/plan?action=stop"))!;
        using (var denied = await Send(admin, HttpMethod.Post, path + "/lifecycle", null, new WorkspaceLifecycleCommand("stop", stop.PlanSha256), Guid.NewGuid()))
            Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        var stopKey = Guid.NewGuid();
        using var admitted = await Send(admin, HttpMethod.Post, path + "/lifecycle", adminCsrf,
            new WorkspaceLifecycleCommand("stop", stop.PlanSha256), stopKey);
        Assert.Equal(HttpStatusCode.Accepted, admitted.StatusCode);
        var intent = (await admitted.Content.ReadFromJsonAsync<WorkspaceLifecycleView>())!;
        Assert.Equal("Stopping", (await owner.GetFromJsonAsync<WorkspaceDefinition>(path))!.LifecycleState);
        using (var duplicate = await Send(admin, HttpMethod.Post, path + "/lifecycle", adminCsrf,
                   new WorkspaceLifecycleCommand("stop", stop.PlanSha256), stopKey))
            Assert.Equal(intent.OperationId, (await duplicate.Content.ReadFromJsonAsync<WorkspaceLifecycleView>())!.OperationId);
        using (var newRun = await Send(owner, HttpMethod.Post, path + "/runs", ownerCsrf, input, Guid.NewGuid()))
            Assert.Equal(HttpStatusCode.Conflict, newRun.StatusCode);
        using (var newSource = await Send(owner, HttpMethod.Put, path + $"/source-imports/{Guid.NewGuid():D}", ownerCsrf, archive))
            Assert.Equal(HttpStatusCode.Conflict, newSource.StatusCode);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new JobRepository(inspection)
            .RequestCancellationAsync(intent.OperationId, "admin-subject", CancellationToken.None));

        host.LoseReply = true;
        await Tick<WorkspaceLifecycleDispatcher>(dispatcher => dispatcher.TickAsync(CancellationToken.None));
        Assert.Equal(1, host.Effects);
        Assert.Equal("Stopping", (await owner.GetFromJsonAsync<WorkspaceDefinition>(path))!.LifecycleState);
        clock.Advance(6);
        host.AfterApply = () => directory.FirstAdministrator = false;
        await Tick<WorkspaceLifecycleDispatcher>(dispatcher => dispatcher.TickAsync(CancellationToken.None));
        var unauthorized = (await recovery.GetFromJsonAsync<WorkspaceLifecycleView>(path + $"/lifecycle/{intent.OperationId:D}"))!;
        Assert.Equal("WORKSPACE_LIFECYCLE_AUTHORIZATION_REQUIRED", unauthorized.Code);
        Assert.Null(unauthorized.CompletedAtUtc);
        host.AfterApply = null;
        using (var authorized = await Send(recovery, HttpMethod.Post, path + $"/lifecycle/{intent.OperationId:D}/authorize", recoveryCsrf))
            Assert.Equal(HttpStatusCode.Accepted, authorized.StatusCode);
        await Tick<WorkspaceLifecycleDispatcher>(dispatcher => dispatcher.TickAsync(CancellationToken.None));
        var stopped = (await owner.GetFromJsonAsync<WorkspaceDefinition>(path))!;
        Assert.Equal("Stopped", stopped.LifecycleState);
        Assert.Equal(2, stopped.LifecycleVersion);
        Assert.Equal(1, host.Effects);
        Assert.Equal("recovery-admin", host.LastOperator);
        Assert.Equal("developer-subject", host.LastOwner);
        using (var replay = await Send(owner, HttpMethod.Post, path + "/runs", ownerCsrf, input, runKey))
            Assert.Equal(run.RunId, (await replay.Content.ReadFromJsonAsync<WorkspaceRunView>())!.RunId);
        using (var replay = await Send(owner, HttpMethod.Put, sourcePath, ownerCsrf, archive))
            Assert.True(replay.IsSuccessStatusCode);
        using (var preflight = await Send(owner, HttpMethod.Post, path + "/runner-preflight", ownerCsrf))
            Assert.Equal(HttpStatusCode.Conflict, preflight.StatusCode);

        // Database admission protects persisted stops even from an older producer
        // that has no lifecycle-aware application check.
        var oldRun = await inspection.WorkspaceRunIntents.AsNoTracking().SingleAsync(row => row.Id == run.RunId);
        var rawId = Guid.NewGuid();
        var blocked = await Assert.ThrowsAsync<PostgresException>(() => inspection.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO development_workspace_runs(id, workspace_id, runtime, task, login_binding, snapshot_json, available_at_utc)
            VALUES ({rawId}, {registered.Id}, 'node', 'test', {oldRun.LoginBinding}, CAST({oldRun.SnapshotJson} AS jsonb), {clock.GetUtcNow()})
            """));
        Assert.Equal(PostgresErrorCodes.CheckViolation, blocked.SqlState);
        Assert.Equal("ck_workspace_lifecycle_admission", blocked.ConstraintName);

        inspection.WorkspaceSourceIntents.Add(new()
        {
            Id = Guid.NewGuid(), WorkspaceId = registered.Id, OwnerSubject = "developer-subject", LoginBinding = oldRun.LoginBinding,
            WorkspaceJson = JsonSerializer.Serialize(baseline, LifecycleJson),
            Format = "tar", ArchiveBytes = 10, ArchiveSha256 = new string('a', 64), State = WorkspaceSources.Uploading,
            AvailableAtUtc = clock.GetUtcNow(), ExpiresAtUtc = clock.GetUtcNow().AddHours(1)
        });
        var blockedSource = await Assert.ThrowsAsync<DbUpdateException>(() => inspection.SaveChangesAsync());
        Assert.Equal("ck_workspace_lifecycle_admission", Assert.IsType<PostgresException>(blockedSource.InnerException).ConstraintName);
        inspection.ChangeTracker.Clear();

        directory.OwnerDeleted = true;
        var resume = (await recovery.GetFromJsonAsync<WorkspaceLifecyclePlan>(path + "/lifecycle/plan?action=resume"))!;
        using (var response = await Send(recovery, HttpMethod.Post, path + "/lifecycle", recoveryCsrf,
                   new WorkspaceLifecycleCommand("resume", resume.PlanSha256), Guid.NewGuid()))
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        await Tick<WorkspaceLifecycleDispatcher>(dispatcher => dispatcher.TickAsync(CancellationToken.None));
        var active = (await recovery.GetFromJsonAsync<WorkspaceDefinition>(path))!;
        Assert.Equal("Active", active.LifecycleState);
        Assert.Equal(3, active.LifecycleVersion);
        Assert.Equal(baseline.OwnerSubject, active.OwnerSubject);
        Assert.Equal(baseline.EnvironmentSha256, active.EnvironmentSha256);
        Assert.Equal(baseline.Budget, active.Budget);
        await using (var scope = server.Scope())
        {
            var queue = scope.ServiceProvider.GetRequiredService<IWorkspaceRunStore>();
            await Assert.ThrowsAsync<InvalidOperationException>(() => queue.EnqueueAsync(baseline,
                new("developer-subject", null, [], oldRun.LoginBinding, "stale", null), Guid.NewGuid(), input, CancellationToken.None));
        }
        // A producer already inside its shared fence must commit before stop's
        // exclusive admission proceeds; the new pending run then invalidates stop.
        directory.OwnerDeleted = false;
        var lateId = Guid.NewGuid();
        await using (var producer = new PlatformDbContext(options))
        await using (var transaction = await producer.Database.BeginTransactionAsync())
        {
            await producer.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM development_workspaces WHERE id = {registered.Id} FOR SHARE");
            var nextPlan = (await recovery.GetFromJsonAsync<WorkspaceLifecyclePlan>(path + "/lifecycle/plan?action=stop"))!;
            var waitingStop = Send(recovery, HttpMethod.Post, path + "/lifecycle", recoveryCsrf,
                new WorkspaceLifecycleCommand("stop", nextPlan.PlanSha256), Guid.NewGuid());
            var observed = false;
            for (var attempt = 0; attempt < 100; attempt++)
            {
                var locks = await inspection.Database.SqlQueryRaw<int>("""
                    SELECT count(*)::integer AS "Value" FROM pg_stat_activity
                    WHERE datname = current_database() AND pid <> pg_backend_pid() AND wait_event_type = 'Lock'
                        AND query LIKE '%development_workspaces%FOR UPDATE%'
                    """).SingleAsync();
                if (locks > 0) { observed = true; break; }
                await Task.Delay(20);
            }
            Assert.True(observed, "Stop did not wait for the workspace producer fence");
            producer.OperationJobs.Add(new OperationJob
            {
                Id = lateId, JobType = WorkspaceRuns.JobType, ActorSubject = baseline.OwnerSubject,
                ResourceId = registered.Id, IdempotencyKey = lateId.ToString("D"), CreatedAtUtc = clock.GetUtcNow()
            });
            producer.WorkspaceRunIntents.Add(new WorkspaceRunIntent
            {
                Id = lateId, WorkspaceId = registered.Id, Runtime = "node", Task = "test", LoginBinding = oldRun.LoginBinding,
                SnapshotJson = JsonSerializer.Serialize(new { Workspace = active,
                    Command = WorkspaceRuns.BindCommand(lateId, active, input, 14) }, LifecycleJson),
                AvailableAtUtc = clock.GetUtcNow()
            });
            await producer.SaveChangesAsync();
            await transaction.CommitAsync();
            using var rejectedStop = await waitingStop.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(HttpStatusCode.Conflict, rejectedStop.StatusCode);
        }
        using (var canceledLate = await Send(owner, HttpMethod.Post, path + $"/runs/{lateId:D}/cancel", ownerCsrf))
            Assert.Equal(HttpStatusCode.Accepted, canceledLate.StatusCode);
        await Tick<WorkspaceRunReportPipeline>(pipeline => pipeline.TickAsync(CancellationToken.None));
        Assert.Equal(2, host.Effects);
        Assert.Equal(2, await inspection.WorkspaceLifecycleIntents.CountAsync(row => row.WorkspaceId == registered.Id && row.FinishedAtUtc != null));
        Assert.True(await inspection.AuditEvents.AnyAsync(row => row.JobId == intent.OperationId &&
            row.Action == "development.workspace.lifecycle.reauthorize" && row.ActorSubject == "recovery-admin"));
        Assert.Equal("admin-subject", (await inspection.OperationJobs.AsNoTracking().SingleAsync(row => row.Id == intent.OperationId)).ActorSubject);
        Console.WriteLine("INBOX_PHASE=workspace-idle-lifecycle-real-http-postgres-fence-replay-lost-reply-admin-recovery");

        async Task Tick<T>(Func<T, Task> action) where T : notnull
        {
            await using var scope = server.Scope();
            await action(scope.ServiceProvider.GetRequiredService<T>());
        }
        static async Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string url, string? csrf,
            object? body = null, Guid? key = null)
        {
            using var request = Write(client, method, url, csrf, body is null ? null : JsonContent.Create(body), key?.ToString("D"));
            return await client.SendAsync(request);
        }
    }

    private sealed class LifecycleKey : IDisposable
    {
        private readonly DirectoryInfo _directory = System.IO.Directory.CreateTempSubdirectory("wd-lifecycle-key-");
        public IConfiguration Configuration { get; }
        public LifecycleKey()
        {
            var path = Path.Combine(_directory.FullName, "key");
            File.WriteAllBytes(path, RandomNumberGenerator.GetBytes(32));
            Configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                { ["Agent:CapabilityKeyFile"] = path }).Build();
        }
        public void Dispose() => _directory.Delete(recursive: true);
    }
    private sealed class LifecycleStorage : IWorkspaceStorageGateway
    {
        public Task AllocateAsync(TerminalActor actor, WorkspaceDefinition workspace, CancellationToken token) => Task.CompletedTask;
        public Task AttestAsync(TerminalActor actor, WorkspaceDefinition workspace, CancellationToken token) => Task.CompletedTask;
    }
    private sealed class LifecycleDirectory : IPrivilegedIdentityDirectory
    {
        public Guid Group { get; } = Guid.NewGuid();
        public bool OwnerDeleted { get; set; }
        public bool FirstAdministrator { get; set; } = true;
        public Task<CurrentUserDto> ResolveForPrivilegedActionAsync(string subject, string? name, CancellationToken token)
        {
            if (subject == "developer-subject" && OwnerDeleted) throw new UnauthorizedAccessException();
            return Task.FromResult(new CurrentUserDto(subject, name, subject == "developer-subject" ? [Group.ToString("D")] : [],
                subject == "recovery-admin" || subject == "admin-subject" && FirstAdministrator, DateTimeOffset.UtcNow.AddHours(1)));
        }
    }
    private sealed class LifecycleHost : IWorkspaceLifecycleGateway
    {
        private long _epoch = 1;
        private string _state = "Active";
        private Guid _operation;
        public int Effects { get; private set; }
        public bool LoseReply { get; set; }
        public Action? AfterApply { get; set; }
        public string? LastOperator { get; private set; }
        public string? LastOwner { get; private set; }
        public Task<WorkspaceLifecycleHostProof> PlanAsync(TerminalActor actor, WorkspaceDefinition workspace, string action, CancellationToken token) =>
            Task.FromResult(new WorkspaceLifecycleHostProof(workspace.Id, action, _epoch, _state, new string('c', 64), new string('d', 64)));
        public Task<WorkspaceLifecycleHostProof> ApplyAsync(TerminalActor actor, WorkspaceDefinition workspace,
            WorkspaceLifecyclePlan plan, Guid operationId, CancellationToken token)
        {
            LastOperator = actor.Subject; LastOwner = workspace.OwnerSubject;
            if (_operation != operationId)
            {
                Assert.Equal(plan.ExpectedVersion, _epoch);
                _epoch++; _state = plan.Action == "stop" ? "Stopped" : "Active"; _operation = operationId; Effects++;
            }
            if (LoseReply) { LoseReply = false; throw new WorkspaceLifecycleException(WorkspaceLifecycleFailure.Retryable); }
            AfterApply?.Invoke();
            return Task.FromResult(new WorkspaceLifecycleHostProof(workspace.Id, plan.Action, _epoch, _state,
                plan.HostPlanSha256, plan.StorageProofSha256));
        }
    }
    private sealed class LifecycleSource : IWorkspaceSourceGateway
    {
        public int Aborts { get; private set; }
        public Task AcquireGitAsync(WorkspaceSourceClaim claim, Func<WorkspaceGitProof, CancellationToken, Task> bind,
            Func<int, byte[], CancellationToken, Task> append, CancellationToken token) => throw new NotSupportedException();
        public Task<WorkspaceSourceObservation> StageSourceAsync(WorkspaceSourceClaim claim, IAsyncEnumerable<ReadOnlyMemory<byte>> chunks, CancellationToken token) => throw new NotSupportedException();
        public Task<WorkspaceSourceObservation> InspectSourceAsync(WorkspaceSourceClaim claim, CancellationToken token) => throw new WorkspaceSourceException(WorkspaceSourceFailure.Missing);
        public Task<WorkspaceSourceObservation> PublishSourceAsync(WorkspaceSourceClaim claim, WorkspaceSourcePlan plan, CancellationToken token) => throw new NotSupportedException();
        public Task<WorkspaceSourceObservation> DiscardSourceAsync(WorkspaceSourceClaim claim, WorkspaceSourcePlan plan, CancellationToken token) => throw new NotSupportedException();
        public Task AbortSourceAsync(WorkspaceSourceClaim claim, CancellationToken token) { Aborts++; return Task.CompletedTask; }
    }
}
