using WhaleDeck.Application.Models;
namespace WhaleDeck.Application.Abstractions;

/// <summary>Independent metadata read transport; caller must separately authorize the current human and resource.</summary>
public interface IContainerAclHistoryGateway
{
    Task RequireContainerAclHistoryAsync(CancellationToken cancellationToken);
    Task<ContainerAclHistoryPageDto> ListContainerAclHistoryAsync(TerminalActor actor, ContainerFileLocationDto location,
        string after, int take, string expectedVersion, CancellationToken cancellationToken);
    Task<ContainerAclHistorySnapshotDto> ReadContainerAclHistoryAsync(TerminalActor actor, ContainerFileLocationDto location,
        string captureId, string expectedHistoryDigest, CancellationToken cancellationToken);
}
