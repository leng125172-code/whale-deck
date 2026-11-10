using System.Security.Cryptography;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Development;

public sealed record WorkspaceSourceInput(string Format, int ArchiveBytes, string ArchiveSha256);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WorkspaceGitInput(string RepositoryId, string Ref,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? PreviousImportId = null);
public sealed record WorkspaceGitRepositoryGrant(string[] Subjects, string? CredentialRef = null);
public sealed record WorkspaceGitProof(string ResolvedObject, string Commit, WorkspaceSourceInput Archive, int FileCount, long TotalBytes);
public sealed record WorkspaceGitSource(string RepositoryId, string Ref, WorkspaceGitProof? Snapshot);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WorkspaceSourceConfirmation(string PlanSha256,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ConflictResolution = null);
public sealed record WorkspaceSourceUpdatePlan(string PreviousContentSha256, string WorkContentSha256, bool Dirty,
    int AddedEntries, int RemovedEntries, int ChangedEntries);
public sealed record WorkspaceSourcePlan(string PlanSha256, string ContentSha256, int FileCount, long TotalBytes,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] WorkspaceSourceUpdatePlan? Update = null);
public sealed record WorkspaceSourceView(Guid WorkspaceId, Guid ImportId, string State, WorkspaceSourceInput Archive,
    int ReceivedBytes, WorkspaceSourcePlan? Plan, string? Code, DateTimeOffset ExpiresAtUtc,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] WorkspaceGitSource? Git = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? FinishedAtUtc = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? PreviousImportId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ConflictResolution = null);
public sealed record WorkspaceSourceClaim(WorkspaceSourceView Import, WorkspaceDefinition Workspace, TerminalActor Actor, Guid Lease,
    string? GitCredentialRef = null, string? BaseContentSha256 = null);
public sealed record WorkspaceSourceObservation(string State, WorkspaceSourcePlan Plan);
public sealed record WorkspaceSourceHistoryPage(WorkspaceSourceView[] Items, Guid? NextBefore);

public enum WorkspaceSourceFailure { Missing, Rejected, Conflict, Retryable, Integrity, Unsupported }
public sealed class WorkspaceSourceException(WorkspaceSourceFailure failure, string? hostCode = null) : Exception("WORKSPACE_SOURCE_AGENT_" + failure)
{
    public WorkspaceSourceFailure Failure { get; } = failure;
    public string? HostCode { get; } = hostCode;
}

public interface IWorkspaceSourceGateway
{
    Task AcquireGitAsync(WorkspaceSourceClaim claim, Func<WorkspaceGitProof, CancellationToken, Task> bind,
        Func<int, byte[], CancellationToken, Task> append, CancellationToken token);
    Task<WorkspaceSourceObservation> StageSourceAsync(WorkspaceSourceClaim claim, IAsyncEnumerable<ReadOnlyMemory<byte>> chunks, CancellationToken token);
    Task<WorkspaceSourceObservation> InspectSourceAsync(WorkspaceSourceClaim claim, CancellationToken token);
    Task<WorkspaceSourceObservation> PublishSourceAsync(WorkspaceSourceClaim claim, WorkspaceSourcePlan plan, CancellationToken token);
    Task<WorkspaceSourceObservation> DiscardSourceAsync(WorkspaceSourceClaim claim, WorkspaceSourcePlan plan, CancellationToken token);
    Task AbortSourceAsync(WorkspaceSourceClaim claim, CancellationToken token);
}

public interface IWorkspaceSourceStore
{
    Task<WorkspaceSourceView> BeginAsync(WorkspaceDefinition workspace, Guid importId, TerminalActor actor, WorkspaceSourceInput input, CancellationToken token);
    Task<WorkspaceSourceView> BeginGitAsync(WorkspaceDefinition workspace, Guid importId, TerminalActor actor,
        WorkspaceGitInput input, string? credentialRef, CancellationToken token);
    Task<WorkspaceSourceView?> FindAsync(Guid workspaceId, Guid importId, string subject, CancellationToken token);
    Task<WorkspaceSourceView?> CurrentAsync(Guid workspaceId, string subject, CancellationToken token);
    Task<WorkspaceSourceView?> PublishedAsync(Guid workspaceId, string subject, CancellationToken token);
    Task<WorkspaceSourceHistoryPage> HistoryAsync(Guid workspaceId, string subject, Guid? before, int take, CancellationToken token);
    Task<WorkspaceSourceView> AppendAsync(Guid workspaceId, Guid importId, TerminalActor actor, int offset, byte[] body, CancellationToken token);
    Task<WorkspaceSourceView> ConfirmAsync(Guid workspaceId, Guid importId, TerminalActor actor, WorkspaceSourceConfirmation input, CancellationToken token);
    Task<WorkspaceSourceView> CancelAsync(Guid workspaceId, Guid importId, TerminalActor actor, CancellationToken token);
    Task<WorkspaceSourceClaim?> ClaimAsync(CancellationToken token);
    Task<bool> IsCurrentAsync(WorkspaceSourceClaim claim, CancellationToken token);
    Task BindGitAsync(WorkspaceSourceClaim claim, WorkspaceGitProof proof, CancellationToken token);
    Task AppendGitAsync(WorkspaceSourceClaim claim, int offset, byte[] body, CancellationToken token);
    Task CompleteGitAsync(WorkspaceSourceClaim claim, CancellationToken token);
    IAsyncEnumerable<ReadOnlyMemory<byte>> ReadChunksAsync(WorkspaceSourceClaim claim, CancellationToken token);
    Task AdvanceAsync(WorkspaceSourceClaim claim, string state, WorkspaceSourcePlan? plan, string? code, CancellationToken token);
    Task DeferAsync(WorkspaceSourceClaim claim, string? code, CancellationToken token);
}

