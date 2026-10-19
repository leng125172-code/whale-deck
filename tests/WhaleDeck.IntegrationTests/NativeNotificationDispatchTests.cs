using System.Text.RegularExpressions;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Npgsql;
using WhaleDeck.Application.Services;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Persistence;
using WhaleDeck.Infrastructure.Notifications;

namespace WhaleDeck.IntegrationTests;

public sealed partial class NativeNotificationDispatchTests
{
    private static readonly string[] LoopbackAddresses = ["127.0.0.1"];
    private static readonly string[] CollectorRecipients = ["collector@example.invalid"];
    [NativePostgresFact]
    public async Task RealPostgresLateCommitFanoutLeaseRecoveryVersionAndRetention()
    {
        var connection = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("WHALEDECK_NATIVE_TEST_CONNECTION"));
        if (connection.Database is null || !Regex.IsMatch(connection.Database, "^wdv_dispatch_[a-f0-9]{16}$"))
            throw new InvalidOperationException("Native dispatch test requires its isolated database namespace.");
        var options = new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(connection.ConnectionString).Options;
        await using var db = new PlatformDbContext(options);
        Assert.False(await db.Database.SqlQueryRaw<bool>("SELECT rolsuper AS \"Value\" FROM pg_roles WHERE rolname = current_user").SingleAsync());
        await db.GetService<IMigrator>().MigrateAsync("20261016034243_TerminalSessionLifecycle");
        var upgradeId = Guid.NewGuid(); var upgradeAt = DateTimeOffset.UtcNow; var upgradeHash = new string('A', 64); var upgradeKey = Guid.NewGuid().ToString("D");
        // Seed a real pre-extension row without referencing any new columns.
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO terminal_sessions (id,actor_subject,login_binding,idempotency_key,grant_hash,target_id,state,created_at_utc,grant_expires_at_utc,expires_at_utc,closed_at_utc,version) VALUES ({upgradeId},'native-upgrade',{upgradeHash},{upgradeKey},{upgradeHash},'whaledeck.host','Closed',{upgradeAt},{upgradeAt.AddSeconds(30)},{upgradeAt.AddMinutes(10)},{upgradeAt},0)");
        await db.GetService<IMigrator>().MigrateAsync("20261016122353_FileUploadWorkflow");
        var uploadUpgradeId = Guid.NewGuid();
        db.OperationJobs.Add(new() { Id = uploadUpgradeId, JobType = "files.upload", ActorSubject = "native-upgrade", IdempotencyKey = Guid.NewGuid().ToString("D") });
        db.FileUploadOperations.Add(new() { Id = uploadUpgradeId, ActorSubject = "native-upgrade", LoginBinding = upgradeHash, OwnerHash = upgradeHash,
            RootId = "test.upgrade", RelativePath = "pending", TargetHash = upgradeHash, RequestHash = upgradeHash, ExpectedVersion = "ABSENT", Sha256 = upgradeHash,
            CreatedAtUtc = upgradeAt, ExpiresAtUtc = upgradeAt.AddMinutes(15), State = "Unknown" });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        await db.GetService<IMigrator>().MigrateAsync("20261017044943_FileHistoryAndRecycle");
        var archiveUpgradeId = Guid.NewGuid();
        db.OperationJobs.Add(new() { Id = archiveUpgradeId, JobType = "files.archive", ActorSubject = "native-upgrade", IdempotencyKey = archiveUpgradeId.ToString("D") });
        db.FileArchiveOperations.Add(new() { Id = archiveUpgradeId, ActorSubject = "native-upgrade", LoginBinding = upgradeHash, OwnerHash = upgradeHash, RequestHash = upgradeHash,
            Kind = "trash", RootId = "test.upgrade", RelativePath = "pending", TargetHash = upgradeHash, ExpectedVersion = upgradeHash, Sha256 = upgradeHash,
            PlanJson = "{}", CreatedAtUtc = upgradeAt, ExpiresAtUtc = upgradeAt.AddMinutes(15), NextAttemptAtUtc = upgradeAt });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        await db.Database.MigrateAsync(); Assert.False(db.Database.HasPendingModelChanges());
        var archiveRegistration = await db.FileArchiveRegistrations.AsNoTracking().SingleAsync(item => item.Id == archiveUpgradeId);
        Assert.Equal("native-upgrade", archiveRegistration.ActorSubject); Assert.Equal(archiveUpgradeId.ToString("D"), archiveRegistration.IdempotencyKey);
        Assert.Equal(upgradeHash, archiveRegistration.RequestHash);
        await db.FileArchiveOperations.Where(item => item.Id == archiveUpgradeId).ExecuteDeleteAsync();
        await db.FileArchiveRegistrations.Where(item => item.Id == archiveUpgradeId).ExecuteDeleteAsync();
        await db.OperationJobs.Where(item => item.Id == archiveUpgradeId).ExecuteDeleteAsync();
        Assert.Equal(uploadUpgradeId, (await db.FilePathLocks.SingleAsync(item => item.PathHash == upgradeHash)).OperationId);
        var legacyPathLock = await db.FilePathLocks.SingleAsync(item => item.PathHash == upgradeHash);
        Assert.Equal("Legacy", legacyPathLock.ScopeKind); Assert.Null(legacyPathLock.RootId); Assert.Null(legacyPathLock.RelativePath);
        await db.FilePathLocks.Where(item => item.OperationId == uploadUpgradeId).ExecuteDeleteAsync();
        await db.FileUploadOperations.Where(item => item.Id == uploadUpgradeId).ExecuteDeleteAsync();
        await db.OperationJobs.Where(item => item.Id == uploadUpgradeId).ExecuteDeleteAsync(); db.ChangeTracker.Clear();
        var upgradedHost = await db.TerminalSessions.SingleAsync(item => item.Id == upgradeId);
        Assert.Equal("Host", upgradedHost.TargetKind); Assert.Equal("whaledeck.host", upgradedHost.TargetId);
        Assert.Null(upgradedHost.InstanceId); Assert.Null(upgradedHost.Shell); Assert.Equal(upgradeKey, upgradedHost.IdempotencyKey);
        db.TerminalSessions.Remove(upgradedHost); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        Console.WriteLine("INBOX_PHASE=terminal-target-real-upgrade");
        Console.WriteLine("INBOX_PHASE=dispatch-migrated");
        var now = DateTimeOffset.UtcNow;
        var channel = new NotificationChannel { Name = "isolated-test", Provider = "Webhook", DestinationReference = "approved-test", IsEnabled = true, CreatedAtUtc = now.AddMinutes(-1) };
        db.NotificationChannels.Add(channel);
        for (var index = 0; index < 2; index++) db.NotificationRoutes.Add(new()
        { ChannelId = channel.Id, SourceKind = "Alert", MinimumSeverity = "Info", IsEnabled = true, CreatedAtUtc = now.AddMinutes(-1) });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        NotificationEvent Event(string key, string audience = "Administrator", DateTimeOffset? published = null, DateTimeOffset? expires = null) => new()
        {
            SourceKey = key, SourceKind = "Alert", Audience = audience, RecipientSubject = audience == "Subject" ? "ordinary-subject" : null,
            MessageCode = "ALERT_ACTIVE", Severity = "Critical", PublishedAtUtc = published ?? now, OccurredAtUtc = now,
            ExpiresAtUtc = expires ?? now.AddDays(1)
        };

