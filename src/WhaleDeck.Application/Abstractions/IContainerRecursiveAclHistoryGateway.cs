using WhaleDeck.Application.Models;
namespace WhaleDeck.Application.Abstractions;

/// <summary>Independent metadata transport; human/resource authorization must precede all calls.</summary>
public interface IContainerRecursiveAclHistoryGateway
{
    Task RequireContainerRecursiveAclHistoryAsync(CancellationToken cancellationToken);
    Task<ContainerRecursiveAclHistoryPageDto> ListContainerRecursiveAclHistoryAsync(TerminalActor actor, ContainerFileLocationDto location,
        string after, int take, string expectedVersion, CancellationToken cancellationToken);
    Task<ContainerRecursiveAclHistorySnapshotDto> ReadContainerRecursiveAclHistoryAsync(TerminalActor actor, ContainerFileLocationDto location,
        string captureId, string expectedHistoryDigest, int offset, int take, CancellationToken cancellationToken);
}