public sealed partial class WorkspaceSources(WorkspaceRuns access, IWorkspaceSourceStore store, WorkspaceRegistrationPolicy policy)
{
    private static readonly SemaphoreSlim ChunkReaders = new(8, 8);
    public const int ChunkBytes = 1024 * 1024;
    public const int ArchiveLimit = 128 * ChunkBytes;
    public const int ReservedBytesLimit = 512 * ChunkBytes;

    public const string Uploading = "Uploading";
    public const string Acquiring = "Acquiring";
    public const string Ready = "Ready";
    public const string Staged = "Staged";
    public const string Publishing = "Publishing";
    public const string Canceling = "Canceling";
    public const string Published = "Published";
    public const string Canceled = "Canceled";
    public const string Failed = "Failed";
    public const string CleanOnly = "clean-only";
    public const string RetainAndReplace = "retain-and-replace";

    public async Task<WorkspaceSourceView> BeginAsync(TerminalActor actor, Guid workspaceId, Guid importId, WorkspaceSourceInput input,
        CancellationToken token)
    {
        var workspace = await RequireAsync(actor, workspaceId, importId, token);
        if (input.Format is not ("zip" or "tar" or "tar.gz") || input.ArchiveBytes is <= 0 or > ArchiveLimit || !Digest(input.ArchiveSha256))
            throw new ArgumentException("WORKSPACE_SOURCE_ARCHIVE_INVALID");
        return await store.BeginAsync(workspace, importId, actor, input, token);
    }

    public async Task<WorkspaceSourceView> BeginGitAsync(TerminalActor actor, Guid workspaceId, Guid importId,
        WorkspaceGitInput input, CancellationToken token)
    {
        var workspace = await RequireAsync(actor, workspaceId, importId, token);
        if (input.PreviousImportId == Guid.Empty || input.PreviousImportId == importId ||
            input.RepositoryId is not { Length: > 0 and <= 48 } || !RepositoryId().IsMatch(input.RepositoryId) ||
            input.Ref is not { Length: > 0 and <= 255 } ||
            input.Ref != "HEAD" && !GitObject(input.Ref) && !GitRef().IsMatch(input.Ref))
            throw new ArgumentException("WORKSPACE_SOURCE_GIT_INPUT_INVALID");
        var grant = RequireGitGrant(actor, input.RepositoryId);
        return await store.BeginGitAsync(workspace, importId, actor, input, grant.CredentialRef, token);
    }

    public async Task<string[]> GitRepositoriesAsync(TerminalActor actor, Guid workspaceId, CancellationToken token)
    {
        _ = await RequireWorkspaceAsync(actor, workspaceId, token);
        return policy.GitRepositories.Where(row => row.Value.Subjects.Contains(actor.Subject, StringComparer.Ordinal))
            .Select(row => row.Key).Order(StringComparer.Ordinal).ToArray();
    }

    internal WorkspaceGitRepositoryGrant RequireGitGrant(TerminalActor actor, string repositoryId) =>
        policy.GitRepositories.TryGetValue(repositoryId, out var grant) && grant.Subjects.Contains(actor.Subject, StringComparer.Ordinal)
            ? grant : throw new UnauthorizedAccessException("WORKSPACE_SOURCE_GIT_REPOSITORY_FORBIDDEN");

    public async Task<WorkspaceSourceView> GetAsync(TerminalActor actor, Guid workspaceId, Guid importId, CancellationToken token)
    {
        _ = await RequireAsync(actor, workspaceId, importId, token);
        return await store.FindAsync(workspaceId, importId, actor.Subject, token)
            ?? throw new KeyNotFoundException("WORKSPACE_SOURCE_NOT_FOUND");
    }

    public async Task<WorkspaceSourceView?> CurrentAsync(TerminalActor actor, Guid workspaceId, CancellationToken token)
    {
        _ = await RequireWorkspaceAsync(actor, workspaceId, token);
        return await store.CurrentAsync(workspaceId, actor.Subject, token);
    }

