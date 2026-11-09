using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using WhaleDeck.Application.Development;
using WhaleDeck.Infrastructure.Persistence;
using WhaleDeck.IntegrationTests.Security;

namespace WhaleDeck.IntegrationTests;

public sealed partial class NativeNotificationDispatchTests
{
    private static async Task WorkspaceRunReportBoundaries(DbContextOptions<PlatformDbContext> options)
    {
        await using var db = new PlatformDbContext(options);
        var systemNow = DateTimeOffset.UtcNow;
        var now = new DateTimeOffset(systemNow.Ticks - systemNow.Ticks % TimeSpan.TicksPerMicrosecond + 7, TimeSpan.Zero);
        var clock = new WorkspaceReportClock(now);
        var store = new WorkspaceRunReportRepository(db, clock);
        var workspace = Guid.NewGuid();
        var evidence = Evidence(now.AddMinutes(-3));
        var reports = new WorkspaceRunReports(new NoWorkspaceReportArtifacts());
        var report = await reports.BuildAsync(evidence, [], default);

        Assert.Equal(WorkspaceReportWriteState.SourceUnavailable, await store.SaveAsync(report, default));
        await AddJob(evidence, "Running");
        Assert.Equal(WorkspaceReportWriteState.SourceUnavailable, await store.SaveAsync(report, default));
        var job = await db.OperationJobs.SingleAsync(item => item.Id == evidence.JobId);
        job.State = "Failed";
        job.Phase = "Failed";
        job.CompletedAtUtc = evidence.FinalizedAtUtc;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        foreach (var invalid in new[]
        {
            report with { WorkspaceId = Guid.NewGuid() },
            report with { RunId = Guid.NewGuid() },
            report with { ActorSubject = "other-subject" },
            report with { TraceId = new('1', 32) },
            report with { Source = report.Source with { Fingerprint = new('2', 64) } },
            report with { Environment = report.Environment with { Version = 2 } },
            report with { RetainUntilUtc = report.RetainUntilUtc.AddDays(1) },
            report with { Outcome = WorkspaceRunOutcome.Unknown },
            report with { FinalizedAtUtc = report.FinalizedAtUtc.AddSeconds(1), RetainUntilUtc = report.RetainUntilUtc.AddSeconds(1) }
        })
            Assert.Equal(WorkspaceReportWriteState.SourceUnavailable, await store.SaveAsync(invalid, default));

        await using var second = new PlatformDbContext(options);
        var writes = await Task.WhenAll(store.SaveAsync(report, default),
            new WorkspaceRunReportRepository(second, clock).SaveAsync(report, default));
        Assert.Contains(WorkspaceReportWriteState.Created, writes);
        Assert.Contains(WorkspaceReportWriteState.AlreadyStored, writes);
        var persistedJob = await second.OperationJobs.AsNoTracking().SingleAsync(item => item.Id == evidence.JobId);
        var persistedStart = persistedJob.StartedAtUtc!.Value;
        var persistedFinish = persistedJob.CompletedAtUtc!.Value;
        Assert.NotEqual(evidence.FinalizedAtUtc, persistedFinish);
        var rebuilt = await reports.BuildAsync(evidence with
        {
            StartedAtUtc = persistedStart, FinalizedAtUtc = persistedFinish, RetainUntilUtc = persistedFinish.AddDays(14),
            Steps = [evidence.Steps[0] with { StartedAtUtc = persistedStart, FinishedAtUtc = persistedFinish }]
        }, [], default);
        Assert.Equal(WorkspaceReportWriteState.AlreadyStored,
            await new WorkspaceRunReportRepository(second, clock).SaveAsync(rebuilt, default));
        Assert.Equal(WorkspaceReportWriteState.Conflict, await store.SaveAsync(report with { ExitCode = 2 }, default));

        var json = await store.ReadAsync(workspace, report.RunId, WorkspaceReportFormat.Json, default);
        var markdown = await store.ReadAsync(workspace, report.RunId, WorkspaceReportFormat.Markdown, default);
        Assert.NotNull(json);
        Assert.NotNull(markdown);
        Assert.Equal(WorkspaceRunReportExport.ToJson(report), json.Content);
        Assert.Equal(Encoding.UTF8.GetBytes(WorkspaceRunReportExport.ToMarkdown(report)), markdown.Content);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(json.Content)), json.Sha256);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(markdown.Content)), markdown.Sha256);
        Assert.Null(await store.ReadAsync(Guid.NewGuid(), report.RunId, WorkspaceReportFormat.Json, default));
        Assert.Null(await store.ReadAsync(workspace, Guid.NewGuid(), WorkspaceReportFormat.Markdown, default));
        await ContainerUploadHttpTests.DownloadWorkspaceReportsFromPostgres(options, workspace, report.RunId, json);
        var downloadAudit = await db.AuditEvents.AsNoTracking().SingleAsync(row =>
            row.Action == "development.workspace.report-download" && row.TargetId == report.RunId.ToString("D"));
        Assert.Equal("admin-subject", downloadAudit.ActorSubject);
        Assert.Equal("Prepared", downloadAudit.Result);
        using (var detail = JsonDocument.Parse(downloadAudit.DetailJson!))
        {
            Assert.Equal(workspace, detail.RootElement.GetProperty("workspaceId").GetGuid());
            Assert.Equal("Json", detail.RootElement.GetProperty("format").GetString());
            Assert.Equal(2, detail.RootElement.EnumerateObject().Count());
        }
        Console.WriteLine("INBOX_PHASE=workspace-report-download-real-http-postgres-bytes-scope-audit");

        var replacement = Encoding.UTF8.GetBytes("unexpected");
        var replacementHash = Convert.ToHexStringLower(SHA256.HashData(replacement));
        var rewrite = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE workspace_run_reports SET markdown_content={replacement}, markdown_sha256={replacementHash} WHERE run_id={report.RunId}"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, rewrite.SqlState);
        Assert.Equal("workspace_report_immutable", rewrite.MessageText);

        var later = Evidence(now.AddMinutes(-1));
        await AddJob(later, "Failed");
        var laterReport = await reports.BuildAsync(later, [], default);
        Assert.Equal(WorkspaceReportWriteState.Created, await store.SaveAsync(laterReport, default));
        Assert.Equal(0, await store.ExpireAsync(1, default));
        clock.UtcNow = later.RetainUntilUtc.AddMilliseconds(1);
        Assert.Null(await store.ReadAsync(workspace, report.RunId, WorkspaceReportFormat.Json, default));
        Assert.Equal(1, await store.ExpireAsync(1, default));
        var expired = await db.WorkspaceRunReports.AsNoTracking().SingleAsync(row => row.RunId == report.RunId);
        Assert.NotNull(expired.ExpiredAtUtc);
        Assert.Null(expired.JsonContent);
        Assert.Null(expired.MarkdownContent);
        Assert.Equal(json.Sha256, expired.JsonSha256);
        Assert.Equal(markdown.Sha256, expired.MarkdownSha256);
        Assert.False(await db.WorkspaceRunReports.AnyAsync(row => row.RunId == later.RunId && row.ExpiredAtUtc != null));
        Assert.Equal(WorkspaceReportWriteState.Expired, await store.SaveAsync(report, default));
        Assert.Equal(1, await store.ExpireAsync(1, default));
        Assert.Equal(0, await store.ExpireAsync(1, default));
        var downgrade = await Assert.ThrowsAsync<PostgresException>(() =>
            db.GetService<IMigrator>().MigrateAsync("20261109082000_ArchiveRetirementPreStartClosure"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, downgrade.SqlState);
        Assert.Equal("workspace_report_history_present", downgrade.MessageText);
        Assert.Equal(2, await db.WorkspaceRunReports.CountAsync());
        Assert.False(db.Database.HasPendingModelChanges());
        Console.WriteLine("INBOX_PHASE=workspace-report-postgres-scope-two-formats-idempotency-expiry-downgrade");

        WorkspaceRunReportEvidence Evidence(DateTimeOffset finalized) => new(
            workspace, Guid.NewGuid(), Guid.NewGuid(), new('a', 32), "workspace-qa-subject",
            finalized.AddSeconds(-1), finalized, finalized.AddDays(14), WorkspaceRunOutcome.Failed, 1,
            new(new('b', 64), null, null, false), new(new('c', 64), 1, []),
            [new(Guid.NewGuid(), "tests", WorkspaceRunOutcome.Failed, finalized.AddSeconds(-1), finalized, 1, [])]);

        async Task AddJob(WorkspaceRunReportEvidence run, string state)
        {
            db.OperationJobs.Add(new()
            {
                Id = run.JobId, JobType = WorkspaceRunJobBinding.JobType, ResourceId = run.WorkspaceId,
                ActorSubject = run.ActorSubject, IdempotencyKey = run.RunId.ToString("D"),
                CreatedAtUtc = run.StartedAtUtc, StartedAtUtc = run.StartedAtUtc,
                CompletedAtUtc = state == "Failed" ? run.FinalizedAtUtc : null, State = state, Phase = state,
                RequestJson = new WorkspaceRunJobBinding(run.RunId, run.TraceId, run.Source.Fingerprint,
                    run.Environment.Digest, run.Environment.Version, 14).ToJobRequestJson()
            });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        }
    }

    private sealed class WorkspaceReportClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class NoWorkspaceReportArtifacts : IWorkspaceReportArtifactReader
    {
        public ValueTask<Stream?> OpenAsync(WorkspaceRegisteredTestArtifact artifact, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Storage test does not open test artifacts.");
    }
}
