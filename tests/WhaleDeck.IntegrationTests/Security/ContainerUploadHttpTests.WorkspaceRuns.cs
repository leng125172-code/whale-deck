using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
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
            services.AddSingleton<IWorkspaceTestArtifactGateway>(runner);
            services.AddScoped(_ => new PlatformDbContext(options));
            services.AddScoped<IDevelopmentWorkspaceRegistry, DevelopmentWorkspaceRegistry>();
            services.AddScoped<IWorkspaceReportAccess, WorkspaceReportAccessRepository>();
            services.AddScoped<WorkspaceReportAuthorization>();
            services.AddScoped<DevelopmentWorkspaces>();
            services.AddScoped<WorkspaceRuns>();
            services.AddScoped<IWorkspaceRunStore, WorkspaceRunRepository>();
            services.AddScoped<IWorkspaceRunReportWork, WorkspaceRunRepository>();
            services.AddScoped<IWorkspaceRunReportStore, WorkspaceRunReportRepository>();
            services.AddScoped<IWorkspaceCollectedReportStore, WorkspaceRunReportRepository>();
            services.AddScoped<IWorkspaceRunReportReadAudit, WorkspaceRunReportReadAudit>();
            services.AddScoped<WorkspaceRunReportDownloads>();
            services.AddScoped<WorkspaceRunReportPipeline>();
            services.AddScoped<WorkspaceRunDispatcher>();
        }
        await using var original = await Fixture.Start(Configure);
        using var developer = await original.Client("developer-subject", false, TimeSpan.FromHours(1));
        using var admin = await original.Client("admin-subject", true, TimeSpan.FromHours(1));
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

        for (var i = 0; i < 6; i++) await TickReport(restarted.CreateAsyncScope());
        Assert.Equal(6, await inspection.WorkspaceRunIntents.CountAsync(row => row.ReportStage == "Complete"));
        Assert.Equal(0, runner.ArtifactReads);
        var skippedReport = await developer.GetFromJsonAsync<JsonElement>($"{path}/{queued.RunId:D}/report/json");
        Assert.Equal("Canceled", skippedReport.GetProperty("outcome").GetString());
        Assert.Equal("Skipped", skippedReport.GetProperty("steps")[0].GetProperty("outcome").GetString());
        var unknownReport = await developer.GetFromJsonAsync<JsonElement>($"{path}/{missing.RunId:D}/report/json");
        Assert.Equal("Unknown", unknownReport.GetProperty("outcome").GetString());

        runner.ObserveFinished = true;
        var reportRun = await NewRun();
        await Tick(restarted.CreateAsyncScope());
        clock.Advance(5);
        await Tick(restarted.CreateAsyncScope());
        Assert.Equal("Pending", (await Read(reportRun.RunId)).ReportStatus);
        Assert.Equal("TaskFailed", (await Read(reportRun.RunId)).Phase);
        Assert.Equal("WORKSPACE_RUN_TASK_FAILED", (await Read(reportRun.RunId)).ErrorCode);
        await inspection.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION qa_workspace_report_reject() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'qa_report_write_failure'; END $$;
            CREATE TRIGGER qa_workspace_report_reject BEFORE INSERT ON workspace_run_reports
                FOR EACH ROW EXECUTE FUNCTION qa_workspace_report_reject()
            """);
        try
        {
            var failed = await Assert.ThrowsAsync<PostgresException>(() => TickReport(restarted.CreateAsyncScope()));
            Assert.Equal("qa_report_write_failure", failed.MessageText);
            Assert.Equal("Collect", await Stage(reportRun.RunId));
            Assert.False(await inspection.WorkspaceRunReports.AnyAsync(row => row.RunId == reportRun.RunId));
            Assert.Equal(0, runner.RetireCalls);
        }
        finally
        {
            await inspection.Database.ExecuteSqlRawAsync("""
                DROP TRIGGER qa_workspace_report_reject ON workspace_run_reports;
                DROP FUNCTION qa_workspace_report_reject()
                """);
        }
        clock.Advance(41);
        await Task.WhenAll(TickReport(restarted.CreateAsyncScope()), TickReport(restarted.CreateAsyncScope()));
        Assert.Equal("Retire", await Stage(reportRun.RunId));
        Assert.Equal("Available", (await Read(reportRun.RunId)).ReportStatus);
        Assert.Equal(2, runner.ArtifactReads);
        runner.RetireProbe = async id =>
        {
            await using var connection = new PlatformDbContext(options);
            Assert.True(await connection.WorkspaceRunReports.AnyAsync(row => row.RunId == id && row.JsonContent != null && row.MarkdownContent != null));
        };
        runner.LoseRetirementReply = true;
        await RunReportWorkerUntil(async () => runner.RetireCalls == 1 && !await inspection.WorkspaceRunIntents
            .AnyAsync(row => row.Id == reportRun.RunId && row.Lease != null));
        Assert.Equal("Retire", await Stage(reportRun.RunId));
        clock.Advance(16);
        await RunReportWorkerUntil(async () => await Stage(reportRun.RunId) == "Complete");
        Assert.Equal(2, runner.RetireCalls);
        Assert.Equal(1, runner.RetireEffects);

        using var jsonResponse = await developer.GetAsync($"{path}/{reportRun.RunId:D}/report/json");
        jsonResponse.EnsureSuccessStatusCode();
        var data = await jsonResponse.Content.ReadAsByteArrayAsync();
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(data)), jsonResponse.Headers.GetValues("X-Content-SHA256").Single());
        var export = JsonSerializer.Deserialize<JsonElement>(data);
        Assert.Equal(reportRun.RunId, export.GetProperty("runId").GetGuid());
        Assert.Equal(JsonValueKind.Null, export.GetProperty("source").GetProperty("fingerprint").ValueKind);
        var reportTest = export.GetProperty("steps")[0].GetProperty("tests")[0];
        Assert.Equal("Matched", reportTest.GetProperty("integrity").GetString());
        Assert.Equal(1, reportTest.GetProperty("result").GetProperty("counts").GetProperty("failed").GetInt32());
        Assert.DoesNotContain("synthetic-private-test-data", Encoding.UTF8.GetString(data), StringComparison.Ordinal);
        Assert.DoesNotContain("synthetic-private-test-data",
            await developer.GetStringAsync($"{path}/{reportRun.RunId:D}/report/markdown"), StringComparison.Ordinal);
        directory.LiveMembership = false;
        using (var revoked = await developer.GetAsync($"{path}/{reportRun.RunId:D}/report/json"))
            Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
        directory.LiveMembership = true;

        runner.CorruptArtifact = true;
        var suspect = await NewRun();
        await Tick(restarted.CreateAsyncScope());
        clock.Advance(5);
        await Tick(restarted.CreateAsyncScope());
        await TickReport(restarted.CreateAsyncScope());
        Assert.Equal("Attention", await Stage(suspect.RunId));
        Assert.Equal("ArtifactAttention", (await Read(suspect.RunId)).ReportStatus);
        await TickReport(restarted.CreateAsyncScope());
        Assert.Equal(2, runner.RetireCalls);
        Assert.Equal(1, await inspection.AuditEvents.CountAsync(row => row.JobId == reportRun.RunId &&
            row.Action == "development.workspace.report.collect"));
        Assert.Equal(1, await inspection.AuditEvents.CountAsync(row => row.JobId == reportRun.RunId &&
            row.Action == "development.workspace.report.retire"));
        Console.WriteLine("INBOX_PHASE=workspace-report-real-postgres-atomic-stage-recovery-redaction-and-owner-download");

        var firstPage = Assert.IsType<WorkspaceRunPage>(await developer.GetFromJsonAsync<WorkspaceRunPage>(path + "?take=2"));
        Assert.Equal([suspect.RunId, reportRun.RunId], firstPage.Items.Select(item => item.RunId));
        Assert.Equal(reportRun.RunId, firstPage.NextAfter);
        var secondPage = Assert.IsType<WorkspaceRunPage>(await developer.GetFromJsonAsync<WorkspaceRunPage>(
            path + $"?take=2&after={firstPage.NextAfter:D}"));
        Assert.Equal(2, secondPage.Items.Length);
        Assert.DoesNotContain(secondPage.Items, row => firstPage.Items.Any(first => row.RunId == first.RunId));
        using (var unknownCursor = await developer.GetAsync(path + $"?after={Guid.NewGuid():D}"))
            Assert.Equal(HttpStatusCode.NotFound, unknownCursor.StatusCode);
        using (var wrongOwner = await admin.GetAsync(path))
            Assert.Equal(HttpStatusCode.NotFound, wrongOwner.StatusCode);
        Console.WriteLine("INBOX_PHASE=workspace-run-history-real-http-postgres-owner-keyset");

        Task<string?> Stage(Guid id) => inspection.WorkspaceRunIntents.AsNoTracking().Where(row => row.Id == id)
            .Select(row => row.ReportStage).SingleAsync();

        async Task RunReportWorkerUntil(Func<Task<bool>> condition)
        {
            using var hosted = new WorkspaceRunReportWorker(restarted.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<WorkspaceRunReportWorker>.Instance);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            await hosted.StartAsync(timeout.Token);
            while (!await condition()) await Task.Delay(50, timeout.Token);
            await hosted.StopAsync(timeout.Token);
        }

        static async Task TickReport(AsyncServiceScope scope)
        {
            await using var lifetime = scope;
            await scope.ServiceProvider.GetRequiredService<WorkspaceRunReportPipeline>().TickAsync(CancellationToken.None);
        }

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

    private sealed class WorkspaceRunAgentFixture : IWorkspaceRunnerGateway, IWorkspaceTestArtifactGateway
    {
        private const string Report = """
            <testsuite tests="2" failures="1" errors="0" skipped="0">
              <testcase name="pass"/>
              <testcase name="fail"><failure>synthetic-private-test-data</failure></testcase>
            </testsuite>
            """;
        private int _starts;
        public int Starts => _starts;
        public int Reclaims { get; private set; }
        public int Cancels { get; private set; }
        public bool InterruptOnObserve { get; set; }
        public bool ObserveFinished { get; set; }
        public bool CorruptArtifact { get; set; }
        public bool LoseRetirementReply { get; set; }
        public int ArtifactReads { get; private set; }
        public int RetireCalls { get; private set; }
        public int RetireEffects { get; private set; }
        public Func<Guid, Task>? RetireProbe { get; set; }
        private readonly byte[] _artifact = Encoding.UTF8.GetBytes(Report);

        public Task<WorkspaceRunnerReceipt> StartAsync(TerminalActor actor, WorkspaceDefinition workspace, WorkspaceRunCommand command, CancellationToken token)
        {
            Assert.True(command.RequireControl);
            Assert.Equal(14, command.ReportRetentionDays);
            Assert.Equal(["/usr/local/bin/node", "--test", "--test-reporter=junit",
                "--test-reporter-destination=/tmp/whaledeck-results/tests.xml"], command.Arguments);
            Assert.Equal(workspace.OwnerSubject, actor.Subject);
            Interlocked.Increment(ref _starts);
            throw new WorkspaceRunnerException(WorkspaceRunnerFailure.Retryable);
        }

        public Task<WorkspaceRunnerReceipt> ObserveAsync(TerminalActor actor, WorkspaceDefinition workspace, WorkspaceRunCommand command, CancellationToken token) =>
            ObserveFinished
                ? Task.FromResult(new WorkspaceRunnerReceipt(command.RunId, new string('c', 64), "Failed", 1,
                    new(WorkspaceTestFormat.JUnit, "Present", _artifact.Length, Convert.ToHexStringLower(SHA256.HashData(_artifact)))))
                : throw new WorkspaceRunnerException(InterruptOnObserve ? WorkspaceRunnerFailure.Interrupted : WorkspaceRunnerFailure.NotFound);

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

        public Task<byte[]> ReadTestArtifactAsync(TerminalActor actor, WorkspaceDefinition workspace, WorkspaceRunCommand command,
            WorkspaceRunnerReceipt receipt, CancellationToken token)
        {
            ArtifactReads++;
            return Task.FromResult(CorruptArtifact ? new byte[] { 0x41 } : _artifact);
        }

        public async Task RetireTestArtifactAsync(TerminalActor actor, WorkspaceDefinition workspace, WorkspaceRunCommand command,
            WorkspaceRunnerReceipt receipt, CancellationToken token)
        {
            if (RetireProbe is not null) await RetireProbe(command.RunId);
            RetireCalls++;
            if (RetireEffects == 0) RetireEffects++;
            if (!LoseRetirementReply) return;
            LoseRetirementReply = false;
            throw new WorkspaceRunnerException(WorkspaceRunnerFailure.Retryable);
        }
    }
}
