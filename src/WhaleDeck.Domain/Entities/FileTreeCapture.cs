namespace WhaleDeck.Domain.Entities;

/// <summary>Immutable authenticated catalogue of a verified private HDD directory capture; not proof of source removal.</summary>
public sealed class FileTreeCapture
{
    public Guid Id { get; init; }
    public Guid IntentId { get; init; }
    public string Kind { get; init; } = "Trash";
    public string? ContentSha256 { get; init; }
    public required string OwnerHash { get; init; }
    public required string SourceHash { get; init; }
    public required string SourceJson { get; init; }
    public required string ManifestSha256 { get; init; }
    public int EntryCount { get; init; }
    public long TotalBytes { get; init; }
    public long ArchiveBytes { get; init; }
    public required string ArchiveSha256 { get; init; }
    public required string ProofHash { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
}
