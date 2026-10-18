using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Abstractions;

public interface IFileBrowserGateway
{
    Task<FilePageDto> DirectoriesAsync(FileLocationDto location, string after, int take, CancellationToken cancellationToken);
    Task<FileSearchPage> SearchAsync(FileSearchCommand command, CancellationToken cancellationToken);
    Task<FileContentSearchPage> SearchContentAsync(FileContentSearchCommand command, CancellationToken cancellationToken);
}