        // Sequence allocation is not commit order. The lower sequence remains uncommitted until after routing the higher.
        await using var delayed = new PlatformDbContext(options);
        await using var delayedTransaction = await delayed.Database.BeginTransactionAsync();
        var late = Event("late-commit"); delayed.NotificationEvents.Add(late); await delayed.SaveChangesAsync();
        var early = Event("early-commit"); var personal = Event("personal", "Subject");
        var old = Event("before-route", published: now.AddMinutes(-2)); var expired = Event("expired", expires: now.AddSeconds(-1));
        db.NotificationEvents.AddRange(early, personal, old, expired); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var store = new NotificationDispatchStore(db);
        Assert.Equal(3, await store.RouteBatchAsync(now, default));
        Assert.Single(await db.NotificationDeliveries.ToArrayAsync());
        await delayedTransaction.CommitAsync();
        Assert.Equal(1, await store.RouteBatchAsync(now, default));
        Assert.Equal(0, await store.RouteBatchAsync(now, default));
        Assert.Equal(2, await db.NotificationDeliveries.CountAsync());
        Assert.Equal("Personal", (await db.NotificationDispatchReceipts.AsNoTracking().SingleAsync(item => item.NotificationId == personal.Id)).Outcome);
        Assert.Equal("NoRoute", (await db.NotificationDispatchReceipts.AsNoTracking().SingleAsync(item => item.NotificationId == old.Id)).Outcome);
        Assert.False(await db.NotificationDispatchReceipts.AnyAsync(item => item.NotificationId == expired.Id));
        Console.WriteLine("INBOX_PHASE=dispatch-late-commit");

