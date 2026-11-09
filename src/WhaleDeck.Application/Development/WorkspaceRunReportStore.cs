using System.Text.Json;

namespace WhaleDeck.Application.Development;

public sealed record WorkspaceRunJobBinding(
    Guid RunId, string TraceId, string SourceFingerprint, string EnvironmentDigest,
    long EnvironmentVersion, int RetentionDays)
{
    public const string JobType = "development.workspace.run";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string ToJobRequestJson() => JsonSerializer.Serialize(new { workspaceRun = this }, Json);
}

public enum WorkspaceReportWriteState { Created, AlreadyStored, Conflict, SourceUnavailable, Expired }
public enum WorkspaceReportFormat { Json, Markdown }
public sealed record WorkspaceReportDownload(byte[] Content, string Sha256, DateTimeOffset RetainUntilUtc);

/// <summary>Server-side report storage. The caller must establish workspace authorization before reads.</summary>
public interface IWorkspaceRunReportStore
{
    Task<WorkspaceReportWriteState> SaveAsync(WorkspaceRunReport report, CancellationToken cancellationToken);
    Task<WorkspaceReportDownload?> ReadAsync(
        Guid workspaceId, Guid runId, WorkspaceReportFormat format, CancellationToken cancellationToken);
    Task<int> ExpireAsync(int limit, CancellationToken cancellationToken);
}
