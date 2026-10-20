using Microsoft.EntityFrameworkCore;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Infrastructure.Persistence;

/// <summary>All callers hold advisory transaction 41307341. Lease expiry never releases a path.</summary>
internal static class FilePathLocks
{
    internal static async Task Acquire(PlatformDbContext db, Guid id, string actor, IEnumerable<FileLocationDto> locations,
        IEnumerable<string> tokenHashes, CancellationToken cancellationToken, Guid? existingTreeId = null)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("FILES_LOCK_TRANSACTION_REQUIRED");
        // Re-entrant for current repositories; also fences a future caller that
        // opened a transaction but forgot the shared gate before checking.
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(41307341);", cancellationToken);
        await EnsureBudget(db, actor, existingTreeId, cancellationToken);
        var paths = locations.Select(item => new FilePathScope(item)).Distinct().ToArray();
        var tokens = tokenHashes.Distinct(StringComparer.Ordinal).ToArray();
        if (paths.Length is < 1 or > 2 || tokens.Length > 1 || tokens.Any(item => !FileManagementService.Hex(item)))
            throw new ArgumentException("FILES_LOCK_SCOPE_INVALID");
        var targets = paths.Select(item => item.Hash).Concat(tokens).Distinct(StringComparer.Ordinal).ToArray();
        var held = await db.FilePathLocks.OrderBy(item => item.PathHash).Take(257).ToArrayAsync(cancellationToken);
        if (held.Length > 256) throw new InvalidOperationException("FILES_LOCK_SCOPE_UNVERIFIABLE");
        // Resolve every held row from its owning intent, including migrated rows.
        // A missing/malformed intent is NOT permission to drop the old lock.
        var resolved = await Resolve(db, held, cancellationToken);
        foreach (var current in held)
        {
            if (targets.Contains(current.PathHash, StringComparer.Ordinal) || current.ScopeKind == "Path" &&
                paths.Any(item => item.Conflicts(resolved[current.PathHash]!))) throw new InvalidOperationException("FILES_TARGET_BUSY");
        }
        foreach (var path in paths) db.FilePathLocks.Add(new()
            { PathHash = path.Hash, OperationId = id, ScopeKind = "Path", RootId = path.RootId, RelativePath = path.Path });
        foreach (var token in tokens) db.FilePathLocks.Add(new() { PathHash = token, OperationId = id, ScopeKind = "Token" });
    }

    private static async Task<Dictionary<string, FilePathScope?>> Resolve(PlatformDbContext db, FilePathLock[] held, CancellationToken cancellationToken)
    {
        var ids = held.Select(item => item.OperationId).Distinct().ToArray();
        var uploads = await db.FileUploadOperations.AsNoTracking().Where(item => ids.Contains(item.Id)).ToDictionaryAsync(item => item.Id, cancellationToken);
        var mutations = await db.FileMutationOperations.AsNoTracking().Where(item => ids.Contains(item.Id)).ToDictionaryAsync(item => item.Id, cancellationToken);
        var archives = await db.FileArchiveOperations.AsNoTracking().Where(item => ids.Contains(item.Id)).ToDictionaryAsync(item => item.Id, cancellationToken);
        var trees = await db.FileTreeCopyOperations.AsNoTracking().Where(item => item.ExecutionJobId != null && ids.Contains(item.ExecutionJobId.Value)).ToDictionaryAsync(item => item.ExecutionJobId!.Value, cancellationToken);
        var moves = await db.FileTreeMoveOperations.AsNoTracking().Where(item => item.ExecutionJobId != null && ids.Contains(item.ExecutionJobId.Value)).ToDictionaryAsync(item => item.ExecutionJobId!.Value, cancellationToken);
        var packings = await db.FileTreePackingOperations.AsNoTracking().Where(item => item.ExecutionJobId != null && ids.Contains(item.ExecutionJobId.Value)).ToDictionaryAsync(item => item.ExecutionJobId!.Value, cancellationToken);
        var unpackings = await db.FileTreeUnpackingOperations.AsNoTracking().Where(item => item.ExecutionJobId != null && ids.Contains(item.ExecutionJobId.Value)).ToDictionaryAsync(item => item.ExecutionJobId!.Value, cancellationToken);
        var recyclings = await db.FileTreeRecyclingOperations.AsNoTracking().Where(item => item.ExecutionJobId != null && ids.Contains(item.ExecutionJobId.Value)).ToDictionaryAsync(item => item.ExecutionJobId!.Value, cancellationToken);
        var restorings = await db.FileTreeRestoringOperations.AsNoTracking().Where(item => item.ExecutionJobId != null && ids.Contains(item.ExecutionJobId.Value)).ToDictionaryAsync(item => item.ExecutionJobId!.Value, cancellationToken);
        var overwrites = await db.FileTreeOverwriteRestoringOperations.AsNoTracking().Where(item => item.ExecutionJobId != null && ids.Contains(item.ExecutionJobId.Value)).ToDictionaryAsync(item => item.ExecutionJobId!.Value, cancellationToken);
        var result = new Dictionary<string, FilePathScope?>(StringComparer.Ordinal);
        foreach (var row in held)
        {
            try
            {
                FilePathScope? scope = null; var token = false;
                var owners = (uploads.ContainsKey(row.OperationId) ? 1 : 0) + (mutations.ContainsKey(row.OperationId) ? 1 : 0) + (archives.ContainsKey(row.OperationId) ? 1 : 0) + (trees.ContainsKey(row.OperationId) ? 1 : 0) + (moves.ContainsKey(row.OperationId) ? 1 : 0);
                owners += packings.ContainsKey(row.OperationId) ? 1 : 0;
                owners += unpackings.ContainsKey(row.OperationId) ? 1 : 0;
                owners += recyclings.ContainsKey(row.OperationId) ? 1 : 0;
                owners += restorings.ContainsKey(row.OperationId) ? 1 : 0;
                owners += overwrites.ContainsKey(row.OperationId) ? 1 : 0;
                if (owners != 1) throw new ArgumentException("FILES_LOCK_SCOPE_INVALID");
                if (uploads.TryGetValue(row.OperationId, out var upload))
                {
                    scope = new(new(upload.RootId, upload.RelativePath));
                    if (upload.TargetHash != scope.Hash || scope.Hash != row.PathHash) throw new ArgumentException("FILES_LOCK_SCOPE_INVALID");
                }
                else if (mutations.TryGetValue(row.OperationId, out var mutation))
                {
                    var plan = FileMutationService.ReadPlan(mutation); FileMutationService.Validate(plan);
                    scope = new[] { plan.Command.Source, plan.Command.Target }.OfType<FileLocationDto>().Select(item => new FilePathScope(item))
                        .SingleOrDefault(item => item.Hash == row.PathHash);
                    if (scope is null) throw new ArgumentException("FILES_LOCK_SCOPE_INVALID");
                }
                else if (archives.TryGetValue(row.OperationId, out var archive))
                {
                    var location = new FilePathScope(new(archive.RootId, archive.RelativePath));
                    if (location.Hash != archive.TargetHash) throw new ArgumentException("FILES_LOCK_SCOPE_INVALID");
                    if (row.PathHash == location.Hash) scope = location;
                    else if (archive.ArchiveVersionId is { } version && row.PathHash == FileManagementService.Hash("file-version-v1:" + version.ToString("N"))) token = true;
                    else throw new ArgumentException("FILES_LOCK_SCOPE_INVALID");
                }
                else if (trees.TryGetValue(row.OperationId, out var tree))
                {
                    var command = FileTreeCopyValidation.ReadCommand(tree); FileTreeCopyValidation.Command(command);
                    var plan = FileTreeCopyValidation.ReadPlan(tree); FileTreeCopyValidation.Plan(plan);
                    var source = new FilePathScope(command.Source); var target = new FilePathScope(command.Target);
                    if (plan.Command != command || tree.SourceHash != source.Hash || tree.TargetHash != target.Hash || tree.ExecutionJobId != FileTreeCopyValidation.ExecutionId(tree.Id))
                        throw new ArgumentException("FILES_LOCK_SCOPE_INVALID");
                    scope = new[] { source, target }.SingleOrDefault(item => item.Hash == row.PathHash);
                    if (scope is null) throw new ArgumentException("FILES_LOCK_SCOPE_INVALID");
                }
                else if (moves.TryGetValue(row.OperationId, out var move))
                {
                    var command = FileTreeMoveValidation.ReadCommand(move); FileTreeMoveValidation.Command(command);
                    var plan = FileTreeMoveValidation.ReadPlan(move); FileTreeMoveValidation.Plan(plan);
                    var source = new FilePathScope(command.Source); var target = new FilePathScope(command.Target);
                    if (plan.Command != command || move.SourceHash != source.Hash || move.TargetHash != target.Hash || move.ExecutionJobId != FileTreeMoveValidation.ExecutionId(move.Id))
                        throw new ArgumentException("FILES_LOCK_SCOPE_INVALID");
                    scope = new[] { source, target }.SingleOrDefault(item => item.Hash == row.PathHash);
                    if (scope is null) throw new ArgumentException("FILES_LOCK_SCOPE_INVALID");
                }
                else if (packings.TryGetValue(row.OperationId, out var packing))
                {
                    var command = FileTreePackingValidation.ReadCommand(packing); FileTreePackingValidation.Command(command);
                    var plan = FileTreePackingValidation.ReadPlan(packing); FileTreePackingValidation.Plan(plan);
                    var source = new FilePathScope(command.Source); var target = new FilePathScope(command.Target);
                    if (plan.Command != command || packing.SourceHash != source.Hash || packing.TargetHash != target.Hash || packing.ExecutionJobId != FileTreePackingValidation.ExecutionId(packing.Id))
                        throw new ArgumentException("FILES_LOCK_SCOPE_INVALID");
                    scope = new[] { source, target }.SingleOrDefault(item => item.Hash == row.PathHash);
                    if (scope is null) throw new ArgumentException("FILES_LOCK_SCOPE_INVALID");
                }
                else if (unpackings.TryGetValue(row.OperationId, out var unpacking))
                {
                    var command = FileTreeUnpackingValidation.ReadCommand(unpacking); FileTreeUnpackingValidation.Command(command);
                    var plan = FileTreeUnpackingValidation.ReadPlan(unpacking); FileTreeUnpackingValidation.Plan(plan);
                    var source = new FilePathScope(command.Source); var target = new FilePathScope(command.Target);
                    if (plan.Command != command || unpacking.SourceHash != source.Hash || unpacking.TargetHash != target.Hash || unpacking.ExecutionJobId != FileTreeUnpackingValidation.ExecutionId(unpacking.Id))
                        throw new ArgumentException("FILES_LOCK_SCOPE_INVALID");
                    scope = new[] { source, target }.SingleOrDefault(item => item.Hash == row.PathHash);
                    if (scope is null) throw new ArgumentException("FILES_LOCK_SCOPE_INVALID");
                }
                else if (recyclings.TryGetValue(row.OperationId, out var recycling))
                {
                    var command = FileTreeRecyclingValidation.ReadCommand(recycling); FileTreeRecyclingValidation.Command(command);
                    var plan = FileTreeRecyclingValidation.ReadPlan(recycling); FileTreeRecyclingValidation.Plan(plan);
                    scope = new(command.Source);
                    if (plan.Command != command || recycling.SourceHash != scope.Hash || row.PathHash != scope.Hash || recycling.ExecutionJobId != FileTreeRecyclingValidation.ExecutionId(recycling.Id))
                        throw new ArgumentException("FILES_LOCK_SCOPE_INVALID");
                }
                else if (restorings.TryGetValue(row.OperationId, out var restoring))
                {
                    var command = FileTreeRestoringValidation.ReadCommand(restoring); FileTreeRestoringValidation.Command(command);
                    var plan = FileTreeRestoringValidation.ReadPlan(restoring); FileTreeRestoringValidation.Plan(plan);
                    var source = new FilePathScope(command.Source); var target = new FilePathScope(command.Target);
                    if (plan.Command != command || restoring.CaptureId != command.CaptureId || restoring.SourceHash != source.Hash || restoring.TargetHash != target.Hash ||
                        restoring.ExecutionJobId != FileTreeRestoringValidation.ExecutionId(restoring.Id)) throw new ArgumentException("FILES_LOCK_SCOPE_INVALID");
                    if (row.PathHash == FileTreeRestoringValidation.CaptureToken(command.CaptureId)) token = true;
                    else
                    {
                        scope = new[] { source, target }.Distinct().SingleOrDefault(item => item.Hash == row.PathHash);
                        if (scope is null) throw new ArgumentException("FILES_LOCK_SCOPE_INVALID");
                    }
                }
                else if (overwrites.TryGetValue(row.OperationId, out var overwrite))
                {
                    var command = FileTreeOverwriteRestoringValidation.ReadCommand(overwrite); FileTreeOverwriteRestoringValidation.Command(command);
                    var plan = FileTreeOverwriteRestoringValidation.ReadPlan(overwrite); FileTreeOverwriteRestoringValidation.Plan(plan);
                    var source = new FilePathScope(command.Source); var target = new FilePathScope(command.Target);
                    if (plan.Command != command || overwrite.CaptureId != command.CaptureId || overwrite.SourceHash != source.Hash || overwrite.TargetHash != target.Hash ||
                        overwrite.ExecutionJobId != FileTreeOverwriteRestoringValidation.ExecutionId(overwrite.Id)) throw new ArgumentException("FILES_LOCK_SCOPE_INVALID");
                    if (row.PathHash == FileTreeOverwriteRestoringValidation.CaptureToken(command.CaptureId)) token = true;
                    else
                    {
                        scope = new[] { source, target }.Distinct().SingleOrDefault(item => item.Hash == row.PathHash);
                        if (scope is null) throw new ArgumentException("FILES_LOCK_SCOPE_INVALID");
                    }
                }
                var kind = token ? "Token" : "Path";
                if (row.ScopeKind != "Legacy" && (row.ScopeKind != kind || row.RootId != scope?.RootId || row.RelativePath != scope?.Path)) throw new ArgumentException("FILES_LOCK_SCOPE_INVALID");
                if (row.ScopeKind == "Legacy") { row.ScopeKind = kind; row.RootId = scope?.RootId; row.RelativePath = scope?.Path; }
                result.Add(row.PathHash, scope);
            }
            catch (Exception error) when (error is ArgumentException or System.Text.Json.JsonException or InvalidOperationException)
            { throw new InvalidOperationException("FILES_LOCK_SCOPE_UNVERIFIABLE"); }
        }
        return result;
    }

    internal static Task<int> Release(PlatformDbContext db, Guid id, CancellationToken cancellationToken) =>
        db.FilePathLocks.Where(item => item.OperationId == id).ExecuteDeleteAsync(cancellationToken);

    internal static async Task EnsureBudget(PlatformDbContext db, string actor, Guid? existingTreeId, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("FILES_LOCK_TRANSACTION_REQUIRED");
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(41307341);", cancellationToken);
        var trees = db.FileTreeCopyOperations.Where(item => item.CompletedAtUtc == null && (!existingTreeId.HasValue || item.Id != existingTreeId.Value));
        var moves = db.FileTreeMoveOperations.Where(item => item.CompletedAtUtc == null && (!existingTreeId.HasValue || item.Id != existingTreeId.Value));
        var packings = db.FileTreePackingOperations.Where(item => item.CompletedAtUtc == null && (!existingTreeId.HasValue || item.Id != existingTreeId.Value));
        var unpackings = db.FileTreeUnpackingOperations.Where(item => item.CompletedAtUtc == null && (!existingTreeId.HasValue || item.Id != existingTreeId.Value));
        var recyclings = db.FileTreeRecyclingOperations.Where(item => item.CompletedAtUtc == null && (!existingTreeId.HasValue || item.Id != existingTreeId.Value));
        var restorings = db.FileTreeRestoringOperations.Where(item => item.CompletedAtUtc == null && (!existingTreeId.HasValue || item.Id != existingTreeId.Value));
        var overwrites = db.FileTreeOverwriteRestoringOperations.Where(item => item.CompletedAtUtc == null && (!existingTreeId.HasValue || item.Id != existingTreeId.Value));
        if (await db.FileUploadOperations.CountAsync(item => item.CompletedAtUtc == null, cancellationToken) +
            await db.FileMutationOperations.CountAsync(item => item.CompletedAtUtc == null, cancellationToken) +
            await db.FileArchiveOperations.CountAsync(item => item.CompletedAtUtc == null, cancellationToken) + await trees.CountAsync(cancellationToken) + await moves.CountAsync(cancellationToken) + await packings.CountAsync(cancellationToken) + await unpackings.CountAsync(cancellationToken) + await recyclings.CountAsync(cancellationToken) + await restorings.CountAsync(cancellationToken) + await overwrites.CountAsync(cancellationToken) >= 64 ||
            await db.FileUploadOperations.CountAsync(item => item.CompletedAtUtc == null && item.ActorSubject == actor, cancellationToken) +
            await db.FileMutationOperations.CountAsync(item => item.CompletedAtUtc == null && item.ActorSubject == actor, cancellationToken) +
            await db.FileArchiveOperations.CountAsync(item => item.CompletedAtUtc == null && item.ActorSubject == actor, cancellationToken) + await trees.CountAsync(item => item.ActorSubject == actor, cancellationToken) + await moves.CountAsync(item => item.ActorSubject == actor, cancellationToken) + await packings.CountAsync(item => item.ActorSubject == actor, cancellationToken) + await unpackings.CountAsync(item => item.ActorSubject == actor, cancellationToken) + await recyclings.CountAsync(item => item.ActorSubject == actor, cancellationToken) + await restorings.CountAsync(item => item.ActorSubject == actor, cancellationToken) + await overwrites.CountAsync(item => item.ActorSubject == actor, cancellationToken) >= 4)
            throw new InvalidOperationException("FILES_OPERATION_BUDGET_EXHAUSTED");
    }
}
