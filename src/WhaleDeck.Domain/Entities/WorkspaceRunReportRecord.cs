namespace WhaleDeck.Domain.Entities;

public sealed class WorkspaceRunReportRecord
{
    public Guid RunId { get; init; }
    public Guid WorkspaceId { get; init; }
    public Guid JobId { get; init; }
    public int SchemaVersion { get; init; }
    public DateTimeOffset FinalizedAtUtc { get; init; }
    public DateTimeOffset RetainUntilUtc { get; init; }
    public DateTimeOffset? ExpiredAtUtc { get; init; }
    public byte[]? JsonContent { get; init; }
    public byte[]? MarkdownContent { get; init; }
    public required string JsonSha256 { get; init; }
    public required string MarkdownSha256 { get; init; }
}
