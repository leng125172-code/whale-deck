using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WhaleDeck.Application.Development;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;
using static WhaleDeck.Application.Development.WorkspaceSources;

namespace WhaleDeck.Infrastructure.Persistence;

public sealed class WorkspaceSourceRepository(PlatformDbContext db, TimeProvider clock, WorkspaceSourceProtector protector) : IWorkspaceSourceStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<WorkspaceSourceView> BeginAsync(WorkspaceDefinition workspace, Guid importId, TerminalActor actor,
        WorkspaceSourceInput input, CancellationToken token) => BeginAsync(workspace, importId, actor, input, null, null, token);

    public Task<WorkspaceSourceView> BeginGitAsync(WorkspaceDefinition workspace, Guid importId, TerminalActor actor,
        WorkspaceGitInput input, string? credentialRef, CancellationToken token) =>
        BeginAsync(workspace, importId, actor, new("tar", 0, ""), input, credentialRef, token);

    private Task<WorkspaceSourceView> BeginAsync(WorkspaceDefinition workspace, Guid importId, TerminalActor actor,
        WorkspaceSourceInput archive, WorkspaceGitInput? git, string? credentialRef, CancellationToken token) => TransactionAsync(async () =>
    {
        // Serialize reservations across workspaces. Other transactions can only
        // free reservations, so their progress cannot race the global limit.
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(1464084307, 1)", token);
        var existing = await db.WorkspaceSourceIntents.SingleOrDefaultAsync(row => row.Id == importId, token);
        if (existing is not null)
        {
            RequireScope(existing, workspace.Id, actor.Subject);
            var matches = git is null
                ? existing.GitRepositoryId is null && View(existing).Archive == archive
                : existing.GitRepositoryId == git.RepositoryId && existing.GitRef == git.Ref && existing.GitCredentialRef == credentialRef &&
                    existing.PreviousImportId == git.PreviousImportId;
            if (!matches) throw new InvalidOperationException("WORKSPACE_SOURCE_IMPORT_CONFLICT");
            return View(existing);
        }
        await WorkspaceMutationFence.RequireActiveAsync(db, workspace.Id, actor.Subject, token, workspace);
        if (await db.WorkspaceSourceIntents.AnyAsync(row => row.WorkspaceId == workspace.Id && row.FinishedAtUtc == null, token))
            throw new InvalidOperationException("WORKSPACE_SOURCE_ALREADY_PENDING");
        if (git?.PreviousImportId is { } previous)
        {
            if (!await PublishedHeads(workspace.Id, actor.Subject).AnyAsync(row => row.Id == previous, token))
                throw new InvalidOperationException("WORKSPACE_SOURCE_WORK_BASE_CONFLICT");
        }
        else if (await db.WorkspaceSourceIntents.AnyAsync(row => row.WorkspaceId == workspace.Id && row.State == Published, token))
            throw new InvalidOperationException("WORKSPACE_SOURCE_WORK_ALREADY_PRESENT");
        var reserved = await db.WorkspaceSourceIntents.Where(row => row.State == Uploading || row.State == Ready || row.State == Acquiring)
            .SumAsync(row => row.State == Acquiring ? (long)ArchiveLimit : row.ArchiveBytes, token);
        if (reserved + (git is null ? archive.ArchiveBytes : ArchiveLimit) > ReservedBytesLimit)
            throw new InvalidOperationException("WORKSPACE_SOURCE_QUEUE_CAPACITY");
        var now = clock.GetUtcNow();
        var expires = now.AddHours(1);
        var row = new WorkspaceSourceIntent
        {
            Id = importId, WorkspaceId = workspace.Id, OwnerSubject = actor.Subject, LoginBinding = actor.LoginBinding,
            WorkspaceJson = JsonSerializer.Serialize(workspace, Json), Format = archive.Format, ArchiveBytes = archive.ArchiveBytes,
            ArchiveSha256 = archive.ArchiveSha256, GitRepositoryId = git?.RepositoryId, GitRef = git?.Ref, GitCredentialRef = credentialRef,
            PreviousImportId = git?.PreviousImportId,
            State = git is null ? Uploading : Acquiring, AvailableAtUtc = git is null ? expires : now, ExpiresAtUtc = expires
        };
        db.WorkspaceSourceIntents.Add(row);
        Audit(row, actor.TraceId, git is null ? "begin" : git.PreviousImportId is null ? "git-begin" : "git-update-begin");
        return View(row);
    }, token);

    public async Task<WorkspaceSourceView?> FindAsync(Guid workspaceId, Guid importId, string subject, CancellationToken token)
    {
        var row = await db.WorkspaceSourceIntents.AsNoTracking().SingleOrDefaultAsync(row =>
            row.Id == importId && row.WorkspaceId == workspaceId && row.OwnerSubject == subject, token);
        return row is null ? null : View(row);
    }

    public async Task<WorkspaceSourceView?> CurrentAsync(Guid workspaceId, string subject, CancellationToken token)
    {
        var row = await db.WorkspaceSourceIntents.AsNoTracking()
            .Where(item => item.WorkspaceId == workspaceId && item.OwnerSubject == subject)
            .OrderBy(item => item.FinishedAtUtc != null).ThenByDescending(item => item.FinishedAtUtc).ThenBy(item => item.Id)
            .FirstOrDefaultAsync(token);
        return row is null ? null : View(row);
    }

    public async Task<WorkspaceSourceView?> PublishedAsync(Guid workspaceId, string subject, CancellationToken token)
    {
        var row = await PublishedHeads(workspaceId, subject).SingleOrDefaultAsync(token);
        return row is null ? null : View(row);
    }

    public async Task RecordPreviousDownloadAsync(Guid workspaceId, Guid importId, TerminalActor actor, CancellationToken token)
    {
        var row = await db.WorkspaceSourceIntents.AsNoTracking().SingleOrDefaultAsync(item =>
            item.Id == importId && item.WorkspaceId == workspaceId && item.OwnerSubject == actor.Subject &&
            item.State == Published && item.PreviousImportId != null, token)
            ?? throw new KeyNotFoundException("WORKSPACE_SOURCE_PREVIOUS_NOT_FOUND");
        // Records authorization to export, not delivery to the browser.
        Audit(row, actor.TraceId, "download-previous-authorized");
        await db.SaveChangesAsync(token);
    }

    private IQueryable<WorkspaceSourceIntent> PublishedHeads(Guid workspaceId, string subject) =>
        db.WorkspaceSourceIntents.AsNoTracking().Where(row => row.WorkspaceId == workspaceId && row.OwnerSubject == subject &&
            row.State == Published && !db.WorkspaceSourceIntents.Any(successor => successor.PreviousImportId == row.Id &&
                successor.WorkspaceId == workspaceId && successor.OwnerSubject == subject && successor.State == Published));

    public async Task<WorkspaceSourceHistoryPage> HistoryAsync(Guid workspaceId, string subject, Guid? before, int take, CancellationToken token)
    {
        var terminal = db.WorkspaceSourceIntents.AsNoTracking().Where(row =>
            row.WorkspaceId == workspaceId && row.OwnerSubject == subject && row.FinishedAtUtc != null);
        if (before is { } id)
        {
            var cursor = await terminal.Where(row => row.Id == id).Select(row => row.FinishedAtUtc).SingleOrDefaultAsync(token)
                ?? throw new KeyNotFoundException("WORKSPACE_SOURCE_HISTORY_CURSOR_NOT_FOUND");
            terminal = terminal.Where(row => row.FinishedAtUtc < cursor || row.FinishedAtUtc == cursor && row.Id.CompareTo(id) < 0);
        }
        var rows = await terminal.OrderByDescending(row => row.FinishedAtUtc).ThenByDescending(row => row.Id).Take(take + 1).ToArrayAsync(token);
        return new(rows.Take(take).Select(View).ToArray(), rows.Length > take ? rows[take - 1].Id : null);
    }

    public Task<WorkspaceSourceView> AppendAsync(Guid workspaceId, Guid importId, TerminalActor actor, int offset,
        byte[] body, CancellationToken token) => TransactionAsync(async () =>
    {
        await WorkspaceMutationFence.RequireActiveAsync(db, workspaceId, actor.Subject, token);
        var row = await LoadAsync(importId, token);
        RequireScope(row, workspaceId, actor.Subject);
        RequireSession(row, actor);
        if (row.GitRepositoryId is not null || row.State is not (Uploading or Ready) || clock.GetUtcNow() >= row.ExpiresAtUtc)
            throw new InvalidOperationException("WORKSPACE_SOURCE_UPLOAD_CLOSED");
        if (await AppendLockedAsync(row, offset, body, token) && row.ReceivedBytes == row.ArchiveBytes)
        {
            row.State = Ready;
            row.AvailableAtUtc = clock.GetUtcNow();
            Audit(row, actor.TraceId, "uploaded");
        }
        return View(row);
    }, token);

    private async Task<bool> AppendLockedAsync(WorkspaceSourceIntent row, int offset, byte[] body, CancellationToken token)
    {
        if (offset < 0 || offset % ChunkBytes != 0 || offset >= row.ArchiveBytes ||
            body.Length != Math.Min(ChunkBytes, row.ArchiveBytes - offset))
            throw new ArgumentException("WORKSPACE_SOURCE_CHUNK_INVALID");
        if (offset < row.ReceivedBytes)
        {
            var prior = await db.WorkspaceSourceChunks.AsNoTracking().SingleAsync(chunk => chunk.ImportId == row.Id && chunk.Offset == offset, token);
            var original = protector.Open(row.Id, offset, prior.SealedContent);
            try
            {
                if (!original.AsSpan().SequenceEqual(body)) throw new InvalidOperationException("WORKSPACE_SOURCE_CHUNK_CHANGED");
            }
            finally { CryptographicOperations.ZeroMemory(original); }
            return false;
        }
        if (offset != row.ReceivedBytes) throw new InvalidOperationException("WORKSPACE_SOURCE_CHUNK_OUT_OF_ORDER");
        db.WorkspaceSourceChunks.Add(new() { ImportId = row.Id, Offset = offset, SealedContent = protector.Seal(row.Id, offset, body) });
        row.ReceivedBytes += body.Length;
        return true;
    }

    public Task BindGitAsync(WorkspaceSourceClaim claim, WorkspaceGitProof proof, CancellationToken token) =>
        TransactionAsync(async () =>
        {
            var row = await LoadGitLeaseAsync(claim, token);
            if (!GitObject(proof.ResolvedObject) || !GitObject(proof.Commit) || proof.ResolvedObject.Length != proof.Commit.Length ||
                proof.Archive is not { Format: "tar", ArchiveBytes: > 0 and <= ArchiveLimit } || !Digest(proof.Archive.ArchiveSha256) ||
                proof.FileCount is < 1 or >= 10000 || proof.TotalBytes < 0 || proof.TotalBytes > proof.Archive.ArchiveBytes ||
                (GitObject(row.GitRef) && row.GitRef != proof.ResolvedObject))
                throw new WorkspaceSourceException(WorkspaceSourceFailure.Integrity);
            if (GitProof(row) is { } pinned)
            {
                if (pinned != proof) throw new WorkspaceSourceException(WorkspaceSourceFailure.Conflict);
                return true;
            }
            row.ArchiveBytes = proof.Archive.ArchiveBytes;
            row.ArchiveSha256 = proof.Archive.ArchiveSha256;
            row.GitResolvedObject = proof.ResolvedObject;
            row.GitCommit = proof.Commit;
            row.GitFileCount = proof.FileCount;
            row.GitTotalBytes = proof.TotalBytes;
            Audit(row, claim.Actor.TraceId, "git-pin");
            return true;
        }, token);

    public Task AppendGitAsync(WorkspaceSourceClaim claim, int offset, byte[] body, CancellationToken token) =>
        TransactionAsync(async () =>
        {
            var row = await LoadGitLeaseAsync(claim, token);
            try { return await AppendLockedAsync(row, offset, body, token); }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or CryptographicException)
            { throw new WorkspaceSourceException(WorkspaceSourceFailure.Integrity); }
        }, token);

    public Task CompleteGitAsync(WorkspaceSourceClaim claim, CancellationToken token) =>
        TransactionAsync(async () =>
        {
            var row = await LoadGitLeaseAsync(claim, token);
            if (row.ArchiveBytes == 0 || row.ReceivedBytes != row.ArchiveBytes)
                throw new WorkspaceSourceException(WorkspaceSourceFailure.Integrity);
            row.State = Ready;
            row.AvailableAtUtc = clock.GetUtcNow();
            row.Lease = null;
            row.LeaseUntilUtc = null;
            row.Code = null;
            Audit(row, claim.Actor.TraceId, "git-acquired");
            return true;
        }, token);

    private async Task<WorkspaceSourceIntent> LoadGitLeaseAsync(WorkspaceSourceClaim claim, CancellationToken token)
    {
        var row = await LoadAsync(claim.Import.ImportId, token);
        var now = clock.GetUtcNow();
        if (row.State != Acquiring || row.GitRepositoryId is null || row.Lease != claim.Lease ||
            row.LeaseUntilUtc <= now || row.ExpiresAtUtc <= now)
            throw new WorkspaceSourceException(WorkspaceSourceFailure.Retryable);
        return row;
    }

    public Task<WorkspaceSourceView> ConfirmAsync(Guid workspaceId, Guid importId, TerminalActor actor, WorkspaceSourceConfirmation input,
        CancellationToken token) => TransactionAsync(async () =>
    {
        await WorkspaceMutationFence.RequireActiveAsync(db, workspaceId, actor.Subject, token);
        var row = await LoadAsync(importId, token);
        RequireScope(row, workspaceId, actor.Subject);
        RequireSession(row, actor);
        if (input.PlanSha256 != row.PlanSha256) throw new InvalidOperationException("WORKSPACE_SOURCE_PLAN_CONFLICT");
        var choice = input.ConflictResolution;
        if (row.PreviousImportId is null ? choice is not null : choice is not (CleanOnly or RetainAndReplace))
            throw new ArgumentException("WORKSPACE_SOURCE_UPDATE_CHOICE_INVALID");
        if (row.State is Publishing or Published)
        {
            if (choice != row.ConflictResolution) throw new InvalidOperationException("WORKSPACE_SOURCE_UPDATE_CHOICE_CONFLICT");
            return View(row);
        }
        if (row.State != Staged || clock.GetUtcNow() >= row.ExpiresAtUtc)
            throw new InvalidOperationException("WORKSPACE_SOURCE_PLAN_UNAVAILABLE");
        if (choice == CleanOnly && ReadUpdate(row) is { Dirty: true })
            throw new InvalidOperationException("WORKSPACE_SOURCE_WORK_DIRTY");
        row.ConflictResolution = choice;
        row.State = Publishing;
        row.AvailableAtUtc = clock.GetUtcNow();
        Audit(row, actor.TraceId, "confirm");
        return View(row);
    }, token);

    public Task<WorkspaceSourceView> CancelAsync(Guid workspaceId, Guid importId, TerminalActor actor, CancellationToken token) =>
        TransactionAsync(async () =>
        {
            var row = await LoadAsync(importId, token);
            RequireScope(row, workspaceId, actor.Subject);
            if (row.State is Publishing or Published) throw new InvalidOperationException("WORKSPACE_SOURCE_PUBLICATION_ADMITTED");
            if (row.FinishedAtUtc is not null || row.State == Canceling) return View(row);
            await RequestCancelAsync(row, actor.TraceId, "WORKSPACE_SOURCE_CANCELED", token);
            return View(row);
        }, token);

    public Task<WorkspaceSourceClaim?> ClaimAsync(CancellationToken token) => TransactionAsync<WorkspaceSourceClaim?>(async () =>
    {
        var now = clock.GetUtcNow();
        var candidates = await db.WorkspaceSourceIntents.FromSqlInterpolated($"""
            SELECT * FROM development_workspace_source_imports
             WHERE finished_at_utc IS NULL AND available_at_utc <= {now}
               AND (lease_until_utc IS NULL OR lease_until_utc <= {now})
             ORDER BY available_at_utc, id LIMIT 1 FOR UPDATE SKIP LOCKED
            """).ToListAsync(token);
        if (candidates.Count == 0) return null;
        var row = candidates[0];
        if (row.State is Uploading or Acquiring or Ready or Staged && row.ExpiresAtUtc <= now)
        {
            await RequestCancelAsync(row, row.Id.ToString("N"), "WORKSPACE_SOURCE_EXPIRED", token);
            if (row.FinishedAtUtc is not null) return null;
        }
        if (row.State is not (Acquiring or Ready or Publishing or Canceling))
            throw new InvalidDataException("WORKSPACE_SOURCE_STATE_INVALID");
        var workspace = JsonSerializer.Deserialize<WorkspaceDefinition>(row.WorkspaceJson, Json);
        if (workspace is null || workspace.Id != row.WorkspaceId || workspace.OwnerSubject != row.OwnerSubject ||
            workspace.State != WorkspaceStorageAllocation.Allocated)
            throw new InvalidDataException("WORKSPACE_SOURCE_SCOPE_INVALID");
        var lease = Guid.NewGuid();
        row.Lease = lease;
        row.LeaseUntilUtc = now.AddSeconds(row.PreviousImportId is not null && row.State == Publishing ? 170 : 140);
        var baseDigest = row.PreviousImportId is { } previous
            ? await db.WorkspaceSourceIntents.AsNoTracking().Where(candidate => candidate.Id == previous &&
                candidate.WorkspaceId == row.WorkspaceId && candidate.OwnerSubject == row.OwnerSubject && candidate.State == Published)
                .Select(candidate => candidate.ContentSha256).SingleOrDefaultAsync(token)
            : null;
        if (row.PreviousImportId is not null && !Digest(baseDigest)) throw new InvalidDataException("WORKSPACE_SOURCE_WORK_BASE_CONFLICT");
        return new(View(row), workspace, new(row.OwnerSubject, null, [], row.LoginBinding, row.Id.ToString("N"), null),
            lease, row.GitCredentialRef, baseDigest);
    }, token);

    public Task<bool> IsCurrentAsync(WorkspaceSourceClaim claim, CancellationToken token)
    {
        var now = clock.GetUtcNow();
        return db.WorkspaceSourceIntents.AsNoTracking().AnyAsync(row => row.Id == claim.Import.ImportId && row.Lease == claim.Lease &&
            row.LeaseUntilUtc > now && row.State == claim.Import.State && (row.State != Acquiring || row.ExpiresAtUtc > now), token);
    }

    public async IAsyncEnumerable<ReadOnlyMemory<byte>> ReadChunksAsync(WorkspaceSourceClaim claim,
        [EnumeratorCancellation] CancellationToken token)
    {
        for (var offset = 0; offset < claim.Import.Archive.ArchiveBytes; offset += ChunkBytes)
        {
            var encrypted = await db.WorkspaceSourceChunks.AsNoTracking()
                .Where(row => row.ImportId == claim.Import.ImportId && row.Offset == offset)
                .Select(row => row.SealedContent).SingleOrDefaultAsync(token);
            if (encrypted is null || encrypted.Length != WorkspaceSourceProtector.Overhead +
                Math.Min(ChunkBytes, claim.Import.Archive.ArchiveBytes - offset))
                throw new WorkspaceSourceException(WorkspaceSourceFailure.Integrity);
            byte[] data;
            try { data = protector.Open(claim.Import.ImportId, offset, encrypted); }
            catch (CryptographicException) { throw new WorkspaceSourceException(WorkspaceSourceFailure.Integrity); }
            try { yield return data; }
            finally { CryptographicOperations.ZeroMemory(data); }
        }
    }

    public Task AdvanceAsync(WorkspaceSourceClaim claim, string state, WorkspaceSourcePlan? plan, string? code, CancellationToken token) =>
        TransactionAsync(async () =>
        {
            var row = await LoadAsync(claim.Import.ImportId, token);
            if (!Release(row, claim)) return false;
            if (row.State != claim.Import.State) return false;
            if (state == Canceling)
            {
                await RequestCancelAsync(row, claim.Actor.TraceId, code!, token);
                return true;
            }
            row.State = state;
            row.Code = code;
            if (plan is not null)
            {
                row.PlanSha256 = plan.PlanSha256;
                row.ContentSha256 = plan.ContentSha256;
                row.FileCount = plan.FileCount;
                row.TotalBytes = plan.TotalBytes;
                row.UpdateJson = plan.Update is null ? null : JsonSerializer.Serialize(plan.Update, Json);
            }
            if (state == Staged) row.AvailableAtUtc = row.ExpiresAtUtc;
            else row.FinishedAtUtc = clock.GetUtcNow();
            await DeleteChunksAsync(row.Id, token);
            Audit(row, claim.Actor.TraceId, "reconcile");
            return true;
        }, token);

    public Task DeferAsync(WorkspaceSourceClaim claim, string? code, CancellationToken token) =>
        TransactionAsync(async () =>
        {
            var row = await LoadAsync(claim.Import.ImportId, token);
            if (!Release(row, claim) || row.State != claim.Import.State) return false;
            if (row.State != Canceling) row.Code = code;
            row.AvailableAtUtc = clock.GetUtcNow().AddSeconds(5);
            return true;
        }, token);

    private async Task RequestCancelAsync(WorkspaceSourceIntent row, string trace, string code, CancellationToken token)
    {
        var now = clock.GetUtcNow();
        if (row.State == Acquiring)
        {
            row.State = Canceled;
            row.FinishedAtUtc = now;
            row.Lease = null;
            row.LeaseUntilUtc = null;
        }
        else row.State = Canceling;
        row.AvailableAtUtc = now;
        row.Code = code;
        await DeleteChunksAsync(row.Id, token);
        Audit(row, trace, "cancel");
    }

    private Task<int> DeleteChunksAsync(Guid id, CancellationToken token) =>
        db.WorkspaceSourceChunks.Where(row => row.ImportId == id).ExecuteDeleteAsync(token);

    private async Task<WorkspaceSourceIntent> LoadAsync(Guid id, CancellationToken token) =>
        (await db.WorkspaceSourceIntents.FromSqlInterpolated($"""
            SELECT * FROM development_workspace_source_imports WHERE id = {id} FOR UPDATE
            """).ToListAsync(token)).SingleOrDefault() ?? throw new KeyNotFoundException("WORKSPACE_SOURCE_NOT_FOUND");

    private bool Release(WorkspaceSourceIntent row, WorkspaceSourceClaim claim)
    {
        if (row.Lease != claim.Lease || row.LeaseUntilUtc <= clock.GetUtcNow()) return false;
        row.Lease = null;
        row.LeaseUntilUtc = null;
        return true;
    }

    private Task<T> TransactionAsync<T>(Func<Task<T>> change, CancellationToken token) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync<T>(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(token);
            var result = await change();
            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
            return result;
        });

    private static void RequireScope(WorkspaceSourceIntent row, Guid workspaceId, string subject)
    {
        if (row.WorkspaceId != workspaceId || row.OwnerSubject != subject)
            throw new KeyNotFoundException("WORKSPACE_SOURCE_NOT_FOUND");
    }

    private static void RequireSession(WorkspaceSourceIntent row, TerminalActor actor)
    {
        if (row.LoginBinding != actor.LoginBinding) throw new UnauthorizedAccessException("WORKSPACE_SOURCE_LOGIN_CHANGED");
    }

    private static WorkspaceSourceView View(WorkspaceSourceIntent row) => new(row.WorkspaceId, row.Id, row.State,
        new(row.Format, row.ArchiveBytes, row.ArchiveSha256), row.ReceivedBytes,
        row.PlanSha256 is null ? null : new(row.PlanSha256, row.ContentSha256!, row.FileCount!.Value, row.TotalBytes!.Value, ReadUpdate(row)),
        row.Code, row.ExpiresAtUtc,
        row.GitRepositoryId is null ? null : new(row.GitRepositoryId, row.GitRef!, GitProof(row)), row.FinishedAtUtc,
        row.PreviousImportId, row.ConflictResolution);

    private static WorkspaceSourceUpdatePlan? ReadUpdate(WorkspaceSourceIntent row) => row.UpdateJson is null ? null :
        JsonSerializer.Deserialize<WorkspaceSourceUpdatePlan>(row.UpdateJson, Json) ?? throw new InvalidDataException("WORKSPACE_SOURCE_PLAN_INVALID");

    private static WorkspaceGitProof? GitProof(WorkspaceSourceIntent row) =>
        row.GitResolvedObject is null ? null : new(row.GitResolvedObject, row.GitCommit!,
            new(row.Format, row.ArchiveBytes, row.ArchiveSha256), row.GitFileCount!.Value, row.GitTotalBytes!.Value);

    private void Audit(WorkspaceSourceIntent row, string trace, string action) => db.AuditEvents.Add(new()
    {
        ActorSubject = row.OwnerSubject, Action = "development.workspace.source." + action, TargetType = "workspace-source",
        TargetId = row.Id.ToString("D"), Result = row.State, TraceId = trace, OccurredAtUtc = clock.GetUtcNow()
    });
}
