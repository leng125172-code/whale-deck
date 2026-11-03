namespace WhaleDeck.Application.Models;

public sealed record ContainerAclHistoryEntryDto(string CaptureId, string HistoryDigest, long CreatedAtUnixMs,
    string SourceInstanceId, string ChangedAclType, string Kind, uint OriginalMode, uint AccessEntryCount, uint DefaultEntryCount);
public sealed record ContainerAclHistoryPageDto(ContainerFileLocationDto Location, string Version,
    IReadOnlyList<ContainerAclHistoryEntryDto> Entries, string NextAfter, int ScannedCount, int InvalidCount, bool Complete)
{ public override string ToString() => nameof(ContainerAclHistoryPageDto); }
public sealed record ContainerAclHistorySnapshotDto(ContainerFileLocationDto Location, ContainerAclHistoryEntryDto Entry,
    IReadOnlyList<ContainerAclEntryDto> AccessAcl, IReadOnlyList<ContainerAclEntryDto> DefaultAcl)
{ public override string ToString() => nameof(ContainerAclHistorySnapshotDto); }

/// <summary>Administrator recovery metadata only; never exposes the private source snapshot or an authorization proof.</summary>
public sealed record ContainerAclRestoreSourceSummaryDto(string CaptureId, string ChangedAclType, string Kind,
    long CreatedAtUnixMs, uint OriginalMode, uint AccessEntryCount, uint DefaultEntryCount)
{ public override string ToString() => nameof(ContainerAclRestoreSourceSummaryDto); }
