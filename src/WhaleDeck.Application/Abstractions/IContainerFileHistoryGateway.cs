using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Abstractions;

public interface IContainerFileHistoryGateway
{
    Task<ContainerHistoryPageDto> ListContainerHistoryAsync(ContainerFileLocationDto location, string after,
        int take, string expectedVersion, CancellationToken cancellationToken);

    /// <summary>
    /// Only a later permanent recovery workflow may consume this source; the stream must finish completely
    /// before that workflow may publish restored content.
    /// </summary>
    IAsyncEnumerable<ReadOnlyMemory<byte>> ReadContainerHistorySourceAsync(ContainerFileLocationDto location,
        ContainerHistoryEntryDto expected, CancellationToken cancellationToken);
}
