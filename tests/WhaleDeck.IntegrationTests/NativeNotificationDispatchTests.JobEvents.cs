namespace WhaleDeck.IntegrationTests;

using Microsoft.EntityFrameworkCore;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Persistence;

public sealed partial class NativeNotificationDispatchTests
{
    private static async Task JobEventBoundaries(DbContextOptions<PlatformDbContext> options)
    {
        await using var reader = new PlatformDbContext(options);
        await using var writer = new PlatformDbContext(options);
        var job = new OperationJob { JobType = "native.event-stream", ActorSubject = "native-stream-admin", IdempotencyKey = Guid.NewGuid().ToString("N"), State = "Running" };
        writer.OperationJobs.Add(job);
        writer.OperationJobEvents.AddRange(Enumerable.Range(1, 1005).Select(index => new OperationJobEvent
        { JobId = job.Id, Sequence = index, State = "Running", Phase = "Working", MessageCode = "NATIVE_STREAM" }));
        await writer.SaveChangesAsync(); writer.ChangeTracker.Clear();
        var repository = new CommitAfterEventRead(new JobRepository(reader), async () =>
        {
            var current = await writer.OperationJobs.SingleAsync(item => item.Id == job.Id);
            current.State = "Succeeded"; current.Phase = "Completed"; current.CompletedAtUtc = DateTimeOffset.UtcNow;
            writer.OperationJobEvents.Add(new() { JobId = job.Id, Sequence = 1006, State = "Succeeded", Phase = "Completed", MessageCode = "NATIVE_STREAM_DONE" });
            await writer.SaveChangesAsync();
        });
        var service = new OperationService(repository);
        var first = Assert.IsType<WhaleDeck.Application.Models.JobEventBatchDto>(await service.EventBatchAsync(job.Id, 0, default));
        Assert.False(first.IsTerminal); Assert.Equal(500, first.Events.Count);
        var second = Assert.IsType<WhaleDeck.Application.Models.JobEventBatchDto>(await service.EventBatchAsync(job.Id, 500, default));
        Assert.True(second.IsTerminal); Assert.Equal(500, second.Events.Count);
        var third = Assert.IsType<WhaleDeck.Application.Models.JobEventBatchDto>(await service.EventBatchAsync(job.Id, 1000, default));
        Assert.True(third.IsTerminal); Assert.Equal(6, third.Events.Count); Assert.Equal("Succeeded", third.Events.Last().State);
        var tail = Assert.IsType<WhaleDeck.Application.Models.JobEventBatchDto>(await service.EventBatchAsync(job.Id, 1006, default));
        Assert.True(tail.IsTerminal); Assert.Empty(tail.Events);
        Assert.Equal(Enumerable.Range(1, 1006).Select(index => (long)index), first.Events.Concat(second.Events).Concat(third.Events).Select(item => item.Sequence));
        Console.WriteLine("INBOX_PHASE=job-terminal-commit-after-event-query-paging-resume");
    }

    private sealed class CommitAfterEventRead(IJobRepository inner, Func<Task> commit) : IJobRepository
    {
        private bool committed;
        public Task<OperationJob?> FindAsync(Guid id, CancellationToken cancellationToken) => inner.FindAsync(id, cancellationToken);
        public async Task<IReadOnlyCollection<OperationJobEvent>> ListEventsAsync(Guid id, long afterSequence, CancellationToken cancellationToken)
        {
            var rows = await inner.ListEventsAsync(id, afterSequence, cancellationToken);
            if (!committed) { committed = true; await commit(); }
            return rows;
        }
        public Task<OperationJob> EnqueueAsync(OperationJob job, OutboxMessage outbox, AuditEvent audit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<OperationJob>> ListAsync(int take, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RequestCancellationAsync(Guid id, string actorSubject, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
