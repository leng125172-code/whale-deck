namespace WhaleDeck.Domain.Entities;

/// <summary>Metadata catalog for an exact root-owned HDD capture. Never stores file bytes or host paths.</summary>
public sealed class FileVersion
{
    public Guid Id { get; init; }
    public Guid? SourceJobId { get; set; }
    public string? CreatedBySubject { get; set; }
    public required string RootId { get; init; }
    public required string RelativePath { get; init; }
    public required string TargetHash { get; init; }
    public required string SourceVersion { get; init; }
    public required string Sha256 { get; init; }
    public long SizeBytes { get; init; }
    public required string CaptureKind { get; init; }
    public string CaptureState { get; set; } = "Available";
    public string Availability { get; set; } = "Present";
    public bool DifferentSourceDevice { get; init; }
    public DateTimeOffset CapturedAtUtc { get; init; }
    public DateTimeOffset LastObservedAtUtc { get; set; }
    public DateTimeOffset? LastFullVerificationAtUtc { get; set; }
    public Guid? LastOperationId { get; set; }
    public long Version { get; set; }
}
