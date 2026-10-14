namespace WhaleDeck.Application.Abstractions;

/// <summary>Private values travel only as transient UDS input, never in job/audit JSON.</summary>
public interface INotificationPrivateGateway
{
    Task<NotificationPrivateMetadata> InspectAsync(Guid channelId, CancellationToken cancellationToken);
    Task<NotificationPrivateMetadata> InspectReceiptAsync(Guid channelId, Guid operationId, CancellationToken cancellationToken);
    Task<NotificationPrivateMetadata> ApplyAsync(NotificationPrivateUpdate update, CancellationToken cancellationToken);
}

public sealed record NotificationPrivateMetadata(string Revision, string DestinationReference, string? CredentialReference,
    bool HasTarget, bool HasCredential, bool Applied, bool Replayed, Guid? OperationId, bool BindingCurrent);

public sealed class NotificationPrivateUpdate
{
    public required Guid ChannelId { get; init; }
    public required Guid OperationId { get; init; }
    public required string ExpectedRevision { get; init; }
    public required string Action { get; init; }
    public required string ActorSubject { get; init; }
    public required string PrivateJson { get; init; }
    public override string ToString() => "NotificationPrivateUpdate [private]";
}
