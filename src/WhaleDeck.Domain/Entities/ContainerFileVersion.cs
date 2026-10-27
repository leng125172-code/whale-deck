namespace WhaleDeck.Domain.Entities;

/// <summary>Permanent metadata only. Presence is not a full content verification.</summary>
public sealed class ContainerFileVersion
{
    public Guid Id { get; init; }
    public required string ContainerId { get; init; }
    public required string OriginalInstanceId { get; init; }
    public required string RelativePath { get; init; }
    public required string SourceVersion { get; init; }
    public required string Sha256 { get; init; }
    public required string CaptureHash { get; set; }
    public long SizeBytes { get; init; }
    public long CapturedAtUnixMs { get; init; }
    public bool DifferentSourceDevice { get; init; }
    public required string Availability { get; set; }
    public DateTimeOffset LastObservedAtUtc { get; set; }
    public long Version { get; set; }
}