        var claims = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            await using var concurrent = new PlatformDbContext(options);
            return await new NotificationDispatchStore(concurrent).ClaimAsync(now, default);
        }));
        var leased = claims.OfType<NotificationDelivery>().ToArray();
        Assert.Equal(2, leased.Length); Assert.Equal(2, leased.Select(item => item.Id).Distinct().Count());
        Assert.False(await store.BeginSendAsync(leased[0].Id, Guid.NewGuid(), now, default));
        Assert.True(await store.BeginSendAsync(leased[0].Id, leased[0].LeaseToken!.Value, now, default));
        // One prepare lease and one sending lease expire: only the prepare lease is safe to repeat.
        Assert.Equal(2, await store.RecoverBatchAsync(now.AddMinutes(2), default));
        Assert.Equal("Unknown", (await db.NotificationDeliveries.AsNoTracking().SingleAsync(item => item.Id == leased[0].Id)).State);
        Assert.Equal("Pending", (await db.NotificationDeliveries.AsNoTracking().SingleAsync(item => item.Id == leased[1].Id)).State);
        Assert.False(await store.FinishAsync(leased[0].Id, leased[0].LeaseToken!.Value, now.AddMinutes(2), NotificationTransportResult.Accepted, default));

        var retry = Assert.IsType<NotificationDelivery>(await store.ClaimAsync(now.AddMinutes(2), default));
        Assert.Equal(leased[1].Id, retry.Id);
        Assert.True(await store.BeginSendAsync(retry.Id, retry.LeaseToken!.Value, now.AddMinutes(2), default));
        Assert.True(await store.FinishAsync(retry.Id, retry.LeaseToken!.Value, now.AddMinutes(2).AddSeconds(1), NotificationTransportResult.RetryableNotSent, default));
        Assert.Null(await store.ClaimAsync(now.AddMinutes(2).AddSeconds(2), default));
        var secondAttempt = Assert.IsType<NotificationDelivery>(await store.ClaimAsync(now.AddMinutes(3), default));
        Assert.True(await store.BeginSendAsync(secondAttempt.Id, secondAttempt.LeaseToken!.Value, now.AddMinutes(3), default));
        Assert.True(await store.FinishAsync(secondAttempt.Id, secondAttempt.LeaseToken!.Value, now.AddMinutes(3), NotificationTransportResult.Accepted, default));
        Assert.False(await store.FinishAsync(secondAttempt.Id, secondAttempt.LeaseToken!.Value, now.AddMinutes(3), NotificationTransportResult.Accepted, default));
        var succeeded = await db.NotificationDeliveries.AsNoTracking().SingleAsync(item => item.Id == secondAttempt.Id);
        Assert.Equal("Succeeded", succeeded.State); Assert.Equal(2, succeeded.AttemptCount);
        Console.WriteLine("INBOX_PHASE=dispatch-leases");

        var changed = Event("channel-changed"); db.NotificationEvents.Add(changed); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        Assert.Equal(1, await store.RouteBatchAsync(now.AddMinutes(4), default));
        var stale = Assert.IsType<NotificationDelivery>(await store.ClaimAsync(now.AddMinutes(4), default));
        await db.NotificationChannels.Where(item => item.Id == channel.Id).ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Version, 1L));
        Assert.False(await store.BeginSendAsync(stale.Id, stale.LeaseToken!.Value, now.AddMinutes(4), default));
        Assert.Equal(1, await store.RecoverBatchAsync(now.AddMinutes(4), default));
        Assert.Equal("Canceled", (await db.NotificationDeliveries.AsNoTracking().SingleAsync(item => item.Id == stale.Id)).State);

        Assert.Equal(6, await NotificationRetention.PruneAsync(db, now.AddDays(2), default));
        Assert.Empty(await db.NotificationDeliveries.ToArrayAsync());
        Assert.Empty(await db.NotificationDispatchReceipts.ToArrayAsync());
        Assert.Single(await db.NotificationChannels.ToArrayAsync());
        Console.WriteLine("INBOX_PHASE=dispatch-retained");
        await Management(options, db, store, now);
        await Sender(db);
        await PrivateWorkflow(db);
        await PrivateBoundaries(options, db);
        await JobEventBoundaries(options);
        await TerminalSessionBoundaries(options);
        await FileUploadBoundaries(options);
        await FileMutationBoundaries(options);
        await FileArchiveBoundaries(options);
        await FileArchiveWorkflowBoundaries(options);
        await FileHierarchyLockBoundaries(options);
        await FileTreeCopyWorkflowBoundaries(options);
        await FileTreePackingWorkflowBoundaries(options);
        await FileTreeUnpackingWorkflowBoundaries(options);
        await FileTreeMoveWorkflowBoundaries(options);
    }

    private static async Task PrivateWorkflow(PlatformDbContext db)
    {
        var folder = Path.Combine(Path.GetTempPath(), "wd-private-native-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        var key = Path.Combine(folder, "key"); await File.WriteAllBytesAsync(key, RandomNumberGenerator.GetBytes(32));
        try
        {
            var channel = new NotificationChannel { Name = "private-native", Provider = "Webhook", DestinationReference = "legacy", IsEnabled = true };
            db.NotificationChannels.Add(channel); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            var protector = new NotificationPrivateProtector(new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["Agent:CapabilityKeyFile"] = key }).Build());
            var broker = new NotificationPrivateInputStore(); var staged = new WhaleDeck.Infrastructure.Secrets.MemoryOneTimeSecretStore();
            var gateway = new NativePrivateGateway(); var repository = new NotificationPrivateRepository(db);
            var service = new WhaleDeck.Application.Services.NotificationPrivateWorkflow(repository, gateway, staged, broker, protector, new NotificationPrivateValidator(), TimeProvider.System);
            var context = new NotificationContext("native-private-admin", true, "private-trace", null, Guid.NewGuid().ToString("D"));
            const string raw = "{\"target\":{\"provider\":\"Webhook\",\"url\":\"https://example.invalid/private-native\",\"allowedAddresses\":[]},\"credentialMode\":\"remove\"}";
            var ticket = await staged.StoreAsync(context.Subject, raw, default);
            var plan = await service.PlanAsync(context, channel.Id, new(0, "apply", ticket.Token), default);
            var command = new NotificationPrivateSubmitCommand(plan.PlanHash, ticket.Token, true);
            var submitted = await service.SubmitAsync(context, channel.Id, command, default);
            Assert.Equal(submitted.Id, (await service.SubmitAsync(context, channel.Id, command, default)).Id);
            var frozen = await db.NotificationChannels.AsNoTracking().SingleAsync(item => item.Id == channel.Id);
            Assert.False(frozen.IsEnabled); Assert.True(frozen.PrivateConfigurationBlocked); Assert.Equal(1, frozen.Version);
            Assert.DoesNotContain("example.invalid", (await db.OperationJobs.AsNoTracking().SingleAsync(item => item.Id == submitted.Id)).RequestJson);
            Assert.DoesNotContain(ticket.Token, (await db.OperationJobs.AsNoTracking().SingleAsync(item => item.Id == submitted.Id)).RequestJson);
            await Assert.ThrowsAsync<InvalidOperationException>(() => new NotificationManagementService(new NotificationManagementRepository(db)).TestAsync(context, channel.Id, new(1, true), default));
            var dispatcher = new NotificationPrivateDispatcher(repository, gateway, broker, new NativeAuthorizer(true), protector, TimeProvider.System);
            await dispatcher.TickAsync(default); Assert.Equal(1, gateway.Applies);
            var interrupted = await db.NotificationPrivateOperations.AsNoTracking().SingleAsync(item => item.Id == submitted.Id);
            Assert.NotNull(interrupted.RootStartedAtUtc); Assert.Equal("Running", interrupted.State);
            await new JobRepository(db).RequestCancellationAsync(submitted.Id, context.Subject, default);
            await broker.DeleteAsync(context.Subject, submitted.Id, default); // Input can disappear after root success.
            await db.NotificationPrivateOperations.Where(item => item.Id == submitted.Id).ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.LeaseExpiresAtUtc, DateTimeOffset.UtcNow.AddSeconds(-1)).SetProperty(item => item.NextAttemptAtUtc, DateTimeOffset.UtcNow.AddSeconds(-1)));
            await new NotificationPrivateDispatcher(repository, gateway, broker, new NativeAuthorizer(false), protector, TimeProvider.System).TickAsync(default);
            Assert.Equal(1, gateway.Applies);
            Assert.Equal("Succeeded", (await db.OperationJobs.AsNoTracking().SingleAsync(item => item.Id == submitted.Id)).State);
            Assert.NotNull((await db.OperationJobs.AsNoTracking().SingleAsync(item => item.Id == submitted.Id)).CancelRequestedAtUtc);
            var applied = await db.NotificationChannels.AsNoTracking().SingleAsync(item => item.Id == channel.Id);
            Assert.False(applied.IsEnabled); Assert.False(applied.PrivateConfigurationBlocked); Assert.Equal("wd-" + channel.Id.ToString("N"), applied.DestinationReference); Assert.Equal(2, applied.Version);
            Assert.Equal(2, await db.AuditEvents.CountAsync(item => item.JobId == submitted.Id));
            Assert.True(await db.OutboxMessages.AnyAsync(item => item.MessageType == "NotificationPrivateRequested" && item.ProcessedAtUtc != null));
            Assert.True(await db.OutboxMessages.AnyAsync(item => item.MessageType == "NotificationPrivateCancellationRequested" && item.ProcessedAtUtc != null));
            Assert.Equal("Succeeded", (await service.SubmitAsync(context, channel.Id, command, default)).State);
            Console.WriteLine("INBOX_PHASE=private-atomic-job-pause-lost-reply-receipt-permanent-replay");
        }
        finally { File.Delete(key); Directory.Delete(folder); }
    }

    private sealed class NativePrivateGateway : INotificationPrivateGateway
    {
        public int Applies; private NotificationPrivateMetadata? _receipt;
        public Task<NotificationPrivateMetadata> InspectAsync(Guid channelId, CancellationToken cancellationToken) => Task.FromResult(new NotificationPrivateMetadata("EMPTY", "wd-" + channelId.ToString("N"), null, false, false, false, false, null, true));
        public Task<NotificationPrivateMetadata> InspectReceiptAsync(Guid channelId, Guid operationId, CancellationToken cancellationToken) => _receipt is null ? InspectAsync(channelId, cancellationToken) : Task.FromResult(_receipt);
        public Task<NotificationPrivateMetadata> ApplyAsync(NotificationPrivateUpdate update, CancellationToken cancellationToken)
        { Applies++; _receipt = new("EMPTY", "wd-" + update.ChannelId.ToString("N"), null, true, false, true, false, update.OperationId, true); throw new IOException("synthetic lost reply after root commit"); }
    }

    private static async Task Sender(PlatformDbContext db)
    {
        // The entire database is the already-verified isolated wdv_dispatch_* namespace.
        await db.NotificationChannels.ExecuteUpdateAsync(setters => setters.SetProperty(item => item.IsEnabled, false));
        await db.NotificationRoutes.ExecuteUpdateAsync(setters => setters.SetProperty(item => item.IsEnabled, false));
        await db.NotificationEvents.ExecuteDeleteAsync(); db.ChangeTracker.Clear();
        var directory = Path.Combine(Path.GetTempPath(), "wdv-sender-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "registry.json");
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        const string fakeSecret = "native-secret-not-a-real-key";
        var registry = JsonSerializer.Serialize(new
        {
            version = 1,
            targets = new Dictionary<string, object> { ["native-target"] = new { provider = "Webhook", url = $"http://127.0.0.1:{port}/hook", allowedAddresses = LoopbackAddresses, allowPrivateNetwork = true, allowHttp = true, minimumIntervalSeconds = 5 } },
            credentials = new Dictionary<string, object> { ["native-credential"] = new { signingSecret = fakeSecret } }
        });
        try
        {
            await File.WriteAllTextAsync(path, registry);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            var clock = new SenderClock(); var channels = Enumerable.Range(0, 2).Select(index => new NotificationChannel
            { Name = "native-sender-" + index, Provider = "Webhook", DestinationReference = "native-target", CredentialReference = "native-credential", IsEnabled = true, CreatedAtUtc = clock.GetUtcNow().AddMinutes(-1) }).ToArray();
            db.NotificationChannels.AddRange(channels);
            foreach (var channel in channels) db.NotificationRoutes.Add(new() { ChannelId = channel.Id, SourceKind = "Alert", MinimumSeverity = "Info", IsEnabled = true, CreatedAtUtc = clock.GetUtcNow().AddMinutes(-1) });
            var source = new NotificationEvent { SourceKey = "sender-native-event", SourceKind = "Alert", Audience = "Administrator", MessageCode = "ALERT_ACTIVE", Severity = "Error", OccurredAtUtc = clock.GetUtcNow(), PublishedAtUtc = clock.GetUtcNow(), ExpiresAtUtc = clock.GetUtcNow().AddDays(1) };
            db.NotificationEvents.Add(source); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            var receiver = Receive(listener, 2, fakeSecret, deadline.Token);
            var store = new NotificationDispatchStore(db);
            var transport = new NotificationTransport(new PrivateNotificationConfiguration(path), new NotificationNetworkPolicy(true), clock);
            var sender = new NotificationSender(store, transport, clock);
            Assert.Equal(1, await sender.TickAsync(deadline.Token));
            var statuses = await db.NotificationDeliveries.AsNoTracking().Where(item => item.NotificationId == source.Id).ToArrayAsync();
            Assert.Equal(2, statuses.Length); Assert.Single(statuses, item => item.State == "Succeeded");
            Assert.Single(statuses, item => item.State == "RetryWait" && item.LastResultCode == "RateDeferred" && item.AttemptCount == 0);
            Assert.Equal(0, await sender.TickAsync(deadline.Token));
            clock.Advance(TimeSpan.FromSeconds(6)); Assert.Equal(1, await sender.TickAsync(deadline.Token));
            await receiver;
            Assert.Equal(2, await db.NotificationDeliveries.CountAsync(item => item.NotificationId == source.Id && item.State == "Succeeded"));
            Assert.Equal(2, await db.NotificationDeliveryAttempts.CountAsync(item => item.ResultCode == "Accepted"));
            foreach (var id in statuses.Select(item => item.Id))
                Assert.Single(await new NotificationManagementRepository(db).AttemptsAsync(id, default));

            // A queued old revision cannot adopt newly rotated private values.
            var rotated = new NotificationEvent { SourceKey = "sender-rotation", SourceKind = "Alert", Audience = "Administrator", MessageCode = "ALERT_RECOVERED", Severity = "Info", OccurredAtUtc = clock.GetUtcNow(), PublishedAtUtc = clock.GetUtcNow(), ExpiresAtUtc = clock.GetUtcNow().AddDays(1) };
            db.NotificationEvents.Add(rotated); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            await using var prepared = await transport.PrepareAsync("Webhook", "native-target", "native-credential", default);
            await store.RouteBatchAsync(clock.GetUtcNow(), default, channels.ToDictionary(item => item.Id, _ => prepared.ConfigurationRevision));
            var replacement = path + ".new";
            await File.WriteAllTextAsync(replacement, registry.Replace(fakeSecret, "rotated-native-not-a-real-key", StringComparison.Ordinal));
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(replacement, UnixFileMode.UserRead | UnixFileMode.UserWrite); File.Move(replacement, path, true);
            Assert.Equal(0, await sender.TickAsync(deadline.Token));
            Assert.Equal(2, await db.NotificationDeliveries.CountAsync(item => item.NotificationId == rotated.Id && item.State == "Canceled" && item.LastResultCode == "ConfigurationChanged"));
            Assert.Equal(2, await db.NotificationDeliveryAttempts.CountAsync());
            Assert.False(await db.NotificationDeliveries.AnyAsync(item => item.ConfigurationRevision == null));
            await File.WriteAllTextAsync(path, registry); // Restore the old binding only for new isolated explicit intents.
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            await Explicit(db, sender, transport, path, listener, channels[0], clock, fakeSecret, deadline.Token);
            await Smtp(path, source, clock, deadline.Token);
            Console.WriteLine("INBOX_PHASE=dispatch-sender-http-rate-rotation");
        }
        finally { listener.Stop(); File.Delete(path); File.Delete(path + ".new"); Directory.Delete(directory); }
    }

    private static async Task Smtp(string path, NotificationEvent message, TimeProvider clock, CancellationToken cancellationToken)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder(); names.AddIpAddress(IPAddress.Loopback); request.CertificateExtensions.Add(names.Build());
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        try
        {
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { version = 1,
                targets = new Dictionary<string, object> { ["native-email"] = new { provider = "Email", smtpHost = "127.0.0.1", smtpPort = port, tls = "SslOnConnect", from = "whaledeck@example.invalid", to = CollectorRecipients, allowedAddresses = LoopbackAddresses, allowPrivateNetwork = true } },
                credentials = new Dictionary<string, object>() }), cancellationToken);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            var accepted = ReceiveSmtp(listener, certificate, cancellationToken);
            var transport = new NotificationTransport(new PrivateNotificationConfiguration(path), new NotificationNetworkPolicy(true, certificate.GetCertHashString(HashAlgorithmName.SHA256)), clock);
            await using var prepared = await transport.PrepareAsync("Email", "native-email", null, cancellationToken);
            var id = Guid.NewGuid();
            Assert.Equal(NotificationTransportResult.Accepted, await prepared.SendAsync(id, message, cancellationToken));
            var data = await accepted;
            Assert.Contains(id.ToString("N") + "@whaledeck.invalid", data); Assert.Contains("ALERT_ACTIVE", data);
            Assert.DoesNotContain("recipientSubject", data);
            Console.WriteLine("INBOX_PHASE=dispatch-sender-real-tls-smtp");
        }
        finally { listener.Stop(); }
    }

    private static async Task<string> ReceiveSmtp(TcpListener listener, X509Certificate2 certificate, CancellationToken cancellationToken)
    {
        using var client = await listener.AcceptTcpClientAsync(cancellationToken);
        await using var stream = new SslStream(client.GetStream());
        await stream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = certificate, ClientCertificateRequired = false }, cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8, false, 4096, true);
        await stream.WriteAsync(Encoding.ASCII.GetBytes("220 native.local ESMTP\r\n"), cancellationToken);
        for (var index = 0; index < 20; index++)
        {
            var line = await reader.ReadLineAsync(cancellationToken); Assert.NotNull(line); Assert.True(line.Length < 2048);
            string response;
            if (line.StartsWith("EHLO ", StringComparison.Ordinal)) response = "250-native.local\r\n250 PIPELINING\r\n";
            else if (line.StartsWith("MAIL FROM:", StringComparison.Ordinal) || line.StartsWith("RCPT TO:", StringComparison.Ordinal)) response = "250 OK\r\n";
            else if (line == "DATA")
            {
                await stream.WriteAsync(Encoding.ASCII.GetBytes("354 End data with dot\r\n"), cancellationToken);
                var body = new StringBuilder();
                for (var count = 0; count < 200; count++)
                {
                    var part = await reader.ReadLineAsync(cancellationToken); Assert.NotNull(part); Assert.True(part.Length <= 2048);
                    if (part == ".")
                    { await stream.WriteAsync(Encoding.ASCII.GetBytes("250 Accepted by isolated collector\r\n"), cancellationToken); return body.ToString(); }
                    body.AppendLine(part); Assert.True(body.Length < 32768);
                }
                throw new InvalidOperationException("Isolated SMTP DATA budget exceeded.");
            }
            else throw new InvalidOperationException("Unexpected isolated SMTP command.");
            await stream.WriteAsync(Encoding.ASCII.GetBytes(response), cancellationToken);
        }
        throw new InvalidOperationException("Isolated SMTP command budget exceeded.");
    }

    private static async Task Receive(TcpListener listener, int count, string secret, CancellationToken cancellationToken, bool dropReply = false)
    {
        for (var index = 0; index < count; index++)
        {
            using var client = await listener.AcceptTcpClientAsync(cancellationToken); await using var stream = client.GetStream();
            var header = new List<byte>(); var one = new byte[1];
            while (header.Count < 16384)
            {
                Assert.Equal(1, await stream.ReadAsync(one, cancellationToken)); header.Add(one[0]);
                if (header.Count >= 4 && header[^4] == 13 && header[^3] == 10 && header[^2] == 13 && header[^1] == 10) break;
            }
            Assert.True(header.Count < 16384);
            var lines = Encoding.ASCII.GetString(header.ToArray()).Split("\r\n"); Assert.Equal("POST /hook HTTP/1.1", lines[0]);
            var fields = lines.Skip(1).Where(line => line.Contains(':', StringComparison.Ordinal)).Select(line => line.Split(':', 2))
                .ToDictionary(item => item[0], item => item[1].Trim(), StringComparer.OrdinalIgnoreCase);
            var length = int.Parse(fields["Content-Length"], System.Globalization.CultureInfo.InvariantCulture); Assert.InRange(length, 1, 16384);
            var body = new byte[length]; await stream.ReadExactlyAsync(body, cancellationToken);
            var id = Guid.Parse(fields["X-WhaleDeck-Delivery"]);
            Assert.Equal(NotificationPayload.WebhookSignature(fields["X-WhaleDeck-Timestamp"], id, body, secret), fields["X-WhaleDeck-Signature"]);
            using var json = JsonDocument.Parse(body); Assert.Equal(id, json.RootElement.GetProperty("deliveryId").GetGuid());
            Assert.DoesNotContain(secret, Encoding.UTF8.GetString(body)); Assert.DoesNotContain("recipientSubject", Encoding.UTF8.GetString(body));
            if (!dropReply) await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 204 No Content\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), cancellationToken);
        }
    }
    private sealed class SenderClock : TimeProvider
    {
        private DateTimeOffset current = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => current;
        public void Advance(TimeSpan duration) => current += duration;
    }

    private static async Task Explicit(PlatformDbContext db, NotificationSender automaticSender, NotificationTransport transport, string path, TcpListener listener,
        NotificationChannel channel, SenderClock clock, string secret, CancellationToken cancellationToken)
    {
        var management = new NotificationManagementService(new NotificationManagementRepository(db));
        NotificationContext Context() => new("isolated-admin", true, "native-explicit", "127.0.0.1", Guid.NewGuid().ToString());
        var disabled = new NotificationChannel { Name = "native-test-disabled", Provider = channel.Provider, DestinationReference = channel.DestinationReference,
            CredentialReference = channel.CredentialReference, IsEnabled = false };
        db.NotificationChannels.Add(disabled); await db.SaveChangesAsync(cancellationToken); db.ChangeTracker.Clear();
        var testContext = Context(); var testCommand = new TestNotificationCommand(0, true);
        var test = await management.TestAsync(testContext, disabled.Id, testCommand, cancellationToken);
        Assert.Equal(test.DeliveryId, (await management.TestAsync(testContext, disabled.Id, testCommand, cancellationToken)).DeliveryId);
        clock.Advance(TimeSpan.FromSeconds(6));
        var accepted = Receive(listener, 1, secret, cancellationToken);
        var authorizedSender = new NotificationSender(new NotificationDispatchStore(db), transport, clock, new NativeAuthorizer(true));
        Assert.Equal(1, await authorizedSender.TickAsync(cancellationToken)); await accepted;
        Assert.Equal("Succeeded", (await db.NotificationDeliveries.AsNoTracking().SingleAsync(item => item.Id == test.DeliveryId, cancellationToken)).State);
        Assert.False((await db.NotificationChannels.AsNoTracking().SingleAsync(item => item.Id == disabled.Id, cancellationToken)).IsEnabled);
        Assert.Equal(1, await db.NotificationDeliveries.CountAsync(item => item.NotificationId == test.NotificationId, cancellationToken));
        var revoked = await management.TestAsync(Context(), disabled.Id, testCommand, cancellationToken);
        Assert.Equal(0, await new NotificationSender(new NotificationDispatchStore(db), transport, clock, new NativeAuthorizer(false)).TickAsync(cancellationToken));
        Assert.Equal("PermissionRevoked", (await db.NotificationDeliveries.AsNoTracking().SingleAsync(item => item.Id == revoked.DeliveryId, cancellationToken)).LastResultCode);

        await db.NotificationChannels.Where(item => item.Id != channel.Id).ExecuteUpdateAsync(setters => setters.SetProperty(item => item.IsEnabled, false), cancellationToken);
        var source = new NotificationEvent { SourceKey = "native-response-lost", SourceKind = "Alert", Audience = "Administrator", MessageCode = "ALERT_ACTIVE", Severity = "Error",
            OccurredAtUtc = clock.GetUtcNow(), PublishedAtUtc = clock.GetUtcNow(), ExpiresAtUtc = clock.GetUtcNow().AddDays(1) };
        db.NotificationEvents.Add(source); await db.SaveChangesAsync(cancellationToken); db.ChangeTracker.Clear();
        clock.Advance(TimeSpan.FromSeconds(6));
        var lost = Receive(listener, 1, secret, cancellationToken, true);
        Assert.Equal(1, await automaticSender.TickAsync(cancellationToken)); await lost;
        var unknown = await db.NotificationDeliveries.AsNoTracking().SingleAsync(item => item.NotificationId == source.Id, cancellationToken);
        Assert.Equal("Unknown", unknown.State); Assert.Equal(1, unknown.AttemptCount);
        Assert.Equal(0, await automaticSender.TickAsync(cancellationToken));
        var retryContext = Context(); var retryCommand = new RetryUnknownNotificationCommand(unknown.Version, channel.Version, true, true);
        var retry = await management.RetryUnknownAsync(retryContext, unknown.Id, retryCommand, cancellationToken);
        Assert.Equal(retry.DeliveryId, (await management.RetryUnknownAsync(retryContext, unknown.Id, retryCommand, cancellationToken)).DeliveryId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => management.RetryUnknownAsync(Context(), unknown.Id, retryCommand, cancellationToken));
        var received = Receive(listener, 1, secret, cancellationToken); clock.Advance(TimeSpan.FromSeconds(6));
        Assert.Equal(1, await authorizedSender.TickAsync(cancellationToken)); await received;
        var original = await db.NotificationDeliveries.AsNoTracking().SingleAsync(item => item.Id == unknown.Id, cancellationToken);
        Assert.Equal("Unknown", original.State); Assert.Equal(unknown.Version, original.Version); Assert.Equal(unknown.AttemptCount, original.AttemptCount);
        var child = await db.NotificationDeliveries.AsNoTracking().SingleAsync(item => item.Id == retry.DeliveryId, cancellationToken);
        Assert.Equal("Succeeded", child.State); Assert.Equal(unknown.Id, child.OriginDeliveryId); Assert.Equal(1, child.AttemptCount);
        var listed = await management.ListDeliveriesAsync(Context(), new(Take: 100), channel.Id, null, cancellationToken);
        Assert.True(listed.Items.Single(item => item.Id == original.Id).HasManualRetry);
        await Assert.ThrowsAsync<InvalidOperationException>(() => management.RetryUnknownAsync(Context(), child.Id, new(child.Version, channel.Version, true, true), cancellationToken));
        // Event retention also removes a bounded manual child chain and its attempt rows.
        await db.NotificationEvents.Where(item => item.Id == source.Id).ExecuteDeleteAsync(cancellationToken);
        Assert.False(await db.NotificationDeliveries.AnyAsync(item => item.Id == original.Id || item.Id == child.Id, cancellationToken));
        Console.WriteLine("INBOX_PHASE=dispatch-explicit-unknown-response-loss-retry");
    }
    private sealed class NativeAuthorizer(bool administrator) : INotificationRequestAuthorizer
    {
        public Task<bool> IsAdministratorAsync(string subject, CancellationToken cancellationToken) => Task.FromResult(administrator);
    }

    private static async Task Management(DbContextOptions<PlatformDbContext> options, PlatformDbContext db, NotificationDispatchStore store, DateTimeOffset now)
    {
        var repository = new NotificationManagementRepository(db); var service = new NotificationManagementService(repository);
        NotificationContext Context(string actor = "native-management") => new(actor, true, "native-trace", "127.0.0.1", Guid.NewGuid().ToString());
        var context = Context(); var create = new SaveNotificationChannelCommand("Managed", "Webhook", "approved-target", "private-reference", Confirmed: true);
        var channel = await service.SaveChannelAsync(context, null, create, default);
        Assert.Equal(channel.Id, (await service.SaveChannelAsync(context, null, create, default)).Id);
        Assert.Equal(1, await db.AuditEvents.CountAsync(item => item.ActorSubject == context.Subject));
        Assert.Equal(1, await db.NotificationChannels.CountAsync(item => item.Id == channel.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveChannelAsync(context, null, create with { Name = "Different" }, default));
        var updateContext = Context();
        var update = create with { Name = "Updated", CredentialReference = null, KeepCredential = true, ExpectedVersion = channel.Version };
        var updated = await service.SaveChannelAsync(updateContext, channel.Id, update, default);
        Assert.Equal(1, updated.Version); Assert.True(updated.HasCredential);
        Assert.Equal("private-reference", (await db.NotificationChannels.AsNoTracking().SingleAsync(item => item.Id == channel.Id)).CredentialReference);
        Assert.Equal(1, (await service.SaveChannelAsync(updateContext, channel.Id, update, default)).Version);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => service.SaveChannelAsync(Context(), channel.Id, update, default));

        var routeContext = Context(); var routeCommand = new SaveNotificationRouteCommand(channel.Id, Confirmed: true);
        var route = await service.SaveRouteAsync(routeContext, null, routeCommand, default);
        Assert.Equal(route.Id, (await service.SaveRouteAsync(routeContext, null, routeCommand, default)).Id);
        Assert.Equal(2, (await db.NotificationChannels.AsNoTracking().SingleAsync(item => item.Id == channel.Id)).Version);
        // Overlapping routes are legal; dispatch deduplicates by event and channel.
        var overlap = await service.SaveRouteAsync(Context(), null, routeCommand, default);
        var changedRoute = await service.SaveRouteAsync(Context(), route.Id, routeCommand with { MinimumSeverity = "Error", ExpectedVersion = route.Version }, default);
        Assert.Equal(1, changedRoute.Version);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => service.SaveRouteAsync(Context(), route.Id, routeCommand with { ExpectedVersion = 0 }, default));
        var deleteContext = Context();
        await service.DeleteRouteAsync(deleteContext, route.Id, new(1, true), default);
        await service.DeleteRouteAsync(deleteContext, route.Id, new(1, true), default);
        Assert.False(await db.NotificationRoutes.AnyAsync(item => item.Id == route.Id));
        await service.DeleteRouteAsync(Context(), overlap.Id, new(overlap.Version, true), default);

        var pageOne = await service.ListChannelsAsync(Context(), new(Take: 1), default);
        var pageTwo = await service.ListChannelsAsync(Context(), new(Page: 2, Take: 1), default);
        Assert.True(pageOne.HasMore); Assert.NotEqual(pageOne.Items.Single().Id, pageTwo.Items.Single().Id);
        var enabled = await service.SaveChannelAsync(Context(), channel.Id, update with { ExpectedVersion = 6, IsEnabled = true }, default);
        Assert.Equal(7, enabled.Version);
        var enabledRoute = await service.SaveRouteAsync(Context(), null, new(channel.Id, MinimumSeverity: "Info", IsEnabled: true, Confirmed: true), default);
        var item = new NotificationEvent { SourceKey = "management-version", SourceKind = "Alert", Audience = "Administrator", MessageCode = "ALERT_ACTIVE", Severity = "Critical",
            PublishedAtUtc = DateTimeOffset.UtcNow.AddSeconds(1), OccurredAtUtc = now, ExpiresAtUtc = now.AddDays(1) };
        db.NotificationEvents.Add(item); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        await store.RouteBatchAsync(now.AddMinutes(5), default);
        Assert.True(await db.NotificationDeliveries.AnyAsync(delivery => delivery.ChannelId == channel.Id));
        await service.SaveRouteAsync(Context(), enabledRoute.Id, new(channel.Id, IsEnabled: false, ExpectedVersion: 0, Confirmed: true), default);
        await store.RecoverBatchAsync(now.AddMinutes(5), default);
        Assert.Equal("Canceled", (await db.NotificationDeliveries.AsNoTracking().SingleAsync(delivery => delivery.ChannelId == channel.Id)).State);
        var deliveries = await service.ListDeliveriesAsync(Context(), new(), channel.Id, "Canceled", default);
        Assert.Single(deliveries.Items);
        var actualVersion = (await db.NotificationChannels.AsNoTracking().SingleAsync(value => value.Id == channel.Id)).Version;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteChannelAsync(Context(), channel.Id, new(actualVersion, true), default));
        await service.SaveChannelAsync(Context(), channel.Id, update with { ExpectedVersion = actualVersion, IsEnabled = false }, default);

        // Failure after the mutation but before audit insertion must roll back both.
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE audit_events ADD CONSTRAINT native_notification_audit_failure CHECK (actor_subject <> 'audit-failure') NOT VALID");
        try
        {
            await Assert.ThrowsAsync<PostgresException>(() => service.SaveChannelAsync(Context("audit-failure"), null, create with { Name = "Must rollback" }, default));
            Assert.False(await db.NotificationChannels.AnyAsync(value => value.Name == "Must rollback"));
        }
        finally { await db.Database.ExecuteSqlRawAsync("ALTER TABLE audit_events DROP CONSTRAINT native_notification_audit_failure"); }

        // One pre-existing enabled channel: race two creators for the final budget slot.
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO notification_channels (id,name,provider,destination_reference,is_enabled,created_at_utc,version)
            SELECT gen_random_uuid(),'budget-' || n,'Webhook','approved-target',true,now(),0 FROM generate_series(1,30) n
            """);
        var successes = await Task.WhenAll(Enumerable.Range(0, 2).Select(async index =>
        {
            await using var concurrent = new PlatformDbContext(options);
            try { await new NotificationManagementService(new NotificationManagementRepository(concurrent)).SaveChannelAsync(Context(), null,
                create with { Name = "concurrent-" + index, IsEnabled = true }, default); return true; }
            catch (InvalidOperationException) { return false; }
        }));
        Assert.Single(successes, value => value); Assert.Equal(32, await db.NotificationChannels.CountAsync(value => value.IsEnabled));
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO notification_routes (id,channel_id,source_kind,minimum_severity,include_recovery,is_enabled,created_at_utc,version)
            SELECT gen_random_uuid(),{channel.Id},'Alert','Warning',true,true,now(),0 FROM generate_series(1,510)
            """);
        Assert.Equal(512, await db.NotificationRoutes.CountAsync(value => value.IsEnabled));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveRouteAsync(Context(), null, new(channel.Id, IsEnabled: true, Confirmed: true), default));
        Assert.DoesNotContain("private-reference", string.Join(' ', await db.AuditEvents.Select(value => value.DetailJson).ToArrayAsync()));
        Console.WriteLine("INBOX_PHASE=dispatch-management");
    }
}
