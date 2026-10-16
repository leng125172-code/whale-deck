using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Notifications;
using WhaleDeck.Infrastructure.Persistence;
using WhaleDeck.Infrastructure.Secrets;

namespace WhaleDeck.IntegrationTests;

public sealed partial class NativeNotificationDispatchTests
{
    private static async Task PrivateBoundaries(DbContextOptions<PlatformDbContext> options, PlatformDbContext db)
    {
        var directory = Path.Combine(Path.GetTempPath(), "wd-private-boundaries-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var key = Path.Combine(directory, "key"); await File.WriteAllBytesAsync(key, RandomNumberGenerator.GetBytes(32));
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(key, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        try
        {
            var protector = new NotificationPrivateProtector(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:CapabilityKeyFile"] = key }).Build());
            var broker = new NotificationPrivateInputStore(); var staged = new MemoryOneTimeSecretStore(); var gateway = new BoundaryGateway();
            var repository = new NotificationPrivateRepository(db);
            NotificationPrivateWorkflow Service(PlatformDbContext context) => new(new NotificationPrivateRepository(context), gateway, staged, broker, protector, new NotificationPrivateValidator(), TimeProvider.System);
            NotificationContext Actor() => new("native-private-boundary-admin", true, "private-boundary-trace", null, Guid.NewGuid().ToString("D"));
            const string raw = "{\"target\":{\"provider\":\"Webhook\",\"url\":\"https://example.invalid/boundary\",\"allowedAddresses\":[]},\"credentialMode\":\"remove\"}";
            async Task<NotificationChannel> Channel()
            {
                var channel = new NotificationChannel { Name = "private-boundary-" + Guid.NewGuid().ToString("N"), Provider = "Webhook", DestinationReference = "legacy", IsEnabled = true };
                db.NotificationChannels.Add(channel); await db.SaveChangesAsync(); db.ChangeTracker.Clear(); return channel;
            }
            async Task<(NotificationChannel Channel, JobDto Job)> Enqueue()
            {
                var channel = await Channel(); var actor = Actor(); var input = await staged.StoreAsync(actor.Subject, raw, default);
                var plan = await Service(db).PlanAsync(actor, channel.Id, new(0, "apply", input.Token), default);
                return (channel, await Service(db).SubmitAsync(actor, channel.Id, new(plan.PlanHash, input.Token, true), default));
            }
            async Task Cancel(Guid id)
            {
                await using var context = new PlatformDbContext(options);
                await new JobRepository(context).RequestCancellationAsync(id, Actor().Subject, default);
            }
            NotificationPrivateDispatcher Dispatcher(INotificationRequestAuthorizer? authorizer = null) =>
                new(repository, gateway, broker, authorizer ?? new NativeAuthorizer(true), protector, TimeProvider.System);

            // The cancellation is committed after the first cancellation read,
            // but before the final transactional start gate.
            var canceled = await Enqueue();
            await Dispatcher(new BoundaryAuthorizer(() => Cancel(canceled.Job.Id))).TickAsync(default);
            var canceledOperation = await db.NotificationPrivateOperations.AsNoTracking().SingleAsync(item => item.Id == canceled.Job.Id);
            Assert.Equal("Canceled", canceledOperation.State); Assert.Null(canceledOperation.RootStartedAtUtc);
            Assert.Null(await broker.GetAsync(Actor().Subject, canceled.Job.Id, default)); Assert.Equal(0, gateway.Applies);
            Assert.Equal("Canceled", (await db.OperationJobs.AsNoTracking().SingleAsync(item => item.Id == canceled.Job.Id)).State);
            Console.WriteLine("INBOX_PHASE=private-cancel-before-final-start");

            // An interrupted operation with a durable start marker is never
            // called definitely canceled just because its caller later cancels.
            var unknown = await Enqueue();
            var original = Assert.IsType<NotificationPrivateOperation>(await repository.ClaimAsync(DateTimeOffset.UtcNow, default));
            Assert.Equal(unknown.Job.Id, original.Id);
            Assert.Equal(NotificationPrivateStartResult.Started, await repository.StartedAsync(original, DateTimeOffset.UtcNow, default));
            await Cancel(original.Id);
            await db.NotificationPrivateOperations.Where(item => item.Id == original.Id).ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.LeaseExpiresAtUtc, DateTimeOffset.UtcNow.AddSeconds(-1)).SetProperty(item => item.NextAttemptAtUtc, DateTimeOffset.UtcNow.AddSeconds(-1)));
            await Dispatcher().TickAsync(default);
            var uncertain = await db.NotificationPrivateOperations.AsNoTracking().SingleAsync(item => item.Id == original.Id);
            Assert.Equal("Unknown", uncertain.State); Assert.NotNull(await broker.GetAsync(Actor().Subject, uncertain.Id, default));
            var acknowledgeActor = Actor(); var early = new AcknowledgeNotificationPrivateCommand(uncertain.Id, uncertain.Version, true, true);
            Assert.False((await Service(db).StatusAsync(acknowledgeActor, unknown.Channel.Id, default))!.CanAcknowledgeUnknown);
            await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db).AcknowledgeAsync(acknowledgeActor, unknown.Channel.Id, early, default));
            await db.NotificationPrivateOperations.Where(item => item.Id == uncertain.Id).ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.RootStartedAtUtc, DateTimeOffset.UtcNow.AddMinutes(-2))
                .SetProperty(item => item.LeaseToken, Guid.NewGuid())
                .SetProperty(item => item.LeaseExpiresAtUtc, DateTimeOffset.UtcNow.AddMinutes(1)));
            Assert.False((await Service(db).StatusAsync(acknowledgeActor, unknown.Channel.Id, default))!.CanAcknowledgeUnknown);
            await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db).AcknowledgeAsync(acknowledgeActor, unknown.Channel.Id, early, default));
            await db.NotificationPrivateOperations.Where(item => item.Id == uncertain.Id).ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.LeaseToken, (Guid?)null).SetProperty(item => item.LeaseExpiresAtUtc, (DateTimeOffset?)null));
            Assert.True((await Service(db).StatusAsync(acknowledgeActor, unknown.Channel.Id, default))!.CanAcknowledgeUnknown);
            gateway.ReadFailure = new IOException("synthetic unavailable");
            await Assert.ThrowsAsync<IOException>(() => Service(db).AcknowledgeAsync(acknowledgeActor, unknown.Channel.Id, early, default));
            Assert.Equal("Unknown", (await db.NotificationPrivateOperations.AsNoTracking().SingleAsync(item => item.Id == uncertain.Id)).State);
            gateway.ReadFailure = null;

            async Task Acknowledge()
            {
                await using var context = new PlatformDbContext(options);
                await Service(context).AcknowledgeAsync(acknowledgeActor, unknown.Channel.Id, early, default);
            }
            await Task.WhenAll(Acknowledge(), Acknowledge(), Acknowledge());
            var reads = gateway.Reads; gateway.ReadFailure = new IOException("synthetic unavailable after accepted acknowledgement");
            await Service(db).AcknowledgeAsync(acknowledgeActor, unknown.Channel.Id, early, default);
            Assert.Equal(reads, gateway.Reads);
            await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db).AcknowledgeAsync(acknowledgeActor, unknown.Channel.Id, early with { ExpectedVersion = early.ExpectedVersion + 1 }, default));
            Assert.Equal(reads, gateway.Reads); gateway.ReadFailure = null;
            Assert.Equal(1, await db.AuditEvents.CountAsync(item => item.JobId == early.OperationId && item.Action == "notification.private.unknown-acknowledged"));
            var terminal = await db.OperationJobEvents.AsNoTracking().Where(item => item.JobId == early.OperationId).OrderByDescending(item => item.Sequence).FirstAsync();
            Assert.Equal("Failed", terminal.State); Assert.Equal("UnknownOutcomeAcknowledged", terminal.Phase);
            Assert.Equal(1, await db.OperationJobEvents.CountAsync(item => item.JobId == early.OperationId && item.MessageCode == "PRIVATE_UNKNOWN_ACKNOWLEDGED"));
            var blocked = await db.NotificationChannels.AsNoTracking().SingleAsync(item => item.Id == unknown.Channel.Id);
            Assert.False(blocked.IsEnabled); Assert.True(blocked.PrivateConfigurationBlocked);
            Console.WriteLine("INBOX_PHASE=private-unknown-lease-ack-replay-terminal-event");

            // Distinct connections compete for one lease. An expired owner,
            // including a caller with no token, cannot change its successor.
            var fenced = await Enqueue();
            async Task<NotificationPrivateOperation?> Claim()
            {
                await using var context = new PlatformDbContext(options);
                return await new NotificationPrivateRepository(context).ClaimAsync(DateTimeOffset.UtcNow, default);
            }
            var claims = await Task.WhenAll(Claim(), Claim());
            var oldOwner = Assert.Single(claims, item => item is not null)!;
            Assert.Equal(fenced.Job.Id, oldOwner.Id);
            var oldToken = oldOwner.LeaseToken; oldOwner.LeaseToken = null;
            Assert.False(await repository.FinishAsync(oldOwner, "Failed", "SYNTHETIC_NO_LEASE", null, default));
            oldOwner.LeaseToken = oldToken;
            await db.NotificationPrivateOperations.Where(item => item.Id == oldOwner.Id).ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.LeaseExpiresAtUtc, DateTimeOffset.UtcNow.AddSeconds(-1)).SetProperty(item => item.NextAttemptAtUtc, DateTimeOffset.UtcNow.AddSeconds(-1)));
            var currentOwner = Assert.IsType<NotificationPrivateOperation>(await Claim());
            Assert.NotEqual(oldOwner.LeaseToken, currentOwner.LeaseToken);
            Assert.Equal(NotificationPrivateStartResult.LeaseLost, await repository.StartedAsync(oldOwner, DateTimeOffset.UtcNow, default));
            Assert.False(await repository.FinishAsync(oldOwner, "Failed", "SYNTHETIC_OLD_LEASE", null, default));
            Assert.True(await repository.FinishAsync(currentOwner, "Canceled", "SYNTHETIC_VALID_OWNER", null, default));
            Assert.Equal(0, gateway.Applies);

            // A fresh DB clock is used even if a caller captured its timestamp
            // before its lease expired. Input expiry is checked at this gate too.
            var expired = await Enqueue();
            var expiring = Assert.IsType<NotificationPrivateOperation>(await Claim());
            await db.NotificationPrivateOperations.Where(item => item.Id == expiring.Id).ExecuteUpdateAsync(setters => setters.SetProperty(item => item.LeaseExpiresAtUtc, DateTimeOffset.UtcNow.AddSeconds(-1)));
            Assert.Equal(NotificationPrivateStartResult.LeaseLost, await repository.StartedAsync(expiring, DateTimeOffset.UtcNow.AddMinutes(-3), default));
            await db.NotificationPrivateOperations.Where(item => item.Id == expiring.Id).ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.LeaseExpiresAtUtc, DateTimeOffset.UtcNow.AddMinutes(1)).SetProperty(item => item.InputExpiresAtUtc, DateTimeOffset.UtcNow.AddSeconds(-1)));
            Assert.Equal(NotificationPrivateStartResult.InputExpired, await repository.StartedAsync(expiring, DateTimeOffset.UtcNow, default));
            Assert.True(await repository.FinishAsync(expiring, "Failed", "SYNTHETIC_EXPIRED_INPUT", null, default));
            Assert.Equal(expired.Job.Id, expiring.Id);

            async Task<(NotificationChannel Channel, NotificationPrivatePlanClaims Claims)> Plan()
            {
                var channel = await Channel(); var actor = Actor(); var input = await staged.StoreAsync(actor.Subject, raw, default);
                var plan = await Service(db).PlanAsync(actor, channel.Id, new(0, "apply", input.Token), default);
                return (channel, protector.Verify(plan.PlanHash));
            }
            var competing = await Plan();
            async Task<bool> Compete()
            {
                await using var context = new PlatformDbContext(options);
                try { await new NotificationPrivateRepository(context).EnqueueAsync(Actor(), Guid.NewGuid(), competing.Claims, new string('A', 64), default); return true; }
                catch (InvalidOperationException) { return false; }
            }
            Assert.Single(await Task.WhenAll(Compete(), Compete()), accepted => accepted);
            var winner = await db.NotificationPrivateOperations.AsNoTracking().SingleAsync(item => item.ChannelId == competing.Channel.Id);
            await Cancel(winner.Id); await Dispatcher().TickAsync(default);
            Assert.Equal("Canceled", (await db.OperationJobs.AsNoTracking().SingleAsync(item => item.Id == winner.Id)).State);

            var repeated = await Plan(); var replayActor = Actor(); var identity = Guid.NewGuid();
            async Task<JobDto> Repeat()
            {
                await using var context = new PlatformDbContext(options);
                return await new NotificationPrivateRepository(context).EnqueueAsync(replayActor, identity, repeated.Claims, new string('B', 64), default);
            }
            Assert.All(await Task.WhenAll(Repeat(), Repeat(), Repeat()), item => Assert.Equal(identity, item.Id));
            Assert.Equal(1, await db.NotificationPrivateOperations.CountAsync(item => item.ChannelId == repeated.Channel.Id));
            Assert.Equal(1, await db.AuditEvents.CountAsync(item => item.JobId == identity && item.Action == "notification.private.accepted"));
            Assert.Equal(1, (await db.NotificationChannels.AsNoTracking().SingleAsync(item => item.Id == repeated.Channel.Id)).Version);
            await Cancel(identity); await Dispatcher().TickAsync(default);
            Assert.Equal(0, gateway.Applies);
            Assert.False(await db.NotificationChannels.AnyAsync(item => item.Name.StartsWith("private-boundary-") && (item.IsEnabled || !item.PrivateConfigurationBlocked)));
            Console.WriteLine("INBOX_PHASE=private-fencing-expiry-concurrent-submit");
        }
        finally { File.Delete(key); Directory.Delete(directory); }
    }
    private sealed class BoundaryAuthorizer(Func<Task> beforeStart) : INotificationRequestAuthorizer
    {
        public async Task<bool> IsAdministratorAsync(string subject, CancellationToken cancellationToken) { await beforeStart(); return true; }
    }
    private sealed class BoundaryGateway : INotificationPrivateGateway
    {
        public int Applies, Reads; public Exception? ReadFailure;
        public Task<NotificationPrivateMetadata> InspectAsync(Guid channelId, CancellationToken cancellationToken) =>
            Task.FromResult(new NotificationPrivateMetadata("EMPTY", "wd-" + channelId.ToString("N"), null, false, false, false, false, null, true));
        public Task<NotificationPrivateMetadata> InspectReceiptAsync(Guid channelId, Guid operationId, CancellationToken cancellationToken)
        { Interlocked.Increment(ref Reads); return ReadFailure is null ? InspectAsync(channelId, cancellationToken) : Task.FromException<NotificationPrivateMetadata>(ReadFailure); }
        public Task<NotificationPrivateMetadata> ApplyAsync(NotificationPrivateUpdate update, CancellationToken cancellationToken)
        { Interlocked.Increment(ref Applies); throw new InvalidOperationException("No root mutation is expected in isolated boundary tests."); }
    }
}
