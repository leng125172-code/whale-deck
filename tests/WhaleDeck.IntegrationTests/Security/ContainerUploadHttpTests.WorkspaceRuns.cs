using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Development;
using WhaleDeck.Application.Models;
using WhaleDeck.Infrastructure.Persistence;
using WhaleDeck.Worker;

namespace WhaleDeck.IntegrationTests.Security;

public sealed partial class ContainerUploadHttpTests
{
    internal static async Task RunWorkspaceJobsInPostgres(DbContextOptions<PlatformDbContext> options)
    {
        var directory = new WorkspaceReportHttpFixture();
        var clock = new WorkspaceRunClock();
        var runner = new WorkspaceRunAgentFixture();
        await using var inspection = new PlatformDbContext(options);
        var workspace = await inspection.DevelopmentWorkspaces.AsNoTracking()
            .SingleAsync(row => row.OwnerSubject == "developer-subject" && row.State == WorkspaceStorageAllocation.Allocated);
        var policy = new WorkspaceRegistrationPolicy { StorageRootIds = [workspace.StorageRootId], CreateGroupIds = [directory.Group] };
        void Configure(IServiceCollection services)
        {
            services.AddSingleton<TimeProvider>(clock);
            services.AddSingleton(policy);
            services.AddSingleton<IPrivilegedIdentityDirectory>(directory);
            services.AddSingleton<IWorkspaceRunnerGateway>(runner);
            services.AddScoped(_ => new PlatformDbContext(options));
            services.AddScoped<IDevelopmentWorkspaceRegistry, DevelopmentWorkspaceRegistry>();
            services.AddScoped<IWorkspaceReportAccess, WorkspaceReportAccessRepository>();
            services.AddScoped<WorkspaceReportAuthorization>();
            services.AddScoped<DevelopmentWorkspaces>();
            services.AddScoped<WorkspaceRuns>();
            services.AddScoped<IWorkspaceRunStore, WorkspaceRunRepository>();
            services.AddScoped<WorkspaceRunDispatcher>();
        }
        await using var original = await Fixture.Start(Configure);
        using var developer = await original.Client("developer-subject", false);
        using var admin = await original.Client("admin-subject", true);
        var csrf = await Csrf(developer);
        var adminCsrf = await Csrf(admin);
        var path = $"/api/v1/development/workspaces/{workspace.Id:D}/runs";
        var input = new WorkspaceRunInput("node", "test");
        using (var denied = await Enqueue(admin, adminCsrf, input, Guid.NewGuid()))
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        using (var denied = await Enqueue(developer, null, input, Guid.NewGuid()))
            Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        using (var unsafeInput = await Enqueue(developer, csrf, new { input.Runtime, input.Task, argv = (string[])["/bin/sh"] }, Guid.NewGuid()))
            Assert.Equal(HttpStatusCode.BadRequest, unsafeInput.StatusCode);

        var idempotency = Guid.NewGuid();
        var pair = await Task.WhenAll(Enqueue(developer, csrf, input, idempotency), Enqueue(developer, csrf, input, idempotency));
        using var first = pair[0];
        using var second = pair[1];
        var run = await Accepted(first);
        Assert.Equal(run.RunId, (await Accepted(second)).RunId);
        using (var conflict = await Enqueue(developer, csrf, new WorkspaceRunInput("dotnet", "test"), idempotency))
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        using (var busy = await Enqueue(developer, csrf, input, Guid.NewGuid()))
            Assert.Equal(HttpStatusCode.Conflict, busy.StatusCode);
        using (var readOther = await admin.GetAsync($"{path}/{run.RunId:D}"))
            Assert.Equal(HttpStatusCode.NotFound, readOther.StatusCode);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new JobRepository(inspection)
            .RequestCancellationAsync(run.RunId, "admin-subject", CancellationToken.None));

