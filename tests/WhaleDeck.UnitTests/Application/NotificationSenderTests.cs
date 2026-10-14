using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.UnitTests.Application;

public sealed class NotificationSenderTests
{
    [Theory]
    [InlineData(NotificationTransportResult.Accepted, "Succeeded")]
    [InlineData(NotificationTransportResult.Rejected, "Failed")]
    [InlineData(NotificationTransportResult.Unknown, "Unknown")]
    [InlineData(NotificationTransportResult.RetryableNotSent, "RetryWait")]
    public async Task TransportIsInvokedOnlyAfterDurableBeginAndResultsArePersisted(NotificationTransportResult result, string state)
    {
        var clock = new Clock(); var repository = new Repository(clock); var transport = new Transport(repository) { Result = result };
        Assert.Equal(1, await new NotificationSender(repository, transport, clock).TickAsync(default));
        Assert.Equal(state, repository.Item.State); Assert.Equal(1, repository.Item.AttemptCount); Assert.Equal(1, transport.Calls);
        Assert.Equal(["Begin", "Transport", "Finish"], repository.Order);
    }

    [Fact]
    public async Task UnknownOrLostCompletionNeverAutomaticallySendsTwice()
    {
        var clock = new Clock(); var repository = new Repository(clock) { LoseCompletion = true }; var transport = new Transport(repository);
        var sender = new NotificationSender(repository, transport, clock);
        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.TickAsync(default));
        Assert.Equal("Sending", repository.Item.State);
        clock.Advance(TimeSpan.FromMinutes(2)); repository.LoseCompletion = false;
        Assert.Equal(0, await sender.TickAsync(default)); Assert.Equal("Unknown", repository.Item.State); Assert.Equal(1, transport.Calls);
    }

    [Fact]
    public async Task OnlyDefiniteNotSentRetriesWithBoundAndDelay()
    {
        var clock = new Clock(); var repository = new Repository(clock); var transport = new Transport(repository) { Result = NotificationTransportResult.RetryableNotSent };
        var sender = new NotificationSender(repository, transport, clock);
        await sender.TickAsync(default);
        Assert.Equal(0, await sender.TickAsync(default)); Assert.Equal(1, transport.Calls);
        for (var index = 0; index < 4; index++) { clock.Advance(TimeSpan.FromHours(1)); await sender.TickAsync(default); }
        Assert.Equal("Failed", repository.Item.State); Assert.Equal(5, repository.Item.AttemptCount); Assert.Equal(5, transport.Calls);
        clock.Advance(TimeSpan.FromHours(1)); Assert.Equal(0, await sender.TickAsync(default)); Assert.Equal(5, transport.Calls);
    }

    [Fact]
    public async Task RotatedConfigurationNeverUsesNewTargetForOldDelivery()
    {
        var clock = new Clock(); var repository = new Repository(clock, new string('B', 64)); var transport = new Transport(repository);
        Assert.Equal(0, await new NotificationSender(repository, transport, clock).TickAsync(default));
        Assert.Equal("ConfigurationChanged", repository.Item.LastResultCode); Assert.Equal("Canceled", repository.Item.State);
        Assert.Equal(0, transport.Calls); Assert.Equal(0, repository.Item.AttemptCount);
    }

    [Fact]
    public async Task InvalidBindingAndPersonalAudienceNeverTransmit()
    {
        var clock = new Clock(); var repository = new Repository(clock); var transport = new Transport(repository) { Invalid = true };
        await new NotificationSender(repository, transport, clock).TickAsync(default);
        Assert.Equal("ConfigurationMissing", repository.Item.LastResultCode); Assert.Equal(0, transport.Calls);
        repository = new Repository(clock) { Personal = true }; transport = new Transport(repository);
        await new NotificationSender(repository, transport, clock).TickAsync(default);
        Assert.Equal("EventUnavailable", repository.Item.LastResultCode); Assert.Equal(0, transport.Calls);
    }

    [Fact]
    public async Task TransportCancellationIsUnknownAndCompletionHasIndependentBudget()
    {
        using var stopping = new CancellationTokenSource();
        var clock = new Clock(); var repository = new Repository(clock); var transport = new Transport(repository)
        { OnSend = () => { stopping.Cancel(); throw new OperationCanceledException(); } };
        await new NotificationSender(repository, transport, clock).TickAsync(stopping.Token);
        Assert.Equal("Unknown", repository.Item.State); Assert.False(repository.CompletionTokenCanceled); Assert.Equal(1, transport.Calls);
    }

    [Fact]
    public async Task PreSendDeniedNeverCallsTransport()
    {
        var clock = new Clock(); var repository = new Repository(clock) { DenyBegin = true }; var transport = new Transport(repository);
        await new NotificationSender(repository, transport, clock).TickAsync(default);
        Assert.Equal(0, transport.Calls); Assert.Equal(0, repository.Item.AttemptCount);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task ExplicitIntentsRequireExecutionTimeAdministratorAndBindOnlyTest(bool administrator)
    {
        var clock = new Clock(); var repository = new Repository(clock, intentKind: "Test", requester: "requester", unbound: true);
        var transport = new Transport(repository); var authorizer = new Authorizer(administrator);
        await new NotificationSender(repository, transport, clock, authorizer).TickAsync(default);
        Assert.Equal(1, authorizer.Calls); Assert.Equal(administrator ? 1 : 0, transport.Calls); Assert.Equal(administrator ? 1 : 0, repository.Bindings);
        if (!administrator) Assert.Equal("PermissionRevoked", repository.Item.LastResultCode);
    }
    [Fact]
    public async Task MissingRequestAuthorizerFailsClosed()
    {
        var clock = new Clock(); var repository = new Repository(clock, intentKind: "ManualRetry", requester: "requester"); var transport = new Transport(repository);
        await new NotificationSender(repository, transport, clock).TickAsync(default);
        Assert.Equal(0, transport.Calls); Assert.Equal("PermissionRevoked", repository.Item.LastResultCode);
    }
    private sealed class Authorizer(bool administrator) : INotificationRequestAuthorizer
    {
        public int Calls { get; private set; }
        public Task<bool> IsAdministratorAsync(string subject, CancellationToken cancellationToken) { Calls++; return Task.FromResult(administrator); }
    }
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset current = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => current;
        public void Advance(TimeSpan duration) => current += duration;
    }
    private sealed class Repository(Clock clock, string? revision = null, string intentKind = "Automatic", string? requester = null, bool unbound = false) : INotificationDispatchRepository
    {
        public List<string> Order { get; } = [];
        public bool LoseCompletion { get; set; }
        public bool DenyBegin { get; set; }
        public bool Personal { get; set; }
        public bool CompletionTokenCanceled { get; private set; }
        private readonly NotificationChannel channel = new() { Name = "test", Provider = "Webhook", DestinationReference = "test", IsEnabled = true };
        public NotificationDelivery Item { get; } = new() { NotificationId = Guid.NewGuid(), ChannelId = Guid.NewGuid(), Provider = "Webhook", DestinationReference = "test", ConfigurationRevision = unbound ? null : revision ?? new string('A', 64), IntentKind = intentKind, RequestedBySubject = requester, CreatedAtUtc = clock.GetUtcNow(), NextAttemptAtUtc = clock.GetUtcNow() };
        public int Bindings { get; private set; }
        public Task<bool> BindTestAsync(Guid id, Guid token, string configurationRevision, DateTimeOffset now, CancellationToken cancellationToken)
        { Bindings++; return Task.FromResult(Item.IntentKind == "Test" && Item.State == "Leased" && Item.LeaseToken == token); }
        public Task<IReadOnlyCollection<NotificationChannel>> EnabledChannelsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<NotificationChannel>>([channel]);
        public Task<int> RouteBatchAsync(DateTimeOffset now, CancellationToken cancellationToken, IReadOnlyDictionary<Guid, string>? revisions = null) => Task.FromResult(0);
        public Task<NotificationDelivery?> ClaimAsync(DateTimeOffset now, CancellationToken cancellationToken)
        {
            if (Item.State is not ("Pending" or "RetryWait") || Item.NextAttemptAtUtc > now) return Task.FromResult<NotificationDelivery?>(null);
            Item.State = "Leased"; Item.LeaseToken = Guid.NewGuid(); Item.LeaseExpiresAtUtc = now.AddSeconds(30); return Task.FromResult<NotificationDelivery?>(Item);
        }
        public Task<NotificationEvent?> MessageAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<NotificationEvent?>(new()
        { Id = id, SourceKey = "test-event", SourceKind = "Alert", Audience = Personal ? "Subject" : "Administrator", RecipientSubject = Personal ? "private-actor" : null, MessageCode = "ALERT_ACTIVE", Severity = "Error", OccurredAtUtc = clock.GetUtcNow(), ExpiresAtUtc = clock.GetUtcNow().AddDays(1) });
        public Task<bool> BeginSendAsync(Guid id, Guid token, DateTimeOffset now, CancellationToken cancellationToken, string? configurationRevision = null, int minimumIntervalSeconds = 0, string? targetIdentity = null)
        { if (DenyBegin) return Task.FromResult(false); Order.Add("Begin"); return Task.FromResult(NotificationDispatchPolicy.BeginSend(Item, token, now)); }
        public Task<bool> FinishAsync(Guid id, Guid token, DateTimeOffset now, NotificationTransportResult result, CancellationToken cancellationToken)
        { if (LoseCompletion) throw new InvalidOperationException("Simulated unavailable persistence"); CompletionTokenCanceled = cancellationToken.IsCancellationRequested; Order.Add("Finish"); return Task.FromResult(NotificationDispatchPolicy.Finish(Item, token, now, result)); }
        public Task<bool> RejectBeforeSendAsync(Guid id, Guid token, DateTimeOffset now, string code, CancellationToken cancellationToken)
        { Item.State = code is "ConfigurationChanged" or "EventUnavailable" ? "Canceled" : "Failed"; Item.LastResultCode = code; Item.LeaseToken = null; Item.LeaseExpiresAtUtc = null; return Task.FromResult(true); }
        public Task<int> RecoverBatchAsync(DateTimeOffset now, CancellationToken cancellationToken)
        { NotificationDispatchPolicy.Recover(Item, now, true, true); return Task.FromResult(0); }
    }
    private sealed class Transport(Repository repository) : INotificationTransport
    {
        public NotificationTransportResult Result { get; init; } = NotificationTransportResult.Accepted;
        public bool Invalid { get; init; }
        public Action? OnSend { get; init; }
        public int Calls { get; private set; }
        public Task<INotificationPreparedSend> PrepareAsync(string provider, string destinationReference, string? credentialReference, CancellationToken cancellationToken)
        { if (Invalid) throw new NotificationConfigurationException("ConfigurationMissing"); return Task.FromResult<INotificationPreparedSend>(new Prepared(this, repository)); }
        private sealed class Prepared(Transport transport, Repository repository) : INotificationPreparedSend
        {
            public string ConfigurationRevision => new('A', 64);
            public string TargetIdentity => new('C', 64);
            public int MinimumIntervalSeconds => 5;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
            public Task<NotificationTransportResult> SendAsync(Guid deliveryId, NotificationEvent message, CancellationToken cancellationToken)
            { transport.Calls++; repository.Order.Add("Transport"); transport.OnSend?.Invoke(); return Task.FromResult(transport.Result); }
        }
    }
}
