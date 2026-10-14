using Google.Protobuf;
using Grpc.Core;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Infrastructure.Agent;

public sealed partial class AgentGateway
{
    public async Task<NotificationPrivateMetadata> InspectAsync(Guid channelId, CancellationToken cancellationToken)
    {
        if (channelId == Guid.Empty) throw new ArgumentException("A notification channel is required.");
        await RequirePrivateConfiguration(cancellationToken);
        var response = await new NotificationConfigurationService.NotificationConfigurationServiceClient(_channel).InspectAsync(
            new NotificationConfigurationLookup { ChannelId = channelId.ToString("D") },
            Headers("/whaledeck.agent.v1.NotificationConfigurationService/Inspect"), deadline: DateTime.UtcNow.AddSeconds(15), cancellationToken: cancellationToken);
        return MapPrivate(response);
    }

    public async Task<NotificationPrivateMetadata> ApplyAsync(NotificationPrivateUpdate update, CancellationToken cancellationToken)
    {
        if (update.ChannelId == Guid.Empty || update.OperationId == Guid.Empty || update.Action is not ("apply" or "remove") ||
            string.IsNullOrWhiteSpace(update.ActorSubject) || update.PrivateJson.Length > 65536) throw new ArgumentException("Invalid notification private update.");
        await RequirePrivateConfiguration(cancellationToken);
        var request = new NotificationConfigurationRequest
        {
            ChannelId = update.ChannelId.ToString("D"), OperationId = update.OperationId.ToString("D"), ExpectedRevision = update.ExpectedRevision,
            Action = update.Action, PrivateJson = ByteString.CopyFromUtf8(update.PrivateJson), Context = new RequestContext { ActorSubject = update.ActorSubject }
        };
        var response = await new NotificationConfigurationService.NotificationConfigurationServiceClient(_channel).ApplyAsync(request,
            Headers("/whaledeck.agent.v1.NotificationConfigurationService/Apply"), deadline: DateTime.UtcNow.AddSeconds(15), cancellationToken: cancellationToken);
        return MapPrivate(response);
    }

    private async Task RequirePrivateConfiguration(CancellationToken cancellationToken)
    {
        var capabilities = await GetCapabilitiesAsync(cancellationToken);
        if (capabilities.ProtocolMajor != 1 || capabilities.ProtocolMinor < 17 || !capabilities.Capabilities.Contains("notifications.private-configuration"))
            throw new RpcException(new Status(StatusCode.Unimplemented, "Notification private configuration capability is unavailable."));
    }
    private static NotificationPrivateMetadata MapPrivate(NotificationConfigurationMetadata item) => new(item.Revision, item.DestinationReference,
        string.IsNullOrEmpty(item.CredentialReference) ? null : item.CredentialReference, item.HasTarget, item.HasCredential, item.Applied, item.Replayed,
        Guid.TryParse(item.OperationId, out var operation) ? operation : null);
}