        await Task.WhenAll(Tick(original.Scope()), Tick(original.Scope()));
        Assert.Equal(1, runner.Starts);
        Assert.Equal("Running", (await Read(run.RunId)).State);
        clock.Advance(5);
        await using var originalScope = original.Scope();
        var workerServices = new ServiceCollection();
        Configure(workerServices);
        workerServices.AddSingleton(originalScope.ServiceProvider.GetRequiredService<ILoginSessionRegistry>());
        await using var restarted = workerServices.BuildServiceProvider();
        runner.InterruptOnObserve = true;
        using (var hosted = new WorkspaceRunWorker(restarted.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<WorkspaceRunWorker>.Instance))
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await hosted.StartAsync(timeout.Token);
            while ((await Read(run.RunId)).CompletedAtUtc is null)
                await Task.Delay(TimeSpan.FromMilliseconds(50), timeout.Token);
            await hosted.StopAsync(timeout.Token);
        }
        var recovered = await Read(run.RunId);
        Assert.Equal("Interrupted", recovered.HostStatus);
        Assert.Equal("Failed", recovered.State);
        Assert.Equal(1, runner.Starts);
        Assert.Equal(1, runner.Reclaims);

        var queued = await NewRun();
        using (var canceled = await Cancel(queued.RunId))
            Assert.Equal("CanceledBeforeDispatch", (await Accepted(canceled)).Phase);
        await Tick(restarted.CreateAsyncScope());
        Assert.Equal(1, runner.Starts);

        var revokedBefore = await NewRun();
        directory.LiveMembership = false;
        await Tick(restarted.CreateAsyncScope());
        directory.LiveMembership = true;
        Assert.Equal("CanceledBeforeDispatch", (await Read(revokedBefore.RunId)).Phase);
        Assert.Equal(1, runner.Starts);

        var active = await NewRun();
        await Tick(restarted.CreateAsyncScope());
        clock.Advance(5);
        directory.LiveMembership = false;
        await Tick(restarted.CreateAsyncScope());
        directory.LiveMembership = true;
        Assert.Equal("Cancelled", (await Read(active.RunId)).HostStatus);
        Assert.Equal(2, runner.Starts);
        Assert.Equal(1, runner.Cancels);

        var activeCancel = await NewRun();
        await Tick(restarted.CreateAsyncScope());
        using (var requested = await Cancel(activeCancel.RunId))
            Assert.True((await Accepted(requested)).CancellationRequested);
        await Tick(restarted.CreateAsyncScope());
        Assert.Equal("Cancelled", (await Read(activeCancel.RunId)).HostStatus);
        Assert.Equal(3, runner.Starts);
        Assert.Equal(2, runner.Cancels);

        var missing = await NewRun();
        await using (var killedWorker = original.Scope())
        {
            var repository = killedWorker.ServiceProvider.GetRequiredService<IWorkspaceRunStore>();
            var claim = Assert.IsType<WorkspaceRunClaim>(await repository.ClaimAsync(CancellationToken.None));
            Assert.Equal(missing.RunId, claim.Run.RunId);
            Assert.True(await repository.EnterDispatchAsync(claim, CancellationToken.None));
        }
        clock.Advance(41);
        runner.InterruptOnObserve = false;
        await Tick(restarted.CreateAsyncScope());
        Assert.Equal("Running", (await Read(missing.RunId)).State);
        clock.Advance(511);
        await Tick(restarted.CreateAsyncScope());
        Assert.Equal("ResultUnknown", (await Read(missing.RunId)).Phase);
        Assert.Equal(3, runner.Starts);

        var events = await inspection.OperationJobEvents.AsNoTracking().Where(row => row.JobId == run.RunId)
            .OrderBy(row => row.Sequence).ToArrayAsync();
        Assert.Equal(["WORKSPACE_RUN_QUEUED", "WORKSPACE_RUN_DISPATCH_COMMITTED", "WORKSPACE_RUN_INTERRUPTED"],
            events.Select(row => row.MessageCode));
        Assert.Equal(1, await inspection.AuditEvents.CountAsync(row => row.JobId == run.RunId && row.Action == "development.workspace.run.enqueue"));
        Assert.Equal(1, await inspection.AuditEvents.CountAsync(row => row.JobId == run.RunId && row.Action == "development.workspace.run.settle"));
        Assert.False(await inspection.WorkspaceRunIntents.AnyAsync(row => row.FinishedAtUtc == null));
        Console.WriteLine("INBOX_PHASE=workspace-run-real-http-postgres-single-dispatch-restart-reclaim-and-revocation");

        async Task<WorkspaceRunView> NewRun()
        {
            using var response = await Enqueue(developer, csrf, input, Guid.NewGuid());
            return await Accepted(response);
        }

        async Task<HttpResponseMessage> Enqueue(HttpClient client, string? token, object body, Guid key)
        {
            using var request = Write(client, HttpMethod.Post, path, token, JsonContent.Create(body), key.ToString("D"));
            return await client.SendAsync(request);
        }

        async Task<HttpResponseMessage> Cancel(Guid runId)
        {
            using var request = Write(developer, HttpMethod.Post, $"{path}/{runId:D}/cancel", csrf);
            return await developer.SendAsync(request);
        }

        async Task<WorkspaceRunView> Read(Guid runId) =>
            Assert.IsType<WorkspaceRunView>(await developer.GetFromJsonAsync<WorkspaceRunView>($"{path}/{runId:D}"));

        static async Task<WorkspaceRunView> Accepted(HttpResponseMessage response)
        {
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            return Assert.IsType<WorkspaceRunView>(await response.Content.ReadFromJsonAsync<WorkspaceRunView>());
        }

        static async Task Tick(AsyncServiceScope scope)
        {
            await using var lifetime = scope;
            await scope.ServiceProvider.GetRequiredService<WorkspaceRunDispatcher>().TickAsync(CancellationToken.None);
        }
    }

    private sealed class WorkspaceRunClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(int seconds) => _now = _now.AddSeconds(seconds);
    }

    private sealed class WorkspaceRunAgentFixture : IWorkspaceRunnerGateway
    {
        private int _starts;
        public int Starts => _starts;
        public int Reclaims { get; private set; }
        public int Cancels { get; private set; }
        public bool InterruptOnObserve { get; set; }

        public Task<WorkspaceRunnerReceipt> StartAsync(TerminalActor actor, WorkspaceDefinition workspace, WorkspaceRunCommand command, CancellationToken token)
        {
            Assert.True(command.RequireControl);
            Assert.Equal(["/usr/local/bin/node", "--test"], command.Arguments);
            Assert.Equal(workspace.OwnerSubject, actor.Subject);
            Interlocked.Increment(ref _starts);
            throw new WorkspaceRunnerException(WorkspaceRunnerFailure.Retryable);
        }

        public Task<WorkspaceRunnerReceipt> ObserveAsync(TerminalActor actor, WorkspaceDefinition workspace, WorkspaceRunCommand command, CancellationToken token) =>
            throw new WorkspaceRunnerException(InterruptOnObserve ? WorkspaceRunnerFailure.Interrupted : WorkspaceRunnerFailure.NotFound);

        public Task<WorkspaceRunnerReceipt> ReclaimAsync(TerminalActor actor, WorkspaceDefinition workspace, WorkspaceRunCommand command, CancellationToken token)
        {
            Reclaims++;
            return Task.FromResult(new WorkspaceRunnerReceipt(command.RunId, new string('a', 64), "Interrupted", null));
        }

        public Task<WorkspaceRunnerReceipt> CancelAsync(TerminalActor actor, WorkspaceDefinition workspace, WorkspaceRunCommand command, CancellationToken token)
        {
            Cancels++;
            return Task.FromResult(new WorkspaceRunnerReceipt(command.RunId, new string('b', 64), "Cancelled", null));
        }
    }
}
