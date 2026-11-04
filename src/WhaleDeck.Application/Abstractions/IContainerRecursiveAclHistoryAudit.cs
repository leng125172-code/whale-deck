using WhaleDeck.Application.Models;
namespace WhaleDeck.Application.Abstractions;

/// <summary>Durable read audit without raw paths, ACLs, private plans or credentials.</summary>
public interface IContainerRecursiveAclHistoryAudit
{
    Task ListAsync(TerminalActor actor, ContainerFileLocationDto location, string after, int take, string expectedVersion,
        ContainerRecursiveAclHistoryPageDto result, CancellationToken cancellationToken);
    Task ReadAsync(TerminalActor actor, ContainerFileLocationDto location, string captureId, string digest, int offset, int take,
        ContainerRecursiveAclHistorySnapshotDto result, CancellationToken cancellationToken);
}
