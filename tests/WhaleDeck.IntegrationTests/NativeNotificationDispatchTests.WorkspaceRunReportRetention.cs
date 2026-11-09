using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using WhaleDeck.Application.Development;
using WhaleDeck.Infrastructure;
using WhaleDeck.Infrastructure.Persistence;
using WhaleDeck.Worker;

namespace WhaleDeck.IntegrationTests;

public sealed partial class NativeNotificationDispatchTests
{
    private static async Task WorkspaceRunReportRetentionBoundaries(DbContextOptions<PlatformDbContext> options)
    {
        await using var db = new PlatformDbContext(options);
        var clock = new ReportRetentionClock(DateTimeOffset.UtcNow);
        var workspace = Guid.NewGuid();
        var body = "qa-only"u8.ToArray();
        var hash = Convert.ToHexStringLower(SHA256.HashData(body));
        var now = clock.GetUtcNow();
        for (var index = 0; index < 52; index++)
        {
            var jobId = Guid.NewGuid();
            var retention = index == 51 ? now.AddHours(1) : now.AddMinutes(index - 52);
            db.OperationJobs.Add(new()
            {
                Id = jobId, JobType = WorkspaceRunJobBinding.JobType, ResourceId = workspace,
                ActorSubject = "report-worker-qa", IdempotencyKey = jobId.ToString("D")
            });
            db.WorkspaceRunReports.Add(new()
            {
                RunId = Guid.NewGuid(), WorkspaceId = workspace, JobId = jobId, SchemaVersion = 1,
                FinalizedAtUtc = retention.AddDays(-14), RetainUntilUtc = retention,
                JsonContent = body, MarkdownContent = body, JsonSha256 = hash, MarkdownSha256 = hash
            });
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var connection = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString()) { CommandTimeout = 1 };
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration["ConnectionStrings:WhaleDeck"] = connection.ConnectionString;
        var logger = new ReportRetentionLogger();
        builder.Logging.AddProvider(logger);
        builder.Services.AddInfrastructure(builder.Configuration);
        builder.Services.AddSingleton<TimeProvider>(clock);
        builder.Services.AddHostedService<WorkspaceRunReportRetentionWorker>();
        using var host = builder.Build();
        using var testDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = testDeadline.Token;

        await using var blocker = new PlatformDbContext(options);
        await using var relationLock = await blocker.Database.BeginTransactionAsync(token);
        await blocker.Database.ExecuteSqlRawAsync("LOCK TABLE workspace_run_reports IN ACCESS EXCLUSIVE MODE", token);
        await host.StartAsync(token);
        try
        {
            // A real PostgreSQL command timeout must leave the worker alive and waiting.
            var retry = await clock.Delays.ReadAsync(token);
            Assert.Equal(TimeSpan.FromMinutes(1), retry.Delay);
            var warning = Assert.Single(logger.Warnings);
            Assert.Equal(1013, warning.EventId);
            Assert.Null(warning.Error);
            await relationLock.RollbackAsync(token);
            Assert.Equal(0, await Cleared());

            retry.Fire();
            var second = await clock.Delays.ReadAsync(token);
            Assert.Equal(TimeSpan.FromMinutes(1), second.Delay);
            Assert.Equal(50, await Cleared());

            second.Fire();
            var idle = await clock.Delays.ReadAsync(token);
            Assert.Equal(TimeSpan.FromMinutes(1), idle.Delay);
            Assert.Equal(51, await Cleared());
            var live = await db.WorkspaceRunReports.AsNoTracking()
                .SingleAsync(row => row.WorkspaceId == workspace && row.ExpiredAtUtc == null, token);
            Assert.True(live.RetainUntilUtc > now);
            Assert.Equal(body, live.JsonContent);
            Assert.Equal(body, live.MarkdownContent);
            Assert.Equal(51, await db.WorkspaceRunReports.CountAsync(row => row.WorkspaceId == workspace &&
                row.ExpiredAtUtc != null && row.JsonContent == null && row.MarkdownContent == null &&
                row.JsonSha256 == hash && row.MarkdownSha256 == hash, token));
            Assert.Single(logger.Warnings);
        }
        finally
        {
            await host.StopAsync(token);
        }
        Assert.False(clock.Delays.TryRead(out _));
        Console.WriteLine("INBOX_PHASE=workspace-report-worker-real-lock-retry-bounded-batch-preserve-live-stop");

        Task<int> Cleared() => db.WorkspaceRunReports.CountAsync(
            row => row.WorkspaceId == workspace && row.ExpiredAtUtc != null, token);
    }

    private sealed class ReportRetentionClock(DateTimeOffset now) : TimeProvider
    {
        private readonly Channel<ReportRetentionDelay> delays = Channel.CreateUnbounded<ReportRetentionDelay>();
        public ChannelReader<ReportRetentionDelay> Delays => delays.Reader;
        public override DateTimeOffset GetUtcNow() => now;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Assert.Equal(Timeout.InfiniteTimeSpan, period);
            var timer = new ReportRetentionDelay(callback, state, dueTime);
            Assert.True(delays.Writer.TryWrite(timer));
            return timer;
        }
    }

    private sealed class ReportRetentionDelay(TimerCallback callback, object? state, TimeSpan delay) : ITimer
    {
        private int disposed;
        public TimeSpan Delay => delay;
        public void Fire()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0) callback(state);
        }
        public bool Change(TimeSpan dueTime, TimeSpan period) => throw new NotSupportedException();
        public void Dispose() => Interlocked.Exchange(ref disposed, 1);
        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ReportRetentionLogger : ILoggerProvider, ILogger
    {
        public ConcurrentQueue<(int EventId, Exception? Error)> Warnings { get; } = new();
        public ILogger CreateLogger(string categoryName) =>
            categoryName == typeof(WorkspaceRunReportRetentionWorker).FullName
                ? this : Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
        public bool IsEnabled(LogLevel logLevel) => logLevel == LogLevel.Warning;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel)) Warnings.Enqueue((eventId.Id, exception));
        }
        public void Dispose() { }
    }
}
