using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.UnitTests.Application;

public sealed class OperationServiceTests
{
    [Theory]
    [InlineData("start")]
    [InlineData("stop")]
    [InlineData("restart")]
    [InlineData("pull")]
    [InlineData("redeploy")]
    public async Task EveryComposeWriteRequiresConfirmationAndPlan(string action)
    {
        var service = new OperationService(new RecordingJobRepository());
        var command = new OperationCommand("compose-projects", action, "application:demo", "key-1", "{}", null, false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnqueueAsync("subject", "trace", command, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnqueueAsync("subject", "trace", command with { Confirmed = true }, default));
        var result = await service.EnqueueAsync("subject", "trace", command with { Confirmed = true, PlanHash = new string('a', 64) }, default);
        Assert.Equal("compose-projects." + action, result.JobType);
    }

    [Fact]
    public async Task DeleteRequiresConfirmationAndPlan()
    {
        var service = new OperationService(new RecordingJobRepository());
        var withoutConfirmation = new OperationCommand("containers", "delete", "container-1", "key-1", "{}", null, false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnqueueAsync("subject", "trace", withoutConfirmation, default));

        var withoutPlan = withoutConfirmation with { Confirmed = true };
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnqueueAsync("subject", "trace", withoutPlan, default));
    }

    [Fact]
    public async Task AcceptedOperationCreatesLinkedJobOutboxAndAudit()
    {
        var repository = new RecordingJobRepository();
        var service = new OperationService(repository);
        var command = new OperationCommand("containers", "restart", "container-1", "key-1", "{}", null, true);

        var result = await service.EnqueueAsync("subject", "trace", command, default);

        Assert.Equal("containers.restart", result.JobType);
        Assert.NotNull(repository.Job);
        Assert.Equal(repository.Job!.Id, repository.Audit!.JobId);
        Assert.Contains(repository.Job.Id.ToString("D"), repository.Outbox!.PayloadJson, StringComparison.Ordinal);
        Assert.Equal("trace", repository.Audit.TraceId);
    }

    [Theory]
    [InlineData("STOP")]
    [InlineData("Restart")]
    [InlineData("DELETE")]
    public async Task ActionCasingCannotBypassConfirmation(string action)
    {
        var service = new OperationService(new RecordingJobRepository());
        var command = new OperationCommand("CONTAINERS", action, "container-1", "key-1", "{}", null, false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnqueueAsync("subject", "trace", command, default));
    }

    [Fact]
    public async Task ContainerCreateRequiresPlan()
    {
        var service = new OperationService(new RecordingJobRepository());
        var command = new OperationCommand("containers", "create", "new", "key-1",
            "{\"parameters\":{\"image\":\"alpine:3.22\"}}", null, false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnqueueAsync("subject", "trace", command, default));
    }

    [Fact]
    public async Task ContainerUpdateRequiresConfirmationAndPlan()
    {
        var service = new OperationService(new RecordingJobRepository());
        var command = new OperationCommand("containers", "update", "container-1", "key-1",
            "{\"parameters\":{}}", null, false);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnqueueAsync("subject", "trace", command, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnqueueAsync("subject", "trace", command with { Confirmed = true }, default));
    }

    [Fact]
    public async Task ContainerRebuildRequiresConfirmationAndPlan()
    {
        var service = new OperationService(new RecordingJobRepository());
        var command = new OperationCommand("containers", "rebuild", "container-1", "key-1",
            "{\"parameters\":{}}", null, false);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnqueueAsync("subject", "trace", command, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnqueueAsync("subject", "trace", command with { Confirmed = true }, default));
    }

    [Theory]
    [InlineData("batch-start")]
    [InlineData("batch-stop")]
    [InlineData("batch-restart")]
    [InlineData("batch-delete")]
    public async Task ContainerBatchActionsRequireConfirmationAndPlan(string action)
    {
        var service = new OperationService(new RecordingJobRepository());
        var command = new OperationCommand("containers", action, "batch", "key-1",
            "{\"parameters\":{\"containerIds\":\"[\\\"container-1\\\"]\"}}", null, false);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnqueueAsync("subject", "trace", command, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnqueueAsync("subject", "trace", command with { Confirmed = true }, default));
    }

    [Theory]
    [InlineData("network-delete")]
    [InlineData("volume-delete")]
    public async Task DockerResourceDeletionRequiresConfirmationAndPlan(string action)
    {
        var service = new OperationService(new RecordingJobRepository());
        var command = new OperationCommand("docker", action, "resource-1", "key-1", "{\"parameters\":{}}", null, false);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnqueueAsync("subject", "trace", command, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnqueueAsync("subject", "trace", command with { Confirmed = true }, default));
    }

    [Fact]
    public async Task DatabasePrincipalDeletionRequiresConfirmationAndPlan()
    {
        var service = new OperationService(new RecordingJobRepository());
        var command = new OperationCommand("databases", "delete-principal", "database-platform.postgres", "key-1",
            "{\"parameters\":{\"principal\":\"temporary_user\"}}", null, false);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnqueueAsync("subject", "trace", command, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnqueueAsync("subject", "trace", command with { Confirmed = true }, default));
    }

    [Fact]
    public async Task BackupCancellationTargetsTheExistingJobEndpoint()
    {
        var service = new OperationService(new RecordingJobRepository());
        var command = new OperationCommand("backups", "cancel", "database-platform.postgres", "key-1",
            "{\"parameters\":{}}", null, false);

        await Assert.ThrowsAsync<ArgumentException>(() => service.EnqueueAsync("subject", "trace", command, default));
    }

    [Theory]
    [InlineData("password")]
    [InlineData("apiToken")]
    [InlineData("clientSecret")]
    [InlineData("credentialFile")]
    [InlineData("environment")]
    [InlineData("ENVIRONMENT")]
    public async Task JobParametersRejectSensitiveValues(string key)
    {
        var service = new OperationService(new RecordingJobRepository());
        var request = $"{{\"parameters\":{{\"{key}\":\"must-not-persist\"}}}}";
        var command = new OperationCommand("containers", "start", "container-1", "key-1", request, null, false);
        await Assert.ThrowsAsync<ArgumentException>(() => service.EnqueueAsync("subject", "trace", command, default));
    }

    private sealed class RecordingJobRepository : IJobRepository
    {
        public OperationJob? Job { get; private set; }
        public OutboxMessage? Outbox { get; private set; }
        public AuditEvent? Audit { get; private set; }

        public Task<OperationJob> EnqueueAsync(OperationJob job, OutboxMessage outbox, AuditEvent audit, CancellationToken cancellationToken)
        {
            Job = job;
            Outbox = outbox;
            Audit = audit;
            return Task.FromResult(job);
        }

        public Task<OperationJob?> FindAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<OperationJob?>(null);
        public Task<IReadOnlyCollection<OperationJob>> ListAsync(int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<OperationJob>>([]);
        public Task<IReadOnlyCollection<OperationJobEvent>> ListEventsAsync(Guid id, long afterSequence, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<OperationJobEvent>>([]);
        public Task RequestCancellationAsync(Guid id, string actorSubject, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
