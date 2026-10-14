using System.Text.Json;
using Microsoft.Extensions.Configuration;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Notifications;
using WhaleDeck.Infrastructure.Secrets;

namespace WhaleDeck.UnitTests.Application;

public sealed class NotificationPrivateWorkflowTests : IDisposable
{
    private const string Input = "{\"target\":{\"provider\":\"Webhook\",\"url\":\"https://example.invalid/events\",\"allowedAddresses\":[]},\"credentialMode\":\"remove\"}";
    private readonly string _key = Path.Combine(Path.GetTempPath(), "wd-private-key-" + Guid.NewGuid().ToString("N"));
    private readonly Clock _clock = new();
    private readonly Repo _repo = new();
    private readonly Gateway _agent = new();
    private readonly MemoryOneTimeSecretStore _staged;
    private readonly NotificationPrivateInputStore _inputs;
    private readonly NotificationPrivateProtector _protector;
    private readonly NotificationPrivateWorkflow _workflow;
    public NotificationPrivateWorkflowTests()
    {
        File.WriteAllBytes(_key, System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        _protector = new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:CapabilityKeyFile"] = _key }).Build());
        _staged = new(_clock); _inputs = new(null, _clock);
        _workflow = new(_repo, _agent, _staged, _inputs, _protector, new NotificationPrivateValidator(), _clock);
    }
    private static NotificationContext Context(bool admin = true) => new("fixture-owner", admin, "trace", null, Guid.NewGuid().ToString("D"));

    [Fact]
    public async Task ExactDurableReplayWorksAfterPlanAndInputExpire()
    {
        var context = Context(); var ticket = await _staged.StoreAsync(context.Subject, Input, default);
        var plan = await _workflow.PlanAsync(context, _repo.Channel.Id, new(0, "apply", ticket.Token), default);
        var command = new NotificationPrivateSubmitCommand(plan.PlanHash, ticket.Token, true);
        var job = await _workflow.SubmitAsync(context, _repo.Channel.Id, command, default);
        Assert.Null(await _staged.PeekAsync(context.Subject, ticket.Token, default));
        _clock.Advance(TimeSpan.FromMinutes(6));
        Assert.Equal(job.Id, (await _workflow.SubmitAsync(context, _repo.Channel.Id, command, default)).Id);
        Assert.Equal(1, _repo.Enqueued); Assert.Equal(0, _agent.Applies);
        Assert.DoesNotContain("example.invalid", JsonSerializer.Serialize(job));
    }

    [Fact]
    public async Task OwnerChannelConfirmationKeyAndInputAreBound()
    {
        var context = Context(); var ticket = await _staged.StoreAsync(context.Subject, Input, default);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _workflow.PlanAsync(context with { IsAdministrator = false }, _repo.Channel.Id, new(0, "apply", ticket.Token), default));
        var plan = await _workflow.PlanAsync(context, _repo.Channel.Id, new(0, "apply", ticket.Token), default);
        await Assert.ThrowsAsync<ArgumentException>(() => _workflow.SubmitAsync(context, _repo.Channel.Id, new(plan.PlanHash, ticket.Token), default));
        await Assert.ThrowsAsync<ArgumentException>(() => _workflow.SubmitAsync(context with { IdempotencyKey = null }, _repo.Channel.Id, new(plan.PlanHash, ticket.Token, true), default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _workflow.SubmitAsync(context with { Subject = "other-owner" }, _repo.Channel.Id, new(plan.PlanHash, ticket.Token, true), default));
        var different = await _staged.StoreAsync(context.Subject, Input.Replace("/events", "/different", StringComparison.Ordinal), default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _workflow.SubmitAsync(context, _repo.Channel.Id, new(plan.PlanHash, different.Token, true), default));
        Assert.Equal(0, _repo.Enqueued);
    }

    [Fact]
    public async Task BrokerOwnerFreezeTtlAndDeleteAreEnforced()
    {
        var id = Guid.NewGuid(); await _inputs.PutAsync("owner", id, Input, _clock.GetUtcNow().AddMinutes(5), default);
        Assert.Null(await _inputs.GetAsync("other", id, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _inputs.PutAsync("owner", id, "different", _clock.GetUtcNow().AddMinutes(4), default));
        _clock.Advance(TimeSpan.FromMinutes(6)); Assert.Null(await _inputs.GetAsync("owner", id, default));
        await Assert.ThrowsAsync<ArgumentException>(() => _inputs.PutAsync("owner", id, Input, _clock.GetUtcNow().AddMinutes(6), default));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ReceiptRecoveryPrecedesPermissionAndDoesNotNeedPrivateInput(bool current)
    {
        _repo.Operation = Operation(); _agent.Receipt = Metadata(_repo.Operation.Id, current);
        var dispatcher = new NotificationPrivateDispatcher(_repo, _agent, _inputs, new Authorizer(false), _protector, _clock);
        Assert.True(await dispatcher.TickAsync(default));
        Assert.Equal(current ? "Succeeded" : "Superseded", _repo.Finished); Assert.Equal(0, _agent.Applies);
    }

    [Fact]
    public async Task MissingInputAfterStartedNeverBecomesDefiniteNotApplied()
    {
        _repo.Operation = Operation(started: true);
        Assert.True(await new NotificationPrivateDispatcher(_repo, _agent, _inputs, new Authorizer(true), _protector, _clock).TickAsync(default));
        Assert.Equal("Unknown", _repo.Finished); Assert.Equal(0, _agent.Applies);
    }

    [Fact]
    public async Task LostReplyDefersSameOperationAndRecoversWithoutSecondApply()
    {
        _repo.Operation = Operation(); var operation = _repo.Operation;
        await _inputs.PutAsync(operation.ActorSubject, operation.Id, Input, _clock.GetUtcNow().AddMinutes(5), default);
        _agent.LoseReply = true;
        var dispatcher = new NotificationPrivateDispatcher(_repo, _agent, _inputs, new Authorizer(true), _protector, _clock);
        await dispatcher.TickAsync(default); Assert.Equal(1, _agent.Applies); Assert.Equal(1, _repo.Deferred);
        await dispatcher.TickAsync(default); Assert.Equal(1, _agent.Applies); Assert.Equal("Succeeded", _repo.Finished);
    }

    [Fact]
    public void SignatureTamperingAndDuplicateOrUnknownFieldsAreRejected()
    {
        var claims = new NotificationPrivatePlanClaims("owner", _repo.Channel.Id, 0, "Webhook", "remove", "EMPTY", new string('A', 64), _clock.GetUtcNow());
        var signed = _protector.Sign(claims);
        Assert.Throws<ArgumentException>(() => _protector.Verify(signed[..^1] + (signed[^1] == 'A' ? 'B' : 'A')));
        var validator = new NotificationPrivateValidator();
        Assert.Throws<ArgumentException>(() => validator.Validate("Webhook", "apply", "{\"target\":{},\"target\":{},\"path\":\"/etc/passwd\"}", false));
    }
    private NotificationPrivateOperation Operation(bool started = false) => new() { Id = Guid.NewGuid(), ChannelId = _repo.Channel.Id, ActorSubject = "fixture-owner", RequestHash = new string('A', 64),
        InputFingerprint = _protector.Fingerprint("fixture-owner", _repo.Channel.Id, "apply", Input), RegistryRevision = "EMPTY", Provider = "Webhook", Action = "apply",
        InputExpiresAtUtc = _clock.GetUtcNow().AddMinutes(5), RootStartedAtUtc = started ? _clock.GetUtcNow().AddMinutes(-1) : null };
    private NotificationPrivateMetadata Metadata(Guid? id = null, bool current = true) => new("EMPTY", "wd-" + _repo.Channel.Id.ToString("N"), null, id != null, false, id != null, false, id, current);
    public void Dispose() { File.Delete(_key); GC.SuppressFinalize(this); }
    private sealed class Clock : TimeProvider { private DateTimeOffset _now = DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow() => _now; public void Advance(TimeSpan span) => _now += span; }
    private sealed class Authorizer(bool allowed) : INotificationRequestAuthorizer { public Task<bool> IsAdministratorAsync(string subject, CancellationToken cancellationToken) => Task.FromResult(allowed); }
    private sealed class Gateway : INotificationPrivateGateway
    {
        public int Applies; public bool LoseReply; public NotificationPrivateMetadata? Receipt;
        public Task<NotificationPrivateMetadata> InspectAsync(Guid channelId, CancellationToken cancellationToken) => Task.FromResult(new NotificationPrivateMetadata("EMPTY", "wd-" + channelId.ToString("N"), null, false, false, false, false, null, true));
        public Task<NotificationPrivateMetadata> InspectReceiptAsync(Guid channelId, Guid operationId, CancellationToken cancellationToken) => Receipt is null ? InspectAsync(channelId, cancellationToken) : Task.FromResult(Receipt);
        public Task<NotificationPrivateMetadata> ApplyAsync(NotificationPrivateUpdate update, CancellationToken cancellationToken)
        { Applies++; Receipt = new("EMPTY", "wd-" + update.ChannelId.ToString("N"), null, true, false, true, false, update.OperationId, true); return LoseReply ? Task.FromException<NotificationPrivateMetadata>(new IOException("fixture reply lost")) : Task.FromResult(Receipt); }
    }
    private sealed class Repo : INotificationPrivateRepository
    {
        public NotificationChannel Channel = new() { Name = "fixture", Provider = "Webhook", DestinationReference = "approved" };
        public NotificationPrivateOperation? Operation; public int Enqueued, Deferred; public string? Finished;
        private Guid _id; private string? _request;
        public Task<NotificationChannel> ChannelAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Channel);
        public Task<JobDto?> ReplayAsync(Guid id, string actor, string requestHash, CancellationToken cancellationToken) => Task.FromResult<JobDto?>(_id == id && _request == requestHash ? Job(id) : null);
        public Task<JobDto> EnqueueAsync(NotificationContext context, Guid id, NotificationPrivatePlanClaims claims, string requestHash, CancellationToken cancellationToken) { Enqueued++; _id = id; _request = requestHash; return Task.FromResult(Job(id)); }
        private static JobDto Job(Guid id) => new(id, "notifications.private-configuration", "Queued", "Queued", null, null, DateTimeOffset.UtcNow, null, null);
        public Task<NotificationPrivateOperation?> ClaimAsync(DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult(Operation);
        public Task<bool> StartedAsync(NotificationPrivateOperation operation, DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task FinishAsync(NotificationPrivateOperation operation, string state, string code, NotificationPrivateMetadata? receipt, CancellationToken cancellationToken) { Finished = state; return Task.CompletedTask; }
        public Task DeferAsync(NotificationPrivateOperation operation, string code, CancellationToken cancellationToken) { Deferred++; return Task.CompletedTask; }
        public Task<bool> CancellationRequestedAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<NotificationPrivateOperation?> LatestAsync(Guid channelId, CancellationToken cancellationToken) => Task.FromResult(Operation);
        public Task AcknowledgeAsync(NotificationContext context, Guid channelId, Guid operationId, long expectedVersion, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
