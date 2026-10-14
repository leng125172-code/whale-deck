namespace WhaleDeck.Application.Models;

public sealed record NotificationPrivatePlanCommand(long ExpectedChannelVersion, string Action, string? InputTicket = null);
public sealed record NotificationPrivateSubmitCommand(string PlanHash, string? InputTicket = null, bool Confirmed = false);
public sealed record NotificationPrivatePlanClaims(string Actor, Guid ChannelId, long ChannelVersion, string Provider, string Action,
    string RegistryRevision, string InputFingerprint, DateTimeOffset ExpiresAtUtc);
public sealed record NotificationPrivatePlanDto(string PlanHash, DateTimeOffset ExpiresAtUtc, bool HasTarget, bool HasCredential, string Warning);
public sealed record NotificationPrivateStatusDto(Guid JobId, string State, string? ResultCode, long Version, bool CanAcknowledgeUnknown);
public sealed record AcknowledgeNotificationPrivateCommand(Guid OperationId, long ExpectedVersion, bool Confirmed = false, bool AcceptUnknownOutcome = false);
