namespace WhaleDeck.Application.Models;

public sealed record FileSearchCommand(string RootId, string Path, string Query, string Kind = "All", bool MatchCase = false, string Cursor = "", int Take = 50);
public sealed record FileSearchHit(string Path, FileEntryDto Entry);
public sealed record FileSearchPage(IReadOnlyCollection<FileSearchHit> Entries, string NextCursor, int ScannedEntries,
    int ExcludedEntries, bool Complete, string StopReason, bool DepthLimited);
