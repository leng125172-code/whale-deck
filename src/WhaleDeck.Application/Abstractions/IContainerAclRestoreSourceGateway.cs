using WhaleDeck.Application.Models;
namespace WhaleDeck.Application.Abstractions;

/// <summary>Same read-only history RPC. Missing/changed selected source is distinct from a transient outage.</summary>
public interface IContainerAclRestoreSourceGateway
{
    Task<ContainerAclHistorySnapshotDto> ReadContainerAclRestoreSourceAsync(TerminalActor actor, ContainerFileLocationDto location,
        string captureId, string expectedHistoryDigest, CancellationToken cancellationToken);
}
