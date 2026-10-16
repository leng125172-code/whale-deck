using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Abstractions;

public interface INotificationPrivateRepository
{
    Task<NotificationChannel> ChannelAsync(Guid id, CancellationToken cancellationToken);
    Task<JobDto?> ReplayAsync(Guid id, string actor, string requestHash, CancellationToken cancellationToken);
    Task<JobDto> EnqueueAsync(NotificationContext context, Guid id, NotificationPrivatePlanClaims claims, string requestHash, CancellationToken cancellationToken);
    Task<NotificationPrivateOperation?> ClaimAsync(DateTimeOffset now, CancellationToken cancellationToken);
    Task<NotificationPrivateStartResult> StartedAsync(NotificationPrivateOperation operation, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> FinishAsync(NotificationPrivateOperation operation, string state, string code, NotificationPrivateMetadata? receipt, CancellationToken cancellationToken);
    Task DeferAsync(NotificationPrivateOperation operation, string code, CancellationToken cancellationToken);
    Task<bool> CancellationRequestedAsync(Guid id, CancellationToken cancellationToken);
    Task<NotificationPrivateOperation?> LatestAsync(Guid channelId, CancellationToken cancellationToken);
    Task<bool> ReplayAcknowledgmentAsync(NotificationContext context, Guid channelId, Guid operationId, long expectedVersion, CancellationToken cancellationToken);
    Task AcknowledgeAsync(NotificationContext context, Guid channelId, Guid operationId, long expectedVersion, CancellationToken cancellationToken);
}

public interface INotificationPrivateInputStore
{
    Task PutAsync(string owner, Guid operationId, string input, DateTimeOffset expiresAt, CancellationToken cancellationToken);
    Task<string?> GetAsync(string owner, Guid operationId, CancellationToken cancellationToken);
    Task DeleteAsync(string owner, Guid operationId, CancellationToken cancellationToken);
}

public interface INotificationPrivateProtector
{
    string Fingerprint(string owner, Guid channelId, string action, string input);
    string Sign(NotificationPrivatePlanClaims claims);
    NotificationPrivatePlanClaims Verify(string plan);
}

public interface INotificationPrivateValidator
{
    void Validate(string provider, string action, string input, bool hasCredential);
}
