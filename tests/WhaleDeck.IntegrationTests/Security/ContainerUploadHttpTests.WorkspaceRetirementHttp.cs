using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Development;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Persistence;

namespace WhaleDeck.IntegrationTests.Security;

public sealed partial class ContainerUploadHttpTests
{
    // Real BFF Cookie/CSRF, HTTP, live authorization, dispatcher and PostgreSQL.
    // Synthetic host receipts prove orchestration only, not physical deletion.
    internal static async Task RetireWorkspaceOverHttpInPostgres(DbContextOptions<PlatformDbContext> options)
    {
        await using var inspect = new PlatformDbContext(options);
        var template = await inspect.DevelopmentWorkspaces.AsNoTracking().FirstAsync(w => w.State == WorkspaceStorageAllocation.Allocated);
        var directory = new ArchiveDirectory(); var host = new RetirementHost(); var clock = new WorkspaceRunClock();
        var policy = new WorkspaceRegistrationPolicy { StorageRootIds = [template.StorageRootId], CreateGroupIds = [directory.Group] };
        await using var server = await Fixture.Start(services =>
        {
            services.AddSingleton<TimeProvider>(clock); services.AddSingleton(policy);
            services.AddSingleton<IPrivilegedIdentityDirectory>(directory);
            services.AddScoped(_ => new PlatformDbContext(options));
            services.AddScoped<IDevelopmentWorkspaceRegistry, DevelopmentWorkspaceRegistry>();
            services.AddScoped<IWorkspaceReportAccess, WorkspaceReportAccessRepository>();
            services.AddScoped<WorkspaceReportAuthorization>(); services.AddScoped<DevelopmentWorkspaces>();
            services.AddScoped<IWorkspaceLifecycleStore, WorkspaceLifecycleRepository>();
            services.AddScoped<IWorkspaceRetirementStore, WorkspaceRetirementRepository>();
            services.AddScoped<WorkspaceRetirements>(); services.AddScoped<WorkspaceRetirementDispatcher>();
            services.AddSingleton<IWorkspaceRetirementGateway>(host);
        });
        using var owner = await server.Client("developer-subject", false, TimeSpan.FromHours(1));
        using var admin = await server.Client("admin-subject", true, TimeSpan.FromHours(1));
        using var recovery = await server.Client("recovery-admin", true, TimeSpan.FromHours(1));
        using var outsider = await server.Client("outsider", false, TimeSpan.FromHours(1));
        var ownerCsrf = await Csrf(owner); var adminCsrf = await Csrf(admin); var recoveryCsrf = await Csrf(recovery);
        var (workspace, archiveId) = await Seed(); var path = Path(workspace.Id);
        using (var response = await outsider.GetAsync(path)) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using (var response = await Send(owner, path, null, new WorkspaceRetirementBegin(archiveId, 2), Guid.NewGuid())) Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using (var request = Write(owner, HttpMethod.Post, path, ownerCsrf, JsonContent.Create(new WorkspaceRetirementBegin(archiveId, 2)), Guid.NewGuid().ToString("D")))
        {
            request.Headers.Remove("Origin"); request.Headers.Add("Origin", "https://foreign.invalid");
            using var response = await owner.SendAsync(request); Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        using (var response = await Send(owner, path, ownerCsrf, new WorkspaceRetirementBegin(archiveId, 2))) Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using (var response = await Send(owner, path, ownerCsrf, new { archiveId, expectedEpoch = 2, deletionAuthorized = true }, Guid.NewGuid())) Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await inspect.WorkspaceRetirementWorkflows.CountAsync(w => w.WorkspaceId == workspace.Id));
        var key = Guid.NewGuid(); var queued = await Begin();
        Assert.Equal(queued.RetirementId, (await Begin()).RetirementId);
        Assert.Equal(queued.RetirementId, (await owner.GetFromJsonAsync<WorkspaceRetirementView>(path))!.RetirementId);
        using (var response = await owner.GetAsync(Path(Guid.NewGuid()) + $"/{queued.RetirementId:D}")) Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using (var response = await Send(owner, path, ownerCsrf, new WorkspaceRetirementBegin(archiveId, 2), Guid.NewGuid())) Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await Change(owner, ownerCsrf, queued, "cancel"); Assert.Equal("Stopped", await State(workspace.Id)); Assert.Equal(0, host.Starts);

        // A committed Start fence whose Worker died before RPC is never replayed.
        key = Guid.NewGuid(); var orphaned = await Begin();
        WorkspaceRetirementClaim orphan;
        await using (var scope = server.Scope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IWorkspaceRetirementStore>();
            orphan = (await store.ClaimAsync(default))!; Assert.True(await store.MarkDispatchAsync(orphan, default));
        }
        clock.Advance(91); await Tick(); Assert.Equal(0, host.Starts); Assert.Equal(orphan.Task.TaskId, host.LastTask!.TaskId);
        await Change(owner, ownerCsrf, orphaned, "cancel"); clock.Advance(4); await Tick();
        Assert.Equal("Canceled", (await Get(orphaned)).State); Assert.Contains(orphan.Task.TaskId, host.Tombstones);

        key = Guid.NewGuid(); var active = await Begin();
        host.FailNextPreflight = true; clock.Advance(4); await Tick(); Assert.Equal(0, host.Starts);
        var step = await inspect.WorkspaceRetirementSteps.AsNoTracking().SingleAsync(s => s.RetirementId == active.RetirementId);
        Assert.Null(step.DispatchedAtUtc);
        host.LoseNextStartReply = true; clock.Advance(4); await Tick(); Assert.Equal(1, host.Starts);
        clock.Advance(4); await Tick(); var ready = await Get(active); Assert.True(ready.AwaitingConfirmation); Assert.Equal(1, host.Starts);
        var digest = ReadDigest(ready.PlanJson!, "retirementPlanSha256");
        using (var response = await Send(owner, path + $"/{active.RetirementId:D}/confirm", ownerCsrf, new WorkspaceRetirementConfirmation(digest, false))) Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using (var response = await Send(owner, path + $"/{active.RetirementId:D}/confirm", ownerCsrf, new WorkspaceRetirementConfirmation(new string('0', 64), true))) Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await Confirm(owner, ownerCsrf, active, digest);
        host.LoseNextStartReply = true; clock.Advance(4); await Tick(); Assert.Equal(2, host.Starts); var executeTask = host.LastTask!.TaskId;
        Assert.Equal("Retiring", await State(workspace.Id)); Assert.Equal(0, await inspect.WorkspaceRetirementCompletions.CountAsync(c => c.WorkspaceId == workspace.Id));
        clock.Advance(4); await Tick(); var retired = await Get(active);
        Assert.Equal(2, host.Starts); Assert.Equal(executeTask, host.LastTask!.TaskId);
        Assert.True(retired.BudgetReleased && retired.SourceRetired && retired.RetainsHdd); Assert.Equal("Retired", await State(workspace.Id));
        Assert.Equal(1, await inspect.AuditEvents.CountAsync(a => a.JobId == active.RetirementId && a.Action == "development.workspace.retirement.budget-released"));
        await Confirm(admin, adminCsrf, active, digest); Assert.Equal(retired.CompletedAtUtc, (await Get(active)).CompletedAtUtc);

        // Current identity loss before dispatch pauses the original task; a
        // different current administrator may authorize that same request.
        (workspace, archiveId) = await Seed(); path = Path(workspace.Id); key = Guid.NewGuid(); var revoked = await Begin();
        directory.OwnerDeleted = true; clock.Advance(4); await Tick();
        Assert.True((await Get(revoked)).AuthorizationRequired); Assert.Equal(2, host.Starts);
        var pending = await inspect.WorkspaceRetirementSteps.AsNoTracking().SingleAsync(s => s.RetirementId == revoked.RetirementId); Assert.Null(pending.DispatchedAtUtc);
        await Change(recovery, recoveryCsrf, revoked, "authorize"); clock.Advance(4); await Tick();
        ready = await Get(revoked); Assert.Equal(pending.Id, host.LastTask!.TaskId); Assert.Equal("recovery-admin", host.LastActor);
        await Confirm(recovery, recoveryCsrf, revoked, ReadDigest(ready.PlanJson!, "retirementPlanSha256"));
        host.Status = "Running"; host.AfterStart = () => directory.RecoveryAllowed = false;
        clock.Advance(4); await Tick(); var attention = await Get(revoked);
        Assert.True(attention.AuthorizationRequired && attention.AttentionRequired && attention.CancelRequested);
        Assert.False(attention.BudgetReleased); Assert.Equal("Retiring", await State(workspace.Id));
        Assert.Contains(host.LastTask!.TaskId, host.Tombstones); var starts = host.Starts;
        clock.Advance(91); await Tick(); Assert.Equal(starts, host.Starts);
        await Change(admin, adminCsrf, revoked, "authorize"); await Change(admin, adminCsrf, revoked, "cancel");
        Assert.True((await Get(revoked)).AttentionRequired); Assert.Equal("Retiring", await State(workspace.Id));
        Console.WriteLine("INBOX_PHASE=workspace-retirement-real-http-postgres-csrf-explicit-delete-fixed-task-lost-reply-live-revocation");

        async Task Tick() { await using var scope = server.Scope(); await scope.ServiceProvider.GetRequiredService<WorkspaceRetirementDispatcher>().TickAsync(default); }
        static string Path(Guid id) => $"/api/v1/development/workspaces/{id:D}/retirements";
        async Task<string> State(Guid id) => await inspect.DevelopmentWorkspaces.AsNoTracking().Where(w => w.Id == id).Select(w => w.LifecycleState).SingleAsync();
        async Task<WorkspaceRetirementView> Get(WorkspaceRetirementView view) => (await admin.GetFromJsonAsync<WorkspaceRetirementView>(Path(view.WorkspaceId) + $"/{view.RetirementId:D}"))!;
        async Task<WorkspaceRetirementView> Begin()
        { using var response = await Send(owner, path, ownerCsrf, new WorkspaceRetirementBegin(archiveId, 2), key); Assert.Equal(HttpStatusCode.Accepted, response.StatusCode); return (await response.Content.ReadFromJsonAsync<WorkspaceRetirementView>())!; }
        static async Task Change(HttpClient client, string csrf, WorkspaceRetirementView view, string action)
        { using var response = await Send(client, Path(view.WorkspaceId) + $"/{view.RetirementId:D}/{action}", csrf); Assert.Equal(HttpStatusCode.Accepted, response.StatusCode); }
        static async Task Confirm(HttpClient client, string csrf, WorkspaceRetirementView view, string sha)
        { using var response = await Send(client, Path(view.WorkspaceId) + $"/{view.RetirementId:D}/confirm", csrf, new WorkspaceRetirementConfirmation(sha, true)); Assert.Equal(HttpStatusCode.Accepted, response.StatusCode); }
        static async Task<HttpResponseMessage> Send(HttpClient client, string url, string? csrf, object? body = null, Guid? requestKey = null)
        { using var request = Write(client, HttpMethod.Post, url, csrf, body is null ? null : JsonContent.Create(body), requestKey?.ToString("D")); return await client.SendAsync(request); }
        async Task<(WorkspaceDefinition Workspace, Guid ArchiveId)> Seed()
        {
            var id = Guid.NewGuid();
            inspect.DevelopmentWorkspaces.Add(new DevelopmentWorkspaceRecord
            {
                Id = id, Slug = "retire-http-" + id.ToString("N")[..16], OwnerSubject = "developer-subject", StorageRootId = template.StorageRootId,
                State = template.State, Version = template.Version, LifecycleState = "Stopped", LifecycleVersion = 2,
                CpuMillis = template.CpuMillis, MemoryMiB = template.MemoryMiB, DiskMiB = template.DiskMiB,
                EnvironmentJson = template.EnvironmentJson, EnvironmentSha256 = template.EnvironmentSha256,
                RegistrationKey = Guid.NewGuid(), RequestSha256 = template.RequestSha256, CreatedAtUtc = template.CreatedAtUtc,
                StoragePlanSha256 = template.StoragePlanSha256, StorageAllocationStartedAtUtc = template.StorageAllocationStartedAtUtc, StorageAllocatedAtUtc = template.StorageAllocatedAtUtc
            });
            await inspect.SaveChangesAsync(); inspect.ChangeTracker.Clear();
            var value = (await new DevelopmentWorkspaceRegistry(inspect).FindAsync(id, null, default))!;
            var actor = new TerminalActor(value.OwnerSubject, null, [], new string('a', 64), "retire-http-seed", null);
            await using var db = new PlatformDbContext(options); var archives = new WorkspaceArchiveRepository(db, clock, new WorkspaceLifecycleRepository(db, clock));
            var archive = await archives.BeginAsync(actor, value, Guid.NewGuid(), 2, default); var archiveHost = new ArchiveHost();
            foreach (var phase in new[] { "plan", "capture", "verify" })
            {
                var claim = (await archives.ClaimAsync(default))!; Assert.Equal(phase, claim.Task.Phase);
                Assert.True(await archives.MarkDispatchAsync(claim, default));
                await archives.RecordAsync(claim, await archiveHost.StartArchiveAsync(actor, value, claim.Task, default), default);
                if (phase == "plan")
                { var ready = (await archives.GetAsync(id, archive.ArchiveId, default))!; await archives.ConfirmAsync(actor, id, archive.ArchiveId, ReadDigest(ready.PlanJson!, "planSha256"), default); }
                clock.Advance(4);
            }
            host.Plans[id] = CanonicalRetirement(RetirementPlan((await archives.GetAsync(id, archive.ArchiveId, default))!.VerifyJson!));
            return (value, archive.ArchiveId);
        }
    }
    private sealed class RetirementHost : IWorkspaceRetirementGateway
    {
        public int Starts { get; private set; }
        public bool FailNextPreflight { get; set; }
        public bool LoseNextStartReply { get; set; }
        public string Status { get; set; } = "Succeeded";
        public Action? AfterStart { get; set; }
        public WorkspaceRetirementTask? LastTask { get; private set; }
        public string? LastActor { get; private set; }
        public Dictionary<Guid, string> Plans { get; } = [];
        public HashSet<Guid> Tombstones { get; } = [];
        private readonly Dictionary<Guid, string> _started = [];
        public Task<WorkspaceRetirementTaskProof> StartRetirementAsync(TerminalActor actor, WorkspaceDefinition workspace, WorkspaceRetirementTask task, CancellationToken token)
        {
            if (FailNextPreflight) { FailNextPreflight = false; throw new WorkspaceRetirementException(WorkspaceRetirementFailure.Unsupported, startWasNotDispatched: true); }
            Assert.False(_started.ContainsKey(task.TaskId)); Starts++; _started[task.TaskId] = Status;
            var proof = Result(actor, workspace, task, Status); AfterStart?.Invoke(); AfterStart = null;
            if (LoseNextStartReply) { LoseNextStartReply = false; throw new WorkspaceRetirementException(WorkspaceRetirementFailure.Retryable); }
            return Task.FromResult(proof);
        }
        public Task<WorkspaceRetirementTaskProof> ObserveRetirementAsync(TerminalActor actor, WorkspaceDefinition workspace, WorkspaceRetirementTask task, CancellationToken token) =>
            Task.FromResult(Result(actor, workspace, task, Tombstones.Contains(task.TaskId) ? "Stopped" : _started.GetValueOrDefault(task.TaskId, "Pending")));
        public Task<WorkspaceRetirementTaskProof> CancelRetirementAsync(TerminalActor actor, WorkspaceDefinition workspace, WorkspaceRetirementTask task, CancellationToken token)
        { Tombstones.Add(task.TaskId); return Task.FromResult(Result(actor, workspace, task, _started.GetValueOrDefault(task.TaskId) == "Succeeded" ? "Succeeded" : "Stopped")); }
        private WorkspaceRetirementTaskProof Result(TerminalActor actor, WorkspaceDefinition workspace, WorkspaceRetirementTask task, string status)
        {
            LastTask = task; LastActor = actor.Subject;
            var view = new WorkspaceRetirementView(task.RetirementId, workspace.Id, task.ArchiveId, "Running", task.Phase, null, false, false, false, false, null, null, null, null, null);
            var claim = new WorkspaceRetirementClaim(view, workspace, task, actor, Guid.NewGuid());
            return Proof(claim, status == "Succeeded" ? task.Phase == "plan" ? Plans[workspace.Id] : Execution(claim, Plans[workspace.Id]) : null, status);
        }
    }
}