    public async Task<WorkspaceSourceView?> PublishedAsync(TerminalActor actor, Guid workspaceId, CancellationToken token)
    {
        _ = await RequireWorkspaceAsync(actor, workspaceId, token);
        return await store.PublishedAsync(workspaceId, actor.Subject, token);
    }

    public async Task<WorkspaceSourceHistoryPage> HistoryAsync(TerminalActor actor, Guid workspaceId, Guid? before, int take,
        CancellationToken token)
    {
        _ = await RequireWorkspaceAsync(actor, workspaceId, token);
        if (before == Guid.Empty || take is < 1 or > 50) throw new ArgumentException("WORKSPACE_SOURCE_HISTORY_INVALID");
        return await store.HistoryAsync(workspaceId, actor.Subject, before, take, token);
    }

    public async Task<WorkspaceSourceView> AppendAsync(TerminalActor actor, Guid workspaceId, Guid importId, int offset,
        int length, Stream body, CancellationToken token)
    {
        if ((await GetAsync(actor, workspaceId, importId, token)).Git is not null)
            throw new InvalidOperationException("WORKSPACE_SOURCE_UPLOAD_CLOSED");
        if (offset < 0 || offset % ChunkBytes != 0 || length is <= 0 or > ChunkBytes)
            throw new ArgumentException("WORKSPACE_SOURCE_CHUNK_INVALID");
        if (!await ChunkReaders.WaitAsync(0, token)) throw new InvalidOperationException("WORKSPACE_SOURCE_CHUNK_BUSY");
        try
        {
            return await ReadChunkAsync(actor, workspaceId, importId, offset, length, body, token);
        }
        finally { ChunkReaders.Release(); }
    }

    public async Task<WorkspaceSourceView> ConfirmAsync(TerminalActor actor, Guid workspaceId, Guid importId, WorkspaceSourceConfirmation input, CancellationToken token)
    {
        _ = await RequireAsync(actor, workspaceId, importId, token);
        if (!Digest(input.PlanSha256) || input.ConflictResolution is not (null or CleanOnly or RetainAndReplace))
            throw new ArgumentException("WORKSPACE_SOURCE_PLAN_INVALID");
        return await store.ConfirmAsync(workspaceId, importId, actor, input, token);
    }

    public async Task<WorkspaceSourceView> CancelAsync(TerminalActor actor, Guid workspaceId, Guid importId, CancellationToken token)
    {
        _ = await RequireAsync(actor, workspaceId, importId, token);
        return await store.CancelAsync(workspaceId, importId, actor, token);
    }

    internal async Task<WorkspaceDefinition> RequireAsync(TerminalActor actor, Guid workspaceId, Guid importId, CancellationToken token)
    {
        if (importId == Guid.Empty) throw new ArgumentException("WORKSPACE_SOURCE_SCOPE_INVALID");
        return await RequireWorkspaceAsync(actor, workspaceId, token);
    }

    private async Task<WorkspaceDefinition> RequireWorkspaceAsync(TerminalActor actor, Guid workspaceId, CancellationToken token)
    {
        if (workspaceId == Guid.Empty) throw new ArgumentException("WORKSPACE_SOURCE_SCOPE_INVALID");
        var workspace = await access.RequireAsync(actor, workspaceId, token);
        return workspace.State == WorkspaceStorageAllocation.Allocated ? workspace
            : throw new InvalidOperationException("WORKSPACE_SOURCE_STORAGE_REQUIRED");
    }

    public static bool Digest(string? value) => value is { Length: 64 } && value.All(c => c is >= 'a' and <= 'f' or >= '0' and <= '9');
    public static bool GitObject(string? value) => value is { Length: 40 or 64 } &&
        value.All(c => c is >= 'a' and <= 'f' or >= '0' and <= '9') && value.Any(c => c != '0');

    [GeneratedRegex(@"\A[a-z0-9]+(?:-[a-z0-9]+)*\z", RegexOptions.CultureInvariant)]
    private static partial Regex RepositoryId();
    [GeneratedRegex(@"\Arefs/[A-Za-z0-9_./-]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex GitRef();

    private async Task<WorkspaceSourceView> ReadChunkAsync(TerminalActor actor, Guid workspaceId, Guid importId, int offset,
        int length, Stream body, CancellationToken token)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        lifetime.CancelAfter(TimeSpan.FromSeconds(30));
        var bytes = new byte[length];
        try
        {
            if (await body.ReadAtLeastAsync(bytes, length, false, lifetime.Token) != length ||
                await body.ReadAsync(new byte[1], lifetime.Token) != 0)
                throw new ArgumentException("WORKSPACE_SOURCE_CHUNK_LENGTH");
            _ = await RequireAsync(actor, workspaceId, importId, lifetime.Token);
            return await store.AppendAsync(workspaceId, importId, actor, offset, bytes, lifetime.Token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new InvalidOperationException("WORKSPACE_SOURCE_CHUNK_TIMEOUT"); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}
