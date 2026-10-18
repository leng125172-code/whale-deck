namespace WhaleDeck.Application.Models;

public sealed record FileSearchCommand(string RootId, string Path, string Query, string Kind = "All", bool MatchCase = false, string Cursor = "", int Take = 50);
public sealed record FileSearchHit(string Path, FileEntryDto Entry);
public sealed record FileSearchPage(IReadOnlyCollection<FileSearchHit> Entries, string NextCursor, int ScannedEntries,
    int ExcludedEntries, bool Complete, string StopReason, bool DepthLimited);

public sealed record FileContentSearchCommand(string RootId, string Path, string Query, bool MatchCase = false,
    string Cursor = "", int Take = 50, int MaxFileBytes = 16 * 1024 * 1024);
public sealed record FileContentSearchHit(string Path, FileEntryDto Entry, int LineNumber, string Excerpt, bool ExcerptTruncated);
public sealed record FileContentSearchPage(IReadOnlyCollection<FileContentSearchHit> Entries, string NextCursor,
    int ScannedEntries, int ExcludedEntries, bool Complete, string StopReason, bool DepthLimited,
    int ScannedFiles, long ReadBytes, int SkippedNonTextFiles, int SkippedOversizedFiles);
