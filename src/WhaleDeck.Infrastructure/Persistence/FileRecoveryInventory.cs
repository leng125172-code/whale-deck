using Microsoft.EntityFrameworkCore;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Infrastructure.Persistence;

/// <summary>Bounded cross-workflow discovery. Never changes an original owner, lease, receipt or path lock.</summary>
public sealed class FileRecoveryInventory(PlatformDbContext db, IFilePlanProtector uploads,
    IFileMutationProtector mutations, IFileArchiveProtector archives,
    IFileTreeCopyProtector copies, IFileTreeMoveProtector moves, IFileTreePackingProtector packings, IFileTreeUnpackingProtector unpackings,
    IFileTreeRecyclingProtector? recyclings = null, IFileTreeRestoringProtector? restorings = null, IFileTreeOverwriteRestoringProtector? overwrites = null,
    IFileTreePurgingProtector? purgings = null, IContainerFileUploadProtector? containerUploads = null) : IFileRecoveryInventory
{
    private sealed class Header
    {
        public required string Kind { get; init; }
        public Guid Id { get; init; }
        public DateTimeOffset CreatedAtUtc { get; init; }
    }

    public async Task<FileRecoveryPageDto> ListAsync(FileRecoveryCursor? after, int take, CancellationToken cancellationToken)
    {
        if (take is < 1 or > FileRecoveryValidation.MaximumPageSize) throw new ArgumentException("FILES_RECOVERY_PAGE_INVALID");
        if (after is not null) _ = FileRecoveryValidation.EncodeCursor(after);
        List<Header> headers = [];
        // Project only small immutable keys first: do not load five pages of
        // potentially large private plans just to select one public page.
        await Add(db.FileArchiveOperations.AsNoTracking().Where(row => row.State == "Unknown" && row.CompletedAtUtc == null)
            .Select(row => new Header { Kind = FileRecoveryKinds.Archive, Id = row.Id, CreatedAtUtc = row.CreatedAtUtc }), FileRecoveryKinds.Archive);
        await Add(db.FileMutationOperations.AsNoTracking().Where(row => row.State == "Unknown" && row.CompletedAtUtc == null)
            .Select(row => new Header { Kind = FileRecoveryKinds.Mutation, Id = row.Id, CreatedAtUtc = row.CreatedAtUtc }), FileRecoveryKinds.Mutation);
        await Add(db.FileTreeCopyOperations.AsNoTracking().Where(row => row.State == "Unknown" && row.CompletedAtUtc == null)
            .Select(row => new Header { Kind = FileRecoveryKinds.TreeCopy, Id = row.Id, CreatedAtUtc = row.CreatedAtUtc }), FileRecoveryKinds.TreeCopy);
        await Add(db.FileTreeMoveOperations.AsNoTracking().Where(row => row.State == "Unknown" && row.CompletedAtUtc == null)
            .Select(row => new Header { Kind = FileRecoveryKinds.TreeMove, Id = row.Id, CreatedAtUtc = row.CreatedAtUtc }), FileRecoveryKinds.TreeMove);
        await Add(db.FileUploadOperations.AsNoTracking().Where(row => row.State == "Unknown" && row.CompletedAtUtc == null)
            .Select(row => new Header { Kind = FileRecoveryKinds.Upload, Id = row.Id, CreatedAtUtc = row.CreatedAtUtc }), FileRecoveryKinds.Upload);
        await Add(db.FileTreePackingOperations.AsNoTracking().Where(row => row.State == "Unknown" && row.CompletedAtUtc == null)
            .Select(row => new Header { Kind = FileRecoveryKinds.TreePacking, Id = row.Id, CreatedAtUtc = row.CreatedAtUtc }), FileRecoveryKinds.TreePacking);
        await Add(db.FileTreeUnpackingOperations.AsNoTracking().Where(row => row.State == "Unknown" && row.CompletedAtUtc == null)
            .Select(row => new Header { Kind = FileRecoveryKinds.TreeUnpacking, Id = row.Id, CreatedAtUtc = row.CreatedAtUtc }), FileRecoveryKinds.TreeUnpacking);
        await Add(db.FileTreeRecyclingOperations.AsNoTracking().Where(row => row.State == "Unknown" && row.CompletedAtUtc == null)
            .Select(row => new Header { Kind = FileRecoveryKinds.TreeRecycling, Id = row.Id, CreatedAtUtc = row.CreatedAtUtc }), FileRecoveryKinds.TreeRecycling);
        await Add(db.FileTreeRestoringOperations.AsNoTracking().Where(row => row.State == "Unknown" && row.CompletedAtUtc == null)
            .Select(row => new Header { Kind = FileRecoveryKinds.TreeRestoring, Id = row.Id, CreatedAtUtc = row.CreatedAtUtc }), FileRecoveryKinds.TreeRestoring);
        await Add(db.FileTreeOverwriteRestoringOperations.AsNoTracking().Where(row => row.State == "Unknown" && row.CompletedAtUtc == null)
            .Select(row => new Header { Kind = FileRecoveryKinds.TreeOverwriteRestoring, Id = row.Id, CreatedAtUtc = row.CreatedAtUtc }), FileRecoveryKinds.TreeOverwriteRestoring);
        await Add(db.FileTreePurgingOperations.AsNoTracking().Where(row => row.State == "Unknown" && row.CompletedAtUtc == null)
            .Select(row => new Header { Kind = FileRecoveryKinds.TreePurging, Id = row.Id, CreatedAtUtc = row.CreatedAtUtc }), FileRecoveryKinds.TreePurging);
        await Add(db.ContainerFileUploadOperations.AsNoTracking().Where(row => row.State == "Unknown" && row.CompletedAtUtc == null)
            .Select(row => new Header { Kind = FileRecoveryKinds.ContainerUpload, Id = row.Id, CreatedAtUtc = row.CreatedAtUtc }), FileRecoveryKinds.ContainerUpload);
        var ordered = headers.OrderBy(row => row.CreatedAtUtc).ThenBy(row => FileRecoveryValidation.Rank(row.Kind)).ThenBy(row => row.Id).Take(take + 1).ToArray();
        List<FileRecoveryCandidateDto> entries = [];
        foreach (var header in ordered.Take(take))
        {
            var row = await GetAsync(header.Kind, header.Id, cancellationToken);
            // A concurrent real terminal result may remove eligibility. Advance
            // through the examined header, not back to a previous key.
            if (row is not null) entries.Add(row);
        }
        var last = ordered.Take(take).LastOrDefault();
        return new(entries, ordered.Length > take && last is not null ? FileRecoveryValidation.EncodeCursor(new(last.CreatedAtUtc, last.Kind, last.Id)) : null);

        async Task Add(IQueryable<Header> query, string kind)
        {
            if (after is not null)
            {
                var rank = FileRecoveryValidation.Rank(kind); var afterRank = FileRecoveryValidation.Rank(after.Kind);
                query = query.Where(row => row.CreatedAtUtc > after.CreatedAtUtc || row.CreatedAtUtc == after.CreatedAtUtc &&
                    (rank > afterRank || rank == afterRank && row.Id.CompareTo(after.IntentId) > 0));
            }
            headers.AddRange(await query.OrderBy(row => row.CreatedAtUtc).ThenBy(row => row.Id).Take(take + 1).ToArrayAsync(cancellationToken));
        }
    }
    public async Task<FileRecoveryCandidateDto?> GetAsync(string kind, Guid id, CancellationToken cancellationToken)
    {
        FileRecoveryValidation.Kind(kind); if (id == Guid.Empty) throw new ArgumentException("FILES_RECOVERY_ID_INVALID");
        switch (kind)
        {
            case FileRecoveryKinds.ContainerUpload:
            {
                var row = await db.ContainerFileUploadOperations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.State == "Unknown" && x.CompletedAtUtc == null, cancellationToken);
                if (row is null) return null;
                var valid = containerUploads is not null && ContainerFileUploadService.HasValidBinding(row, containerUploads);
                return Candidate(row.Id, row.Id, row.Version, row.CreatedAtUtc, row.LeaseExpiresAtUtc, row.RootStartedAtUtc, valid,
                    null, null, row.PreviousRetained) with { Container = valid ? new(row.ContainerId, row.InstanceId, row.RelativePath) : null };
            }
            case FileRecoveryKinds.Upload:
            {
                var row = await db.FileUploadOperations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.State == "Unknown" && x.CompletedAtUtc == null, cancellationToken);
                if (row is null) return null;
                var valid = FileManagementService.HasValidBinding(row, uploads);
                return Candidate(row.Id, row.Id, row.Version, row.CreatedAtUtc, row.LeaseExpiresAtUtc, row.RootStartedAtUtc, valid,
                    null, valid ? new(row.RootId, row.RelativePath) : null, row.PreviousRetained);
            }
            case FileRecoveryKinds.Mutation:
            {
                var row = await db.FileMutationOperations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.State == "Unknown" && x.CompletedAtUtc == null, cancellationToken);
                if (row is null) return null;
                var valid = FileMutationService.HasValidBinding(row, mutations); var plan = valid ? FileMutationService.ReadPlan(row) : null;
                return Candidate(row.Id, row.Id, row.Version, row.CreatedAtUtc, row.LeaseExpiresAtUtc, row.RootStartedAtUtc, valid,
                    plan?.Command.Source, plan?.Command.Target, row.SourceRetained);
            }
            case FileRecoveryKinds.Archive:
            {
                var row = await db.FileArchiveOperations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.State == "Unknown" && x.CompletedAtUtc == null, cancellationToken);
                if (row is null) return null;
                var valid = FileArchiveValidation.HasValidBinding(row, archives);
                return Candidate(row.Id, row.Id, row.Version, row.CreatedAtUtc, row.LeaseExpiresAtUtc, row.RootStartedAtUtc, valid,
                    valid ? new(row.RootId, row.RelativePath) : null, null, row.SourceRetained);
            }
            case FileRecoveryKinds.TreeCopy:
            {
                var row = await db.FileTreeCopyOperations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.State == "Unknown" && x.CompletedAtUtc == null, cancellationToken);
                if (row is null) return null;
                var valid = FileTreeCopyValidation.HasValidBinding(row, copies) && row.ExecutionJobId is not null;
                var command = valid ? FileTreeCopyValidation.ReadCommand(row) : null;
                return Candidate(row.Id, row.ExecutionJobId, row.Version, row.CreatedAtUtc, row.LeaseExpiresAtUtc, row.RootStartedAtUtc, valid,
                    command?.Source, command?.Target, row.PartialRetained);
            }
            case FileRecoveryKinds.TreePacking:
            {
                var row = await db.FileTreePackingOperations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.State == "Unknown" && x.CompletedAtUtc == null, cancellationToken);
                if (row is null) return null;
                var valid = FileTreePackingValidation.HasValidBinding(row, packings) && row.ExecutionJobId is not null;
                var command = valid ? FileTreePackingValidation.ReadCommand(row) : null;
                return Candidate(row.Id, row.ExecutionJobId, row.Version, row.CreatedAtUtc, row.LeaseExpiresAtUtc, row.RootStartedAtUtc, valid,
                    command?.Source, command?.Target, row.PartialRetained);
            }
            case FileRecoveryKinds.TreeUnpacking:
            {
                var row = await db.FileTreeUnpackingOperations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.State == "Unknown" && x.CompletedAtUtc == null, cancellationToken);
                if (row is null) return null;
                var valid = FileTreeUnpackingValidation.HasValidBinding(row, unpackings) && row.ExecutionJobId is not null;
                var command = valid ? FileTreeUnpackingValidation.ReadCommand(row) : null;
                return Candidate(row.Id, row.ExecutionJobId, row.Version, row.CreatedAtUtc, row.LeaseExpiresAtUtc, row.RootStartedAtUtc, valid,
                    command?.Source, command?.Target, row.PartialRetained);
            }
            case FileRecoveryKinds.TreeRecycling:
            {
                var row = await db.FileTreeRecyclingOperations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.State == "Unknown" && x.CompletedAtUtc == null, cancellationToken);
                if (row is null) return null;
                var valid = recyclings is not null && FileTreeRecyclingValidation.HasValidBinding(row, recyclings) && row.ExecutionJobId is not null;
                var command = valid ? FileTreeRecyclingValidation.ReadCommand(row) : null;
                return Candidate(row.Id, row.ExecutionJobId, row.Version, row.CreatedAtUtc, row.LeaseExpiresAtUtc, row.RootStartedAtUtc, valid,
                    command?.Source, null, row.CaptureMayBeRetained || row.OriginalsRetained);
            }
            case FileRecoveryKinds.TreeRestoring:
            {
                var row = await db.FileTreeRestoringOperations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.State == "Unknown" && x.CompletedAtUtc == null, cancellationToken);
                if (row is null) return null;
                var valid = restorings is not null && FileTreeRestoringValidation.HasValidBinding(row, restorings) && row.ExecutionJobId is not null;
                var command = valid ? FileTreeRestoringValidation.ReadCommand(row) : null;
                return Candidate(row.Id, row.ExecutionJobId, row.Version, row.CreatedAtUtc, row.LeaseExpiresAtUtc, row.RootStartedAtUtc, valid,
                    command?.Source, command?.Target, row.PartialRetained);
            }
            case FileRecoveryKinds.TreeOverwriteRestoring:
            {
                var row = await db.FileTreeOverwriteRestoringOperations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.State == "Unknown" && x.CompletedAtUtc == null, cancellationToken);
                if (row is null) return null;
                var valid = overwrites is not null && FileTreeOverwriteRestoringValidation.HasValidBinding(row, overwrites) && row.ExecutionJobId is not null;
                var command = valid ? FileTreeOverwriteRestoringValidation.ReadCommand(row) : null;
                return Candidate(row.Id, row.ExecutionJobId, row.Version, row.CreatedAtUtc, row.LeaseExpiresAtUtc, row.RootStartedAtUtc, valid,
                    command?.Source, command?.Target, row.PartialRetained || row.BackupMayBeRetained || row.OriginalsRetained);
            }
            case FileRecoveryKinds.TreePurging:
            {
                var row = await db.FileTreePurgingOperations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.State == "Unknown" && x.CompletedAtUtc == null, cancellationToken);
                if (row is null) return null;
                var valid = purgings is not null && FileTreePurgingValidation.HasValidBinding(row, purgings) && row.ExecutionJobId is not null;
                var command = valid ? FileTreePurgingValidation.ReadCommand(row) : null;
                // A negative purge flag is not proof of any retained object.
                return Candidate(row.Id, row.ExecutionJobId, row.Version, row.CreatedAtUtc, row.LeaseExpiresAtUtc, row.RootStartedAtUtc, valid,
                    command?.Source, null, false) with { Purge = valid ? FileTreePurgingValidation.RecoverySummary(row, purgings!) : null };
            }
            case FileRecoveryKinds.TreeMove:
            {
                var row = await db.FileTreeMoveOperations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.State == "Unknown" && x.CompletedAtUtc == null, cancellationToken);
                if (row is null) return null;
                var valid = FileTreeMoveValidation.HasValidBinding(row, moves) && row.ExecutionJobId is not null;
                var command = valid ? FileTreeMoveValidation.ReadCommand(row) : null;
                return Candidate(row.Id, row.ExecutionJobId, row.Version, row.CreatedAtUtc, row.LeaseExpiresAtUtc, row.RootStartedAtUtc, valid,
                    command?.Source, command?.Target, row.PartialRetained || row.SourceRetained == true);
            }
            default: throw new ArgumentException("FILES_RECOVERY_KIND_INVALID");
        }
        FileRecoveryCandidateDto Candidate(Guid intentId, Guid? execution, long version, DateTimeOffset created,
            DateTimeOffset? lease, DateTimeOffset? started, bool valid, FileLocationDto? source, FileLocationDto? target, bool retained) =>
            new(kind, intentId, execution, version, created, lease, started is not null, valid ? "Verified" : "InvalidBinding", source, target, valid && retained ? true : null);
    }
}
