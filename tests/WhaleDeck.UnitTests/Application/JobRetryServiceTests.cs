using System.Reflection;
using System.Text.Json;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.UnitTests.Application;

public sealed class JobRetryServiceTests
{
    [Theory]
    [InlineData("files.upload")]
    [InlineData("containers.files.upload")]
    [InlineData("containers.files.restore")]
    public async Task ByteUploadsRequireNewFileSelectionAndCannotUseGenericRetry(string jobType)
    {
        var repository = new Repository(CreateSource("Failed", jobType)); var service = Service(repository);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetContextAsync(repository.Source.Id, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PlanAsync(repository.Source.Id, null, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RetryAsync(repository.Source.Id, "admin", "trace", "new-key", null, null, true, default));
        Assert.Null(repository.Enqueued); Assert.Null(repository.Outbox); Assert.Null(repository.Audit);
    }
    [Theory]
    [InlineData("Queued")]
    [InlineData("Running")]
    [InlineData("Succeeded")]
    public async Task LiveAndSuccessfulTasksCannotBeRetried(string state)
    {
        var repository = new Repository(CreateSource(state));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(repository).GetContextAsync(repository.Source.Id, default));
        Assert.Null(repository.Enqueued);
    }

    [Fact]
    public async Task MissingTaskReturnsNotFound()
    {
        var repository = new Repository(CreateSource("Failed"));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Service(repository).GetContextAsync(Guid.NewGuid(), default));
    }

    [Theory]
    [InlineData("Failed")]
    [InlineData("Canceled")]
    [InlineData("RolledBack")]
    public async Task RetryCreatesNewLinkedJobWithoutMutatingSource(string state)
    {
        var source = CreateSource(state);
        var originalRequest = source.RequestJson;
        var repository = new Repository(source);
        var result = await Service(repository).RetryAsync(source.Id, "current-admin", "new-trace", "retry-key", null, null, true, default);

        Assert.NotEqual(source.Id, result.Id);
        Assert.Equal("Queued", result.State);
        Assert.Equal(state, source.State);
        Assert.Equal(originalRequest, source.RequestJson);
        Assert.Equal("current-admin", repository.Enqueued!.ActorSubject);
        Assert.Null(repository.Enqueued.AgentOperationId);
        Assert.Equal("retry-key", repository.Enqueued.IdempotencyKey);
        using var audit = JsonDocument.Parse(repository.Audit!.DetailJson!);
        Assert.Equal(source.Id, audit.RootElement.GetProperty("retriedFromJobId").GetGuid());
        Assert.Contains(result.Id.ToString("D"), repository.Outbox!.PayloadJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RetryNeedsExplicitConfirmation()
    {
        var repository = new Repository(CreateSource("Failed"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(repository).RetryAsync(repository.Source.Id,
            "admin", "trace", "key", null, null, false, default));
        Assert.Null(repository.Enqueued);
    }

    [Fact]
    public async Task OldPlanCannotSubstituteForNewPreflight()
    {
        var repository = new Repository(CreateSource("Failed", "containers.delete"));
        var gateway = DispatchProxy.Create<IAgentGateway, PlannerProxy>();
        var proxy = (PlannerProxy)(object)gateway;
        var service = new JobRetryService(repository, gateway, new OperationService(repository));

        var preview = await service.PlanAsync(repository.Source.Id, null, default);
        Assert.Equal("fresh-plan", preview.Plan!.PlanHash);
        Assert.Equal("containers", proxy.Area);
        Assert.Equal("delete", proxy.Action);
        Assert.Equal("container-1", proxy.ResourceId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RetryAsync(repository.Source.Id,
            "admin", "trace", "key", null, null, true, default));
        var job = await service.RetryAsync(repository.Source.Id, "admin", "trace", "key", preview.Plan.PlanHash, null, true, default);
        Assert.Equal("containers.delete", job.JobType);
        Assert.DoesNotContain("old-plan", repository.Enqueued!.RequestJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConsumedInputMustBeRestagedAndContextNeverExposesTicket()
    {
        var source = CreateSource("Failed", "databases.rotate",
            "{\"resourceId\":\"database-platform.postgres\",\"parameters\":{\"principal\":\"example\",\"inputTicket\":\"old-ticket\",\"inputKind\":\"password\"},\"planHash\":\"old-plan\"}");
        var repository = new Repository(source);
        var service = Service(repository);
        var context = await service.GetContextAsync(source.Id, default);
        Assert.True(context.RequiresInput);
        Assert.Equal("password", context.InputKind);
        Assert.DoesNotContain("old-ticket", JsonSerializer.Serialize(context), StringComparison.Ordinal);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RetryAsync(source.Id, "admin", "trace", "key", null, null, true, default));
        await service.RetryAsync(source.Id, "admin", "trace", "key", null, "new-ticket", true, default);
        Assert.Contains("new-ticket", repository.Enqueued!.RequestJson, StringComparison.Ordinal);
        Assert.DoesNotContain("old-ticket", repository.Enqueued.RequestJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OriginalIdempotencyKeyCannotBeReused()
    {
        var repository = new Repository(CreateSource("Failed"));
        await Assert.ThrowsAsync<ArgumentException>(() => Service(repository).RetryAsync(repository.Source.Id,
            "admin", "trace", repository.Source.IdempotencyKey, null, null, true, default));
        Assert.Null(repository.Enqueued);
    }

    [Fact]
    public async Task ReplacementInputCannotBeInjectedIntoOtherOperations()
    {
        var repository = new Repository(CreateSource("Failed"));
        await Assert.ThrowsAsync<ArgumentException>(() => Service(repository).RetryAsync(repository.Source.Id,
            "admin", "trace", "key", null, "unexpected-ticket", true, default));
    }

    private static JobRetryService Service(Repository repository) => new(repository, null!, new OperationService(repository));
    private static OperationJob CreateSource(string state, string type = "containers.restart", string? request = null) => new()
    {
        JobType = type, ActorSubject = "old-admin", IdempotencyKey = "old-key", State = state,
        AgentOperationId = Guid.NewGuid(),
        RequestJson = request ?? "{\"resourceId\":\"container-1\",\"parameters\":{},\"planHash\":\"old-plan\"}"
    };

    public class PlannerProxy : DispatchProxy
    {
        public string? Area { get; private set; }
        public string? Action { get; private set; }
        public string? ResourceId { get; private set; }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name != nameof(IAgentGateway.PlanAsync) || args is null) throw new NotSupportedException();
            Area = (string)args[0]!;
            Action = (string)args[1]!;
            ResourceId = (string?)args[2];
            return Task.FromResult(new PlanDto("fresh-plan", DateTimeOffset.UtcNow.AddMinutes(5), ["current change"], []));
        }
    }

    private sealed class Repository(OperationJob source) : IJobRepository
    {
        public OperationJob Source { get; } = source;
        public OperationJob? Enqueued { get; private set; }
        public AuditEvent? Audit { get; private set; }
        public OutboxMessage? Outbox { get; private set; }
        public Task<OperationJob> EnqueueAsync(OperationJob job, OutboxMessage outbox, AuditEvent audit, CancellationToken cancellationToken)
        {
            Enqueued = job; Audit = audit; Outbox = outbox;
            return Task.FromResult(job);
        }
        public Task<OperationJob?> FindAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(id == Source.Id ? Source : null);
        public Task<IReadOnlyCollection<OperationJob>> ListAsync(int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<OperationJob>>([Source]);
        public Task<IReadOnlyCollection<OperationJobEvent>> ListEventsAsync(Guid id, long afterSequence, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<OperationJobEvent>>([]);
        public Task RequestCancellationAsync(Guid id, string actorSubject, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
