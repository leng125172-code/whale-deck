namespace WhaleDeck.Domain.Entities;

public sealed class WorkspaceReportGrantRecord
{
    public Guid WorkspaceId { get; init; }
    public Guid GroupId { get; init; }
    public bool Enabled { get; init; }
    public long Version { get; init; }
}
