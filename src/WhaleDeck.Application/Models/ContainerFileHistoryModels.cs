namespace WhaleDeck.Application.Models;

/// <summary>Historical container file metadata; Availability "Present" does not mean the content has been fully verified.</summary>
public sealed record ContainerHistoryEntryDto(string CaptureId, string ContainerId, string OriginalInstanceId,
    string Path, string SourceVersion, long SizeBytes, string Sha256, long CreatedAtUnixMs,
    bool DifferentSourceDevice, string Availability, string CaptureHash);

public sealed record ContainerHistoryPageDto(ContainerFileLocationDto Location, string TargetProof,
    IReadOnlyList<ContainerHistoryEntryDto> Entries, string Version, string NextCursor, int Scanned,
    int InvalidRecords, bool Complete);
