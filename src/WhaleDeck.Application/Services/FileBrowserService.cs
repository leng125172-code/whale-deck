using System.Text;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Services;

public sealed class FileBrowserService(FileManagementService files, IFileBrowserGateway gateway)
{
    public async Task<FilePageDto> DirectoriesAsync(TerminalActor actor, FileLocationDto location, string after, int take, CancellationToken cancellationToken)
    {
        await files.AuthorizeAsync(actor, cancellationToken); FileManagementService.Location(location, true);
        if (take is < 1 or > 100 || after.Any(char.IsControl) || after.Contains('/', StringComparison.Ordinal) || after.Contains('\\', StringComparison.Ordinal) ||
            after is "." or ".." || Encoding.UTF8.GetByteCount(after) > 255) throw new ArgumentException("FILES_LIST_INVALID");
        return await gateway.DirectoriesAsync(location, after, take, cancellationToken);
    }
    public async Task<FileSearchPage> SearchAsync(TerminalActor actor, FileSearchCommand command, CancellationToken cancellationToken)
    {
        await files.AuthorizeAsync(actor, cancellationToken); Validate(command);
        return await gateway.SearchAsync(command, cancellationToken);
    }
    public static void Validate(FileSearchCommand command)
    {
        FileManagementService.Location(new(command.RootId, command.Path), true);
        if (string.IsNullOrWhiteSpace(command.Query) || Encoding.UTF8.GetByteCount(command.Query) > 256 || command.Query.Any(char.IsControl) ||
            command.Query.Contains('/', StringComparison.Ordinal) || command.Query.Contains('\\', StringComparison.Ordinal) || command.Kind is not ("All" or "File" or "Directory") ||
            command.Take is < 1 or > 100 || command.Cursor.Length > 32768 || command.Cursor.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.')))
            throw new ArgumentException("FILES_SEARCH_INVALID");
    }

    public async Task<FileContentSearchPage> SearchContentAsync(TerminalActor actor, FileContentSearchCommand command, CancellationToken cancellationToken)
    {
        await files.AuthorizeAsync(actor, cancellationToken);
        ValidateContent(command);
        cancellationToken.ThrowIfCancellationRequested();
        var result = await gateway.SearchContentAsync(command, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        // Content snippets are private reads too: do not return a completed
        // scan after the caller's current administrator authority has expired.
        await files.AuthorizeAsync(actor, cancellationToken);
        return result;
    }

    public static void ValidateContent(FileContentSearchCommand command)
    {
        FileManagementService.Location(new(command.RootId, command.Path), true);
        if (string.IsNullOrWhiteSpace(command.Query) || new UTF8Encoding(false, true).GetByteCount(command.Query) > 256 || command.Query.Any(char.IsControl) ||
            command.Take is < 1 or > 100 || command.MaxFileBytes is < 1 or > 16 * 1024 * 1024 ||
            command.Cursor.Length > 32768 || command.Cursor.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.')))
            throw new ArgumentException("FILES_SEARCH_INVALID");
    }
}
