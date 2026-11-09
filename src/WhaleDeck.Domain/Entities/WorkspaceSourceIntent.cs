namespace WhaleDeck.Domain.Entities;

public sealed class WorkspaceSourceIntent
{
    public Guid Id { get; init; }
    public Guid WorkspaceId { get; init; }
    public string OwnerSubject { get; init; } = "";
    public string LoginBinding { get; init; } = "";
    public string WorkspaceJson { get; init; } = "";
    public string Format { get; init; } = "";
    public int ArchiveBytes { get; init; }
    public string ArchiveSha256 { get; init; } = "";
    public string State { get; set; } = "";
    public int ReceivedBytes { get; set; }
    public string? PlanSha256 { get; set; }
    public string? ContentSha256 { get; set; }
    public int? FileCount { get; set; }
    public long? TotalBytes { get; set; }
    public string? Code { get; set; }
    public Guid? Lease { get; set; }
    public DateTimeOffset? LeaseUntilUtc { get; set; }
    public DateTimeOffset AvailableAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; init; }
    public DateTimeOffset? FinishedAtUtc { get; set; }
}

public sealed class WorkspaceSourceChunk
{
    public Guid ImportId { get; init; }
    public int Offset { get; init; }
    public byte[] SealedContent { get; init; } = [];
}
