using System.Security.Cryptography;
using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Development;

public sealed record WorkspaceSourceInput(string Format, int ArchiveBytes, string ArchiveSha256);
public sealed record WorkspaceSourceConfirmation(string PlanSha256);
public sealed record WorkspaceSourcePlan(string PlanSha256, string ContentSha256, int FileCount, long TotalBytes);
public sealed record WorkspaceSourceView(Guid WorkspaceId, Guid ImportId, string State, WorkspaceSourceInput Archive,
    int ReceivedBytes, WorkspaceSourcePlan? Plan, string? Code, DateTimeOffset ExpiresAtUtc);
public sealed record WorkspaceSourceClaim(WorkspaceSourceView Import, WorkspaceDefinition Workspace, TerminalActor Actor, Guid Lease);
public sealed record WorkspaceSourceObservation(string State, WorkspaceSourcePlan Plan);

public enum WorkspaceSourceFailure { Missing, Rejected, Conflict, Retryable, Integrity, Unsupported }
public sealed class WorkspaceSourceException(WorkspaceSourceFailure failure) : Exception("WORKSPACE_SOURCE_AGENT_" + failure)
{
    public WorkspaceSourceFailure Failure { get; } = failure;
}

public interface IWorkspaceSourceGateway
{
    Task<WorkspaceSourceObservation> StageSourceAsync(WorkspaceSourceClaim claim, IAsyncEnumerable<ReadOnlyMemory<byte>> chunks, CancellationToken token);
    Task<WorkspaceSourceObservation> InspectSourceAsync(WorkspaceSourceClaim claim, CancellationToken token);
    Task<WorkspaceSourceObservation> PublishSourceAsync(WorkspaceSourceClaim claim, WorkspaceSourcePlan plan, CancellationToken token);
    Task<WorkspaceSourceObservation> DiscardSourceAsync(WorkspaceSourceClaim claim, WorkspaceSourcePlan plan, CancellationToken token);
    Task AbortSourceAsync(WorkspaceSourceClaim claim, CancellationToken token);
}

public interface IWorkspaceSourceStore
{
    Task<WorkspaceSourceView> BeginAsync(WorkspaceDefinition workspace, Guid importId, TerminalActor actor, WorkspaceSourceInput input, CancellationToken token);
    Task<WorkspaceSourceView?> FindAsync(Guid workspaceId, Guid importId, string subject, CancellationToken token);
    Task<WorkspaceSourceView> AppendAsync(Guid workspaceId, Guid importId, TerminalActor actor, int offset, byte[] body, CancellationToken token);
    Task<WorkspaceSourceView> ConfirmAsync(Guid workspaceId, Guid importId, TerminalActor actor, string planSha256, CancellationToken token);
    Task<WorkspaceSourceView> CancelAsync(Guid workspaceId, Guid importId, TerminalActor actor, CancellationToken token);
    Task<WorkspaceSourceClaim?> ClaimAsync(CancellationToken token);
    Task<bool> IsCurrentAsync(WorkspaceSourceClaim claim, CancellationToken token);
    IAsyncEnumerable<ReadOnlyMemory<byte>> ReadChunksAsync(WorkspaceSourceClaim claim, CancellationToken token);
    Task AdvanceAsync(WorkspaceSourceClaim claim, string state, WorkspaceSourcePlan? plan, string? code, CancellationToken token);
    Task DeferAsync(WorkspaceSourceClaim claim, string? code, CancellationToken token);
}

public sealed class WorkspaceSources(WorkspaceRuns access, IWorkspaceSourceStore store)
{
    private static readonly SemaphoreSlim ChunkReaders = new(8, 8);
    public const int ChunkBytes = 1024 * 1024;
    public const int ArchiveLimit = 128 * ChunkBytes;
    public const int ReservedBytesLimit = 512 * ChunkBytes;

    public const string Uploading = "Uploading";
    public const string Ready = "Ready";
    public const string Staged = "Staged";
    public const string Publishing = "Publishing";
    public const string Canceling = "Canceling";
    public const string Published = "Published";
    public const string Canceled = "Canceled";
    public const string Failed = "Failed";

    public async Task<WorkspaceSourceView> BeginAsync(TerminalActor actor, Guid workspaceId, Guid importId, WorkspaceSourceInput input,
        CancellationToken token)
    {
        var workspace = await RequireAsync(actor, workspaceId, importId, token);
        if (input.Format is not ("zip" or "tar" or "tar.gz") || input.ArchiveBytes is <= 0 or > ArchiveLimit || !Digest(input.ArchiveSha256))
            throw new ArgumentException("WORKSPACE_SOURCE_ARCHIVE_INVALID");
        return await store.BeginAsync(workspace, importId, actor, input, token);
    }

    public async Task<WorkspaceSourceView> GetAsync(TerminalActor actor, Guid workspaceId, Guid importId, CancellationToken token)
    {
        _ = await RequireAsync(actor, workspaceId, importId, token);
        return await store.FindAsync(workspaceId, importId, actor.Subject, token)
            ?? throw new KeyNotFoundException("WORKSPACE_SOURCE_NOT_FOUND");
    }

    public async Task<WorkspaceSourceView> AppendAsync(TerminalActor actor, Guid workspaceId, Guid importId, int offset,
        int length, Stream body, CancellationToken token)
    {
        _ = await GetAsync(actor, workspaceId, importId, token);
        if (offset < 0 || offset % ChunkBytes != 0 || length is <= 0 or > ChunkBytes)
            throw new ArgumentException("WORKSPACE_SOURCE_CHUNK_INVALID");
        if (!await ChunkReaders.WaitAsync(0, token)) throw new InvalidOperationException("WORKSPACE_SOURCE_CHUNK_BUSY");
        try
        {
            return await ReadChunkAsync(actor, workspaceId, importId, offset, length, body, token);
        }
        finally { ChunkReaders.Release(); }
    }

    public async Task<WorkspaceSourceView> ConfirmAsync(TerminalActor actor, Guid workspaceId, Guid importId, string digest, CancellationToken token)
    {
        _ = await RequireAsync(actor, workspaceId, importId, token);
        if (!Digest(digest)) throw new ArgumentException("WORKSPACE_SOURCE_PLAN_INVALID");
        return await store.ConfirmAsync(workspaceId, importId, actor, digest, token);
    }

    public async Task<WorkspaceSourceView> CancelAsync(TerminalActor actor, Guid workspaceId, Guid importId, CancellationToken token)
    {
        _ = await RequireAsync(actor, workspaceId, importId, token);
        return await store.CancelAsync(workspaceId, importId, actor, token);
    }

    internal async Task<WorkspaceDefinition> RequireAsync(TerminalActor actor, Guid workspaceId, Guid importId, CancellationToken token)
    {
        if (workspaceId == Guid.Empty || importId == Guid.Empty) throw new ArgumentException("WORKSPACE_SOURCE_SCOPE_INVALID");
        var workspace = await access.RequireAsync(actor, workspaceId, token);
        return workspace.State == WorkspaceStorageAllocation.Allocated ? workspace
            : throw new InvalidOperationException("WORKSPACE_SOURCE_STORAGE_REQUIRED");
    }

    internal static bool Digest(string? value) => value is { Length: 64 } && value.All(c => c is >= 'a' and <= 'f' or >= '0' and <= '9');

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
