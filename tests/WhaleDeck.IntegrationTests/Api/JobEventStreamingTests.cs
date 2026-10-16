using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using WhaleDeck.Api.Controllers;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.IntegrationTests.Api;

public sealed class JobEventStreamingTests
{
    private static readonly string[] StatusThenEventsTwice = ["status", "events", "status", "events"];
    private static readonly string[] StatusOnly = ["status"];
    [Fact]
    public async Task TerminalCommitAfterEventQueryIsObservedOnNextBatchNotClosedEarly()
    {
        var repository = new Repository(1) { State = "Running" };
        repository.AfterRead = () => { repository.State = "Succeeded"; repository.Events.Add(Event(2, "Succeeded")); };
        var service = new OperationService(repository);
        var before = Assert.IsType<WhaleDeck.Application.Models.JobEventBatchDto>(await service.EventBatchAsync(repository.Id, 0, default));
        Assert.False(before.IsTerminal); Assert.Single(before.Events);
        var after = Assert.IsType<WhaleDeck.Application.Models.JobEventBatchDto>(await service.EventBatchAsync(repository.Id, 1, default));
        Assert.True(after.IsTerminal); Assert.Equal("Succeeded", Assert.Single(after.Events).State);
        Assert.Equal(StatusThenEventsTwice, repository.Reads);
    }

    [Theory]
    [InlineData("Succeeded")]
    [InlineData("Failed")]
    [InlineData("Canceled")]
    [InlineData("RolledBack")]
    public async Task TerminalHistoryDrainsEveryBoundedPageBeforeClosing(string state)
    {
        var repository = new Repository(1005) { State = state };
        repository.Events[^1] = Event(1005, state);
        var controller = Controller(repository);
        await controller.Events(repository.Id, default);
        var rows = Rows(controller);
        Assert.Equal(1005, rows.Length);
        Assert.Equal(Enumerable.Range(1, 1005).Select(item => (long)item), rows.Select(item => item.GetProperty("sequence").GetInt64()));
        Assert.Equal(state, rows[^1].GetProperty("state").GetString());
        Assert.Equal(4, repository.Reads.Count(item => item == "events"));
        Assert.Equal("no-store", controller.Response.Headers.CacheControl);
        Assert.Equal("no", controller.Response.Headers["X-Accel-Buffering"]);
    }

    [Fact]
    public async Task ResumeOnlyReturnsUnseenEventsAndAtTailReturnsNoDuplicates()
    {
        var repository = new Repository(1005);
        var controller = Controller(repository, "1004");
        await controller.Events(repository.Id, default);
        Assert.Equal(1005, Assert.Single(Rows(controller)).GetProperty("sequence").GetInt64());
        var tail = Controller(repository, "1005");
        await tail.Events(repository.Id, default);
        Assert.Empty(Rows(tail));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("x")]
    [InlineData("")]
    [InlineData(" 1")]
    [InlineData("9223372036854775808")]
    public async Task InvalidCursorIsRejectedBeforeAnyRepositoryAccess(string cursor)
    {
        var repository = new Repository(1);
        await Assert.ThrowsAsync<ArgumentException>(() => Controller(repository, cursor).Events(repository.Id, default));
        Assert.Empty(repository.Reads);
    }

    [Fact]
    public async Task MissingJobIs404BeforeStartingStream()
    {
        var repository = new Repository(1) { Missing = true };
        var controller = Controller(repository);
        Assert.IsType<NotFoundResult>(await controller.Events(repository.Id, default));
        Assert.Null(controller.Response.ContentType); Assert.Empty(Rows(controller));
        Assert.Equal(StatusOnly, repository.Reads);
    }

    [Fact]
    public async Task RequestCancellationStopsReadsWithoutRestartingOperation()
    {
        var repository = new Repository(1);
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Controller(repository).Events(repository.Id, canceled.Token));
        Assert.Empty(repository.Reads);
    }

    private static JobsController Controller(Repository repository, string? cursor = null)
    {
        var context = new DefaultHttpContext(); context.Response.Body = new MemoryStream();
        if (cursor is not null) context.Request.Headers["Last-Event-ID"] = cursor;
        return new(new OperationService(repository), null!) { ControllerContext = new() { HttpContext = context } };
    }
    private static JsonElement[] Rows(JobsController controller) => Encoding.UTF8.GetString(((MemoryStream)controller.Response.Body).ToArray())
        .Split('\n').Where(line => line.StartsWith("data: ", StringComparison.Ordinal)).Select(line => JsonDocument.Parse(line[6..]).RootElement.Clone()).ToArray();
    private static OperationJobEvent Event(long sequence, string state = "Running") => new() { Sequence = sequence, State = state, Phase = state, MessageCode = "STREAM_TEST" };
    private sealed class Repository(int count) : IJobRepository
    {
        public Guid Id { get; } = Guid.NewGuid();
        public string State { get; set; } = "Succeeded";
        public bool Missing { get; init; }
        public Action? AfterRead { get; set; }
        public List<string> Reads { get; } = [];
        public List<OperationJobEvent> Events { get; } = Enumerable.Range(1, count).Select(index => Event(index)).ToList();
        public Task<OperationJob?> FindAsync(Guid id, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); Reads.Add("status");
            return Task.FromResult<OperationJob?>(Missing ? null : new() { Id = Id, State = State, JobType = "test", ActorSubject = "test", IdempotencyKey = "test" });
        }
        public Task<IReadOnlyCollection<OperationJobEvent>> ListEventsAsync(Guid id, long afterSequence, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); Reads.Add("events");
            IReadOnlyCollection<OperationJobEvent> rows = Events.Where(item => item.Sequence > afterSequence).Take(500).ToArray();
            var callback = AfterRead; AfterRead = null; callback?.Invoke(); return Task.FromResult(rows);
        }
        public Task<OperationJob> EnqueueAsync(OperationJob job, OutboxMessage outbox, AuditEvent audit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<OperationJob>> ListAsync(int take, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RequestCancellationAsync(Guid id, string actorSubject, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
