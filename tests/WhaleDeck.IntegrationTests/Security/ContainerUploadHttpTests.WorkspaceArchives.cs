using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Development;
using WhaleDeck.Application.Models;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Persistence;

namespace WhaleDeck.IntegrationTests.Security;

public sealed partial class ContainerUploadHttpTests
{
    // Real HTTP/BFF/CSRF, dispatcher and PostgreSQL. Host receipts below are
    // synthetic protocol-valid fixtures, not installed HDD acceptance.
    internal static async Task ArchiveWorkspaceInPostgres(DbContextOptions<PlatformDbContext> options)
    {
        await using var inspection = new PlatformDbContext(options);
        var original = await inspection.DevelopmentWorkspaces.AsNoTracking().FirstAsync(row => row.State == WorkspaceStorageAllocation.Allocated);
        var id = Guid.NewGuid(); var directory = new ArchiveDirectory(); var host = new ArchiveHost(); var clock = new WorkspaceRunClock();
        var row = new DevelopmentWorkspaceRecord
        {
            Id = id, Slug = "archive-" + id.ToString("N"), OwnerSubject = "developer-subject", StorageRootId = original.StorageRootId,
            State = original.State, Version = original.Version, LifecycleState = "Stopped", LifecycleVersion = 2,
            CpuMillis = original.CpuMillis, MemoryMiB = original.MemoryMiB, DiskMiB = original.DiskMiB,
            EnvironmentJson = original.EnvironmentJson, EnvironmentSha256 = original.EnvironmentSha256,
            RegistrationKey = Guid.NewGuid(), RequestSha256 = original.RequestSha256, CreatedAtUtc = original.CreatedAtUtc,
            StoragePlanSha256 = original.StoragePlanSha256, StorageAllocationStartedAtUtc = original.StorageAllocationStartedAtUtc,
            StorageAllocatedAtUtc = original.StorageAllocatedAtUtc
        };
        inspection.DevelopmentWorkspaces.Add(row); await inspection.SaveChangesAsync(); inspection.ChangeTracker.Clear();
        var policy = new WorkspaceRegistrationPolicy { StorageRootIds = [row.StorageRootId], CreateGroupIds = [directory.Group] };
        await using var server = await Fixture.Start(services =>
        {
            services.AddSingleton<TimeProvider>(clock); services.AddSingleton(policy);
            services.AddSingleton<IPrivilegedIdentityDirectory>(directory);
            services.AddScoped(_ => new PlatformDbContext(options));
            services.AddScoped<IDevelopmentWorkspaceRegistry, DevelopmentWorkspaceRegistry>();
            services.AddScoped<IWorkspaceReportAccess, WorkspaceReportAccessRepository>();
            services.AddScoped<WorkspaceReportAuthorization>(); services.AddScoped<DevelopmentWorkspaces>();
            services.AddScoped<IWorkspaceLifecycleStore, WorkspaceLifecycleRepository>(); services.AddScoped<WorkspaceLifecycle>();
            services.AddSingleton<IWorkspaceLifecycleGateway>(new LifecycleHost());
            services.AddScoped<IWorkspaceArchiveStore, WorkspaceArchiveRepository>();
            services.AddScoped<WorkspaceArchives>(); services.AddScoped<WorkspaceArchiveDispatcher>();
            services.AddSingleton<IWorkspaceArchiveGateway>(host);
        });
        using var owner = await server.Client("developer-subject", false, TimeSpan.FromHours(1));
        using var admin = await server.Client("admin-subject", true, TimeSpan.FromHours(1));
        using var recovery = await server.Client("recovery-admin", true, TimeSpan.FromHours(1));
        using var outsider = await server.Client("outsider", false, TimeSpan.FromHours(1));
        var ownerCsrf = await Csrf(owner); var adminCsrf = await Csrf(admin); var recoveryCsrf = await Csrf(recovery);
        var path = $"/api/v1/development/workspaces/{id:D}"; var archivePath = path + "/archives";
        using (var denied = await outsider.GetAsync(archivePath)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var csrf = await Send(owner, archivePath, null, new WorkspaceArchiveBegin(2), Guid.NewGuid())) Assert.Equal(HttpStatusCode.BadRequest, csrf.StatusCode);
        var key = Guid.NewGuid(); var queued = await Begin(owner, ownerCsrf, key, 2);
        Assert.Equal(queued.ArchiveId, (await Begin(owner, ownerCsrf, key, 2)).ArchiveId);
        using (var competing = await Send(admin, archivePath, adminCsrf, new WorkspaceArchiveBegin(2), Guid.NewGuid())) Assert.Equal(HttpStatusCode.Conflict, competing.StatusCode);
        await AssertFenced();

        // Commit the dispatch fence, then lose the entire Worker before RPC.
        // A new scope after lease expiry observes Pending; it never Starts.
        WorkspaceArchiveClaim orphan;
        await using (var scope = server.Scope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IWorkspaceArchiveStore>();
            orphan = (await store.ClaimAsync(CancellationToken.None))!;
            Assert.True(await store.MarkDispatchAsync(orphan, CancellationToken.None));
        }
        clock.Advance(91); await Tick(); Assert.Equal(0, host.Starts);
        Assert.Equal(orphan.Task.TaskId, host.LastTask!.TaskId); await AssertFenced();
        await Cancel(owner, ownerCsrf, queued.ArchiveId); clock.Advance(4); await Tick();
        Assert.Equal("Canceled", (await Get(admin, queued.ArchiveId)).State);
        Assert.Contains(orphan.Task.TaskId, host.Tombstones);
        await using (var scope = server.Scope())
            Assert.False(await scope.ServiceProvider.GetRequiredService<IWorkspaceArchiveStore>().MarkDispatchAsync(orphan, CancellationToken.None));

        // Lost Start reply: only original task Observe can produce the plan.
        var active = await Begin(owner, ownerCsrf, Guid.NewGuid(), 2);
        host.FailNextPreflight = true; clock.Advance(4); await Tick();
        Assert.Equal(0, host.Starts);
        var preflight = await inspection.WorkspaceArchiveSteps.AsNoTracking().SingleAsync(step => step.ArchiveId == active.ArchiveId);
        Assert.Null(preflight.DispatchedAtUtc); Assert.Null(preflight.ProofJson); await AssertFenced();
        host.LoseNextStartReply = true; clock.Advance(4); await Tick();
        Assert.Equal(1, host.Starts); var planTask = host.LastTask!.TaskId;
        Assert.Equal(preflight.Id, planTask); // Restore dependency, keep the same task.
        clock.Advance(4); await Tick(); var ready = await Get(owner, active.ArchiveId);
        Assert.True(ready.AwaitingConfirmation); Assert.Equal(1, host.Starts); Assert.Equal(planTask, host.LastTask!.TaskId);
        var digest = ReadDigest(ready.PlanJson!, "planSha256");
        using (var wrong = await Send(owner, archivePath + $"/{active.ArchiveId:D}/confirm", ownerCsrf, new WorkspaceArchiveConfirmation(new string('0', 64)))) Assert.Equal(HttpStatusCode.Conflict, wrong.StatusCode);
        var confirmations = await Task.WhenAll(Send(owner, archivePath + $"/{active.ArchiveId:D}/confirm", ownerCsrf, new WorkspaceArchiveConfirmation(digest)),
            Send(admin, archivePath + $"/{active.ArchiveId:D}/confirm", adminCsrf, new WorkspaceArchiveConfirmation(digest)));
        foreach (var response in confirmations) { using (response) Assert.Equal(HttpStatusCode.Accepted, response.StatusCode); }
        Assert.Equal(2, await inspection.WorkspaceArchiveSteps.CountAsync(step => step.ArchiveId == active.ArchiveId));
        host.Status = "Running"; clock.Advance(4); await Tick(); var captureTask = host.LastTask!.TaskId;
        host.Status = "Finishing"; clock.Advance(4); await Tick(); await AssertFenced();
        Assert.Null((await Get(admin, active.ArchiveId)).CompletedAtUtc);
        host.Status = "Succeeded"; clock.Advance(4); await Tick();
        var captured = await Get(admin, active.ArchiveId); Assert.Equal("Verify", captured.Phase); Assert.NotNull(captured.CaptureJson);
        var capture = await inspection.WorkspaceArchiveSteps.AsNoTracking().SingleAsync(step => step.Id == captureTask);
        Assert.NotNull(capture.SettledAtUtc); Assert.NotNull(capture.ProofJson);
        var verify = await inspection.WorkspaceArchiveSteps.AsNoTracking().SingleAsync(step => step.ArchiveId == active.ArchiveId && step.Phase == "verify");
        Assert.Null(verify.DispatchedAtUtc); Assert.Contains(ReadDigest(captured.CaptureJson!, "receiptSha256"), verify.RequestJson, StringComparison.Ordinal);
        host.CorruptNextProof = true; clock.Advance(4); await Tick(); await AssertFenced();
        Assert.Null((await Get(admin, active.ArchiveId)).CompletedAtUtc);
        clock.Advance(4); await Tick(); var archived = await Get(admin, active.ArchiveId);
        Assert.Equal("Succeeded", archived.State); Assert.Equal("Archived", archived.Phase); Assert.Equal(3, host.Starts);
        Assert.Equal(ReadDigest(archived.CaptureJson!, "receiptSha256"), ReadDigest(archived.VerifyJson!, "receiptSha256"));
        Assert.True(archived.RetainsSource && archived.ReservesBudget);
        await inspection.Database.ExecuteSqlInterpolatedAsync($"UPDATE development_workspaces SET lifecycle_state='Active', lifecycle_version=3 WHERE id={id}");
        await inspection.Database.ExecuteSqlInterpolatedAsync($"UPDATE development_workspaces SET lifecycle_state='Stopped', lifecycle_version=4 WHERE id={id}");

        // Before dispatch, revocation pauses the same immutable task. Another
        // authorized administrator rebinds the session, never the host request.
        var renewed = await Begin(owner, ownerCsrf, Guid.NewGuid(), 4); directory.OwnerDeleted = true;
        clock.Advance(4); await Tick(); var paused = await Get(admin, renewed.ArchiveId); Assert.True(paused.AuthorizationRequired);
        var pendingStep = await inspection.WorkspaceArchiveSteps.AsNoTracking().SingleAsync(step => step.ArchiveId == renewed.ArchiveId);
        Assert.Null(pendingStep.DispatchedAtUtc);
        using (var authorized = await Send(recovery, archivePath + $"/{renewed.ArchiveId:D}/authorize", recoveryCsrf)) Assert.Equal(HttpStatusCode.Accepted, authorized.StatusCode);
        clock.Advance(4); await Tick(); var renewedPlan = await Get(admin, renewed.ArchiveId);
        Assert.True(renewedPlan.AwaitingConfirmation); Assert.Equal(pendingStep.Id, host.LastTask!.TaskId); Assert.Equal("recovery-admin", host.LastActor);
        var unchangedStep = await inspection.WorkspaceArchiveSteps.AsNoTracking().SingleAsync(step => step.Id == pendingStep.Id);
        Assert.Equal(pendingStep.RequestJson, unchangedStep.RequestJson);
        using (var confirm = await Send(recovery, archivePath + $"/{renewed.ArchiveId:D}/confirm", recoveryCsrf,
            new WorkspaceArchiveConfirmation(ReadDigest(renewedPlan.PlanJson!, "planSha256")))) Assert.Equal(HttpStatusCode.Accepted, confirm.StatusCode);
        host.Status = "Running"; host.AfterStart = () => directory.RecoveryAllowed = false;
        clock.Advance(4); await Tick();
        var revoked = await Get(admin, renewed.ArchiveId); Assert.Equal("Canceled", revoked.State); Assert.True(revoked.AuthorizationRequired);
        Assert.Contains(host.LastTask!.TaskId, host.Tombstones); Assert.Equal(2, await inspection.WorkspaceArchiveSteps.CountAsync(step => step.ArchiveId == renewed.ArchiveId));
        Assert.Null(revoked.VerifyJson); Assert.True(await inspection.AuditEvents.AnyAsync(a => a.JobId == renewed.ArchiveId && a.Action == "development.workspace.archive.authorization-required"));
        var final = await inspection.DevelopmentWorkspaces.AsNoTracking().SingleAsync(w => w.Id == id);
        Assert.Equal(row.EnvironmentJson, final.EnvironmentJson); Assert.Equal(row.DiskMiB, final.DiskMiB); Assert.Equal(row.OwnerSubject, final.OwnerSubject);
        Assert.Equal("Stopped", final.LifecycleState); Assert.Equal(4, final.LifecycleVersion);
        Assert.Equal(0, await inspection.WorkspaceArchiveIntents.CountAsync(a => a.WorkspaceId == id && a.FinishedAtUtc == null));
        Console.WriteLine("INBOX_PHASE=workspace-archive-real-http-postgres-confirm-fence-at-most-once-lost-reply-verify-reauthorize-revoke");

        async Task Tick() { await using var scope = server.Scope(); await scope.ServiceProvider.GetRequiredService<WorkspaceArchiveDispatcher>().TickAsync(CancellationToken.None); }
        async Task<WorkspaceArchiveView> Get(HttpClient client, Guid archiveId) => (await client.GetFromJsonAsync<WorkspaceArchiveView>(archivePath + $"/{archiveId:D}"))!;
        async Task<WorkspaceArchiveView> Begin(HttpClient client, string csrf, Guid requestKey, long epoch)
        { using var response = await Send(client, archivePath, csrf, new WorkspaceArchiveBegin(epoch), requestKey); Assert.Equal(HttpStatusCode.Accepted, response.StatusCode); return (await response.Content.ReadFromJsonAsync<WorkspaceArchiveView>())!; }
        async Task Cancel(HttpClient client, string csrf, Guid archiveId)
        { using var response = await Send(client, archivePath + $"/{archiveId:D}/cancel", csrf); Assert.Equal(HttpStatusCode.Accepted, response.StatusCode); }
        async Task AssertFenced()
        {
            using var response = await admin.GetAsync(path + "/lifecycle/plan?action=resume"); Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            var error = await Assert.ThrowsAsync<PostgresException>(() => inspection.Database.ExecuteSqlInterpolatedAsync($"UPDATE development_workspaces SET lifecycle_state='Resuming', lifecycle_version=lifecycle_version+1 WHERE id={id}"));
            Assert.Equal("ck_workspace_archive_not_settled", error.ConstraintName);
        }
        static async Task<HttpResponseMessage> Send(HttpClient client, string url, string? csrf, object? body = null, Guid? requestKey = null)
        { using var request = Write(client, HttpMethod.Post, url, csrf, body is null ? null : JsonContent.Create(body), requestKey?.ToString("D")); return await client.SendAsync(request); }
    }
    private static string ReadDigest(string json, string property) { using var value = JsonDocument.Parse(json); return value.RootElement.GetProperty(property).GetString()!; }
    private sealed class ArchiveDirectory : IPrivilegedIdentityDirectory
    {
        public Guid Group { get; } = Guid.NewGuid();
        public bool OwnerDeleted { get; set; }
        public bool RecoveryAllowed { get; set; } = true;
        public Task<CurrentUserDto> ResolveForPrivilegedActionAsync(string subject, string? name, CancellationToken token)
        {
            if (subject == "developer-subject" && OwnerDeleted) throw new UnauthorizedAccessException();
            return Task.FromResult(new CurrentUserDto(subject, name, subject == "developer-subject" ? [Group.ToString("D")] : [],
                subject == "admin-subject" || subject == "recovery-admin" && RecoveryAllowed, DateTimeOffset.UtcNow.AddHours(1)));
        }
    }
    private sealed class ArchiveHost : IWorkspaceArchiveGateway
    {
        public int Starts { get; private set; }
        public bool LoseNextStartReply { get; set; }
        public bool FailNextPreflight { get; set; }
        public bool CorruptNextProof { get; set; }
        public string Status { get; set; } = "Succeeded";
        public WorkspaceArchiveTask? LastTask { get; private set; }
        public string? LastActor { get; private set; }
        public Action? AfterStart { get; set; }
        public HashSet<Guid> Tombstones { get; } = [];
        private readonly HashSet<Guid> _started = [];
        public Task<WorkspaceArchiveTaskProof> StartArchiveAsync(TerminalActor actor, WorkspaceDefinition workspace, WorkspaceArchiveTask task, CancellationToken token)
        {
            if (FailNextPreflight) { FailNextPreflight = false; throw new WorkspaceArchiveException(WorkspaceArchiveFailure.Unsupported, startWasNotDispatched: true); }
            Starts++; _started.Add(task.TaskId); var proof = Proof(actor, workspace, task, Tombstones.Contains(task.TaskId) ? "Stopped" : Status);
            AfterStart?.Invoke(); AfterStart = null;
            if (LoseNextStartReply) { LoseNextStartReply = false; throw new WorkspaceArchiveException(WorkspaceArchiveFailure.Retryable); }
            return Task.FromResult(proof);
        }
        public Task<WorkspaceArchiveTaskProof> ObserveArchiveAsync(TerminalActor actor, WorkspaceDefinition workspace, WorkspaceArchiveTask task, CancellationToken token) =>
            Task.FromResult(Proof(actor, workspace, task, Tombstones.Contains(task.TaskId) ? "Stopped" : _started.Contains(task.TaskId) ? Status : "Pending"));
        public Task<WorkspaceArchiveTaskProof> CancelArchiveAsync(TerminalActor actor, WorkspaceDefinition workspace, WorkspaceArchiveTask task, CancellationToken token)
        { Tombstones.Add(task.TaskId); return Task.FromResult(Proof(actor, workspace, task, _started.Contains(task.TaskId) && Status == "Succeeded" ? "Succeeded" : "Stopped")); }
        private WorkspaceArchiveTaskProof Proof(TerminalActor actor, WorkspaceDefinition w, WorkspaceArchiveTask task, string status)
        {
            LastTask = task; LastActor = actor.Subject;
            var request = new WorkspaceArchiveRequest { Storage = new() { WorkspaceId = w.Id.ToString("D"), StorageRootId = w.StorageRootId,
                EnvironmentSha256 = w.EnvironmentSha256, DiskMib = (uint)w.Budget.DiskMiB, ActorSubject = actor.Subject, TraceId = actor.TraceId },
                TaskId = task.TaskId.ToString("D"), ArchiveId = task.ArchiveId.ToString("D"), Phase = task.Phase, ExpectedEpoch = (ulong)task.ExpectedEpoch,
                PlanSha256 = task.PlanSha256, ReceiptSha256 = task.ReceiptSha256 };
            string? result = null;
            if (status == "Succeeded")
            {
                var facts = new SortedDictionary<string, object>(StringComparer.Ordinal) {
                    ["schemaVersion"] = 1, ["expectedEpoch"] = task.ExpectedEpoch,
                    ["storage"] = new SortedDictionary<string, object>(StringComparer.Ordinal) { ["schemaVersion"] = 1, ["workspaceId"] = w.Id.ToString("D"),
                        ["rootId"] = w.StorageRootId, ["diskMiB"] = w.Budget.DiskMiB, ["environmentSha256"] = w.EnvironmentSha256 },
                    ["storageProofSha256"] = new string('a',64), ["sourceVersion"] = new string('C',64), ["sourceManifestSha256"] = new string('b',64),
                    ["entryCount"] = 2, ["totalBytes"] = 17, ["archiveFilesystemUuid"] = "11111111-1111-4111-8111-111111111111", ["hddProofSha256"] = new string('d',64) };
                facts["planSha256"] = Hash(facts);
                if (task.Phase != "plan")
                {
                    facts["operationId"] = task.ArchiveId.ToString("D"); facts["archiveFormat"] = "tar.gz"; facts["archiveBytes"] = 128;
                    facts["archiveSha256"] = new string('e',64); facts["archiveIndexSha256"] = new string('f',64); facts["archiveInode"] = 123;
                    facts["receiptSha256"] = Hash(facts);
                }
                result = JsonSerializer.Serialize(facts);
            }
            var hash = DevelopmentArchiveBinding.HostHash(request);
            if (CorruptNextProof) { CorruptNextProof = false; hash = new string('0', 64); }
            return new(task.TaskId, hash, status, null, result);
        }
        private static string Hash(SortedDictionary<string, object> value) => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
    }
}
