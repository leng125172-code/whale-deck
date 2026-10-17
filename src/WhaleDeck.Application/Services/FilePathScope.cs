using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Services;

/// <summary>Linux path hierarchy; separators, not textual prefixes, define ancestry.</summary>
public sealed record FilePathScope
{
    public string RootId { get; }
    public string Path { get; }
    public string Hash => FileManagementService.Hash(RootId + ":" + Path);

    public FilePathScope(FileLocationDto location)
    {
        FileManagementService.Location(location);
        RootId = location.RootId;
        Path = location.Path;
    }

    public bool Conflicts(FilePathScope other) => RootId == other.RootId &&
        (Path == other.Path || Path.StartsWith(other.Path + "/", StringComparison.Ordinal) || other.Path.StartsWith(Path + "/", StringComparison.Ordinal));
}
