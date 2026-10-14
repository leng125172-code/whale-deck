using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Infrastructure.Persistence;

public sealed class NotificationPrivateRepository(PlatformDbContext db) : INotificationPrivateRepository
{
    public async Task<NotificationChannel> ChannelAsync(Guid id, CancellationToken cancellationToken)
    {
        if (await db.NotificationPrivateOperations.AnyAsync(item => item.ChannelId == id && (item.State == "Pending" || item.State == "Running" || item.State == "Unknown"), cancellationToken))
            throw new InvalidOperationException("The channel has an unresolved private operation.");
        return await db.NotificationChannels.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken) ?? throw new KeyNotFoundException("Notification channel not found.");
    }
    public async Task<JobDto?> ReplayAsync(Guid id, string actor, string requestHash, CancellationToken cancellationToken)
    {
        var existing = await db.NotificationPrivateOperations.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (existing is null) return null;
        if (existing.ActorSubject != actor || existing.RequestHash != requestHash) throw new InvalidOperationException("Private configuration idempotency key was reused for another request.");
        return Map(await db.OperationJobs.AsNoTracking().SingleAsync(item => item.Id == id, cancellationToken));
    }
    public Task<JobDto> EnqueueAsync(NotificationContext context, Guid id, NotificationPrivatePlanClaims claims, string requestHash, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var replay = await ReplayAsync(id, context.Subject, requestHash, cancellationToken); if (replay is not null) return replay;
        var channel = await LockChannel(claims.ChannelId, cancellationToken);
        if (channel.Version != claims.ChannelVersion || channel.Provider != claims.Provider || channel.Version >= long.MaxValue - 2) throw new InvalidOperationException("The channel changed since planning.");
        await ChannelAsync(channel.Id, cancellationToken);
        if (await db.NotificationPrivateOperations.CountAsync(item => item.State == "Pending" || item.State == "Running" || item.State == "Unknown", cancellationToken) >= 64) throw new InvalidOperationException("Private operation budget exhausted.");
        if (await db.OperationJobs.AnyAsync(item => item.ActorSubject == context.Subject && item.IdempotencyKey == context.IdempotencyKey, cancellationToken)) throw new InvalidOperationException("The operation key already belongs to another job.");
        var now = DateTimeOffset.UtcNow;
        if (claims.ExpiresAtUtc <= now) throw new InvalidOperationException("Private plan expired.");
        await db.NotificationChannels.Where(item => item.Id == channel.Id).ExecuteUpdateAsync(setters => setters.SetProperty(item => item.IsEnabled, false).SetProperty(item => item.PrivateConfigurationBlocked, true).SetProperty(item => item.Version, item => item.Version + 1), cancellationToken);
        var job = new OperationJob { Id = id, JobType = "notifications.private-configuration", ActorSubject = context.Subject, IdempotencyKey = context.IdempotencyKey!,
            RequestJson = JsonSerializer.Serialize(new { channelId = channel.Id, claims.Action, requestHash }) };
        db.OperationJobs.Add(job);
        db.NotificationPrivateOperations.Add(new() { Id = id, ChannelId = channel.Id, ActorSubject = context.Subject, RequestHash = requestHash,
            InputFingerprint = claims.InputFingerprint, RegistryRevision = claims.RegistryRevision, Provider = channel.Provider, Action = claims.Action,
            ChannelVersion = channel.Version + 1, InputExpiresAtUtc = claims.ExpiresAtUtc, NextAttemptAtUtc = now });
        db.OutboxMessages.Add(new() { MessageType = "NotificationPrivateRequested", PayloadJson = JsonSerializer.Serialize(new { jobId = id }) });
        db.OperationJobEvents.Add(new() { JobId = id, Sequence = 1, State = "Queued", Phase = "Queued", MessageCode = "PRIVATE_ACCEPTED" });
        Audit(context.Subject, "notification.private.accepted", id, channel.Id, context.TraceId, context.SourceIp, "Accepted");
        await db.SaveChangesAsync(cancellationToken); return Map(job);
    }, cancellationToken);

    public Task<NotificationPrivateOperation?> ClaimAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var token = Guid.NewGuid();
        return Transaction(async () =>
        {
            var prior = await db.NotificationPrivateOperations.AsNoTracking().SingleOrDefaultAsync(item => item.LeaseToken == token, cancellationToken); if (prior is not null) return prior;
            var expiry = now.AddSeconds(90);
            var ids = await db.Database.SqlQuery<Guid>($"""
                WITH candidate AS (
                  SELECT id FROM notification_private_operations WHERE state IN ('Pending','Running','Unknown')
                    AND next_attempt_at_utc<={now} AND (lease_expires_at_utc IS NULL OR lease_expires_at_utc<={now})
                  ORDER BY next_attempt_at_utc,id LIMIT 1 FOR UPDATE SKIP LOCKED
                ) UPDATE notification_private_operations p SET lease_token={token},lease_expires_at_utc={expiry},version=p.version+1
                  FROM candidate WHERE p.id=candidate.id RETURNING p.id AS "Value"
                """).ToArrayAsync(cancellationToken);
            return ids.Length == 0 ? null : await db.NotificationPrivateOperations.AsNoTracking().SingleAsync(item => item.Id == ids[0], cancellationToken);
        }, cancellationToken);
    }
    public Task<bool> StartedAsync(NotificationPrivateOperation operation, DateTimeOffset now, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var current = await Lock(operation, cancellationToken); if (current is null || current.LeaseExpiresAtUtc <= now) return false;
        var channel = await LockChannel(current.ChannelId, cancellationToken);
        if (channel.Version != current.ChannelVersion || channel.IsEnabled) throw new InvalidOperationException("The private channel lock changed.");
        await db.NotificationPrivateOperations.Where(item => item.Id == current.Id).ExecuteUpdateAsync(setters => setters.SetProperty(item => item.State, "Running")
            .SetProperty(item => item.RootStartedAtUtc, item => item.RootStartedAtUtc ?? now).SetProperty(item => item.Version, item => item.Version + 1), cancellationToken);
        var job = await db.OperationJobs.SingleAsync(item => item.Id == current.Id, cancellationToken);
        job.State = "Running"; job.Phase = "ApplyingPrivateConfiguration"; job.StartedAtUtc ??= now; job.Version++;
        await db.SaveChangesAsync(cancellationToken); return true;
    }, cancellationToken);

    public Task<bool> CancellationRequestedAsync(Guid id, CancellationToken cancellationToken) => db.OperationJobs.AsNoTracking().AnyAsync(item => item.Id == id && item.CancelRequestedAtUtc != null, cancellationToken);
    public Task<NotificationPrivateOperation?> LatestAsync(Guid channelId, CancellationToken cancellationToken) => db.NotificationPrivateOperations.AsNoTracking()
        .Where(item => item.ChannelId == channelId).OrderByDescending(item => item.CreatedAtUtc).ThenByDescending(item => item.Id).FirstOrDefaultAsync(cancellationToken);
    public Task AcknowledgeAsync(NotificationContext context, Guid channelId, Guid operationId, long expectedVersion, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var receiptId = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes("notification-private-ack:" + context.Subject + ":" + Guid.Parse(context.IdempotencyKey!).ToString("D"))).AsSpan(0, 16));
        var requestHash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { channelId, operationId, expectedVersion })));
        var existing = await db.AuditEvents.AsNoTracking().SingleOrDefaultAsync(item => item.Id == receiptId, cancellationToken);
        if (existing is not null)
        {
            using var detail = JsonDocument.Parse(existing.DetailJson ?? "{}");
            if (existing.ActorSubject != context.Subject || existing.Action != "notification.private.unknown-acknowledged" || !detail.RootElement.TryGetProperty("requestHash", out var stored) || stored.GetString() != requestHash)
                throw new InvalidOperationException("The acknowledgment key was reused for another request.");
            return true;
        }
        var current = await db.NotificationPrivateOperations.FromSqlInterpolated($"SELECT * FROM notification_private_operations WHERE id={operationId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("Private operation not found.");
        var now = DateTimeOffset.UtcNow;
        if (current.ChannelId != channelId || current.Version != expectedVersion || current.State != "Unknown" || current.RootStartedAtUtc is null || current.RootStartedAtUtc > now.AddSeconds(-90) || current.LeaseExpiresAtUtc > now)
            throw new InvalidOperationException("The unknown operation changed or still has a live lease.");
        var job = await db.OperationJobs.SingleAsync(item => item.Id == current.Id, cancellationToken);
        await db.NotificationPrivateOperations.Where(item => item.Id == current.Id).ExecuteUpdateAsync(setters => setters.SetProperty(item => item.State, "Failed")
            .SetProperty(item => item.ResultCode, "PRIVATE_UNKNOWN_ACKNOWLEDGED").SetProperty(item => item.CompletedAtUtc, now)
            .SetProperty(item => item.LeaseToken, (Guid?)null).SetProperty(item => item.LeaseExpiresAtUtc, (DateTimeOffset?)null).SetProperty(item => item.Version, item => item.Version + 1), cancellationToken);
        job.State = "Failed"; job.Phase = "UnknownOutcomeAcknowledged"; job.ErrorCode = "PRIVATE_RESULT_UNKNOWN"; job.CompletedAtUtc = now; job.Version++;
        db.AuditEvents.Add(new() { Id = receiptId, ActorSubject = context.Subject, Action = "notification.private.unknown-acknowledged", TargetType = "notification-channel", TargetId = channelId.ToString("D"),
            JobId = job.Id, TraceId = context.TraceId, SourceIp = context.SourceIp, Result = "Acknowledged", DetailJson = JsonSerializer.Serialize(new { requestHash }) });
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE outbox_messages SET processed_at_utc={now} WHERE message_type IN ('NotificationPrivateRequested','NotificationPrivateCancellationRequested') AND payload_json->>'jobId'={current.Id.ToString("D")}", cancellationToken);
        await db.SaveChangesAsync(cancellationToken); return true;
    }, cancellationToken);
    public Task FinishAsync(NotificationPrivateOperation operation, string state, string code, NotificationPrivateMetadata? receipt, CancellationToken cancellationToken) => Transaction(async () =>
    {
        if (state is not ("Succeeded" or "Superseded" or "Unknown" or "Failed" or "Canceled")) throw new ArgumentException("Invalid private result state.");
        var current = await Lock(operation, cancellationToken); if (current is null) return false;
        var job = await db.OperationJobs.SingleAsync(item => item.Id == current.Id, cancellationToken);
        var channel = await LockChannel(current.ChannelId, cancellationToken);
        if (state == "Succeeded")
        {
            var alias = "wd-" + current.ChannelId.ToString("N");
            if (receipt is null || !receipt.Applied || !receipt.BindingCurrent || receipt.OperationId != current.Id || receipt.DestinationReference != alias ||
                receipt.HasCredential != (receipt.CredentialReference is not null) || receipt.CredentialReference is not null && receipt.CredentialReference != alias) throw new InvalidOperationException("Private root receipt is incompatible.");
            if (channel.Version != current.ChannelVersion || channel.IsEnabled) { state = "Superseded"; code = "PRIVATE_CHANNEL_CHANGED"; }
            else await db.NotificationChannels.Where(item => item.Id == channel.Id).ExecuteUpdateAsync(setters => setters.SetProperty(item => item.DestinationReference, alias)
                .SetProperty(item => item.CredentialReference, receipt.CredentialReference).SetProperty(item => item.IsEnabled, false)
                .SetProperty(item => item.PrivateConfigurationBlocked, !receipt.HasTarget).SetProperty(item => item.Version, item => item.Version + 1), cancellationToken);
        }
        var now = DateTimeOffset.UtcNow; var terminal = state != "Unknown";
        await db.NotificationPrivateOperations.Where(item => item.Id == current.Id).ExecuteUpdateAsync(setters => setters.SetProperty(item => item.State, state)
            .SetProperty(item => item.ResultCode, code).SetProperty(item => item.CompletedAtUtc, terminal ? now : null)
            .SetProperty(item => item.LeaseToken, (Guid?)null).SetProperty(item => item.LeaseExpiresAtUtc, (DateTimeOffset?)null)
            .SetProperty(item => item.NextAttemptAtUtc, now.AddMinutes(1)).SetProperty(item => item.Version, item => item.Version + 1), cancellationToken);
        var phase = terminal ? state == "Succeeded" ? "Completed" : code : "AwaitingPrivateReceipt";
        if (job.Phase != phase || terminal)
        {
            job.State = state == "Succeeded" ? "Succeeded" : state == "Canceled" ? "Canceled" : terminal ? "Failed" : "Running";
            job.Phase = phase; job.ProgressPercent = state == "Succeeded" ? (short)100 : null; job.ErrorCode = state == "Succeeded" ? null : code;
            job.CompletedAtUtc = terminal ? now : null; job.Version++;
            var sequence = (await db.OperationJobEvents.Where(item => item.JobId == job.Id).MaxAsync(item => (long?)item.Sequence, cancellationToken) ?? 0) + 1;
            db.OperationJobEvents.Add(new() { JobId = job.Id, Sequence = sequence, State = job.State, Phase = phase, MessageCode = code });
            Audit(current.ActorSubject, "notification.private.result", current.Id, channel.Id, null, null, state);
        }
        if (terminal) await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE outbox_messages SET processed_at_utc={now} WHERE message_type IN ('NotificationPrivateRequested','NotificationPrivateCancellationRequested') AND payload_json->>'jobId'={current.Id.ToString("D")}", cancellationToken);
        await db.SaveChangesAsync(cancellationToken); return true;
    }, cancellationToken);
    public Task DeferAsync(NotificationPrivateOperation operation, string code, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var current = await Lock(operation, cancellationToken); if (current is null) return false;
        await db.NotificationPrivateOperations.Where(item => item.Id == current.Id).ExecuteUpdateAsync(setters => setters.SetProperty(item => item.ResultCode, code)
            .SetProperty(item => item.NextAttemptAtUtc, DateTimeOffset.UtcNow.AddSeconds(90)).SetProperty(item => item.Version, item => item.Version + 1), cancellationToken);
        return true;
    }, cancellationToken);
    private async Task<NotificationPrivateOperation?> Lock(NotificationPrivateOperation item, CancellationToken cancellationToken)
    {
        var current = await db.NotificationPrivateOperations.FromSqlInterpolated($"SELECT * FROM notification_private_operations WHERE id={item.Id} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        return current?.LeaseToken == item.LeaseToken && current?.CompletedAtUtc is null ? current : null;
    }
    private Task<NotificationChannel> LockChannel(Guid id, CancellationToken cancellationToken) => db.NotificationChannels.FromSqlInterpolated($"SELECT * FROM notification_channels WHERE id={id} FOR UPDATE").AsNoTracking().SingleAsync(cancellationToken);
    private void Audit(string actor, string action, Guid job, Guid channel, string? trace, string? source, string state) => db.AuditEvents.Add(new() { ActorSubject = actor,
        Action = action, TargetType = "notification-channel", TargetId = channel.ToString("D"), JobId = job, TraceId = trace ?? job.ToString("D"), SourceIp = source, Result = state, DetailJson = "{}" });
    private static JobDto Map(OperationJob job) => new(job.Id, job.JobType, job.State, job.Phase, job.ProgressPercent, job.ErrorCode, job.CreatedAtUtc, job.CompletedAtUtc, null);
    private Task<T> Transaction<T>(Func<Task<T>> operation, CancellationToken cancellationToken) => db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
    {
        db.ChangeTracker.Clear(); await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='3s'; SET LOCAL statement_timeout='10s';", cancellationToken);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(41307303)", cancellationToken);
        var result = await operation(); await transaction.CommitAsync(cancellationToken); return result;
    });
}
