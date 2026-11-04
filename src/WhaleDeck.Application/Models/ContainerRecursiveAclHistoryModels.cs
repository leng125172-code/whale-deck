namespace WhaleDeck.Application.Models;

/// <summary>Counts belong to the captured plan, not its execution outcome.</summary>
public sealed record ContainerRecursiveAclHistorySummaryDto(string CaptureId, string HistoryDigest, long CreatedAtUnixMs,
    string SourceInstanceId, string ChangedAclType, string ManifestDigest, uint EntryCount, uint FileCount, uint DirectoryCount, uint ChangeCount);
public sealed record ContainerRecursiveAclHistoryPageDto(ContainerFileLocationDto Location, string Version,
    IReadOnlyList<ContainerRecursiveAclHistorySummaryDto> Entries, string NextAfter, int ScannedCount, int InvalidCount, bool Complete)
{ public override string ToString()=>nameof(ContainerRecursiveAclHistoryPageDto); }
public sealed record ContainerRecursiveAclHistoryEntryDto(string RelativePath, string Kind, uint OriginalMode,
    IReadOnlyList<ContainerAclEntryDto> AccessAcl, IReadOnlyList<ContainerAclEntryDto> DefaultAcl)
{ public override string ToString()=>nameof(ContainerRecursiveAclHistoryEntryDto); }
public sealed record ContainerRecursiveAclHistorySnapshotDto(ContainerFileLocationDto Location, ContainerRecursiveAclHistorySummaryDto Summary,
    uint Offset, IReadOnlyList<ContainerRecursiveAclHistoryEntryDto> Entries, bool Complete, uint? NextOffset)
{ public override string ToString()=>nameof(ContainerRecursiveAclHistorySnapshotDto); }
