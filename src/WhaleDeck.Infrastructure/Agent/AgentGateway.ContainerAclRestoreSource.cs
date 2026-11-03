using Grpc.Core;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;

namespace WhaleDeck.Infrastructure.Agent;

public sealed partial class AgentGateway : IContainerAclRestoreSourceGateway
{
    public async Task<ContainerAclHistorySnapshotDto> ReadContainerAclRestoreSourceAsync(TerminalActor actor, ContainerFileLocationDto location,
        string captureId, string expectedHistoryDigest, CancellationToken cancellationToken)
    {
        try
        {
            await RequireContainerAclHistoryAsync(cancellationToken);
            return await ReadContainerAclHistoryAsync(actor, location, captureId, expectedHistoryDigest, cancellationToken);
        }
        catch (RpcException error) when (error.StatusCode is StatusCode.NotFound or StatusCode.Aborted)
        { throw new ContainerAclRestoreSourceChangedException(); }
    }
}
