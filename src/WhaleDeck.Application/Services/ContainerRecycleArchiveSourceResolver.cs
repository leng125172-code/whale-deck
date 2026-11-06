using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

/// <summary>Only the durable proven purge and its original recycle row supply copy authority.</summary>
public sealed class ContainerRecycleArchiveSourceResolver(IContainerRecyclePurgeRepository purges, IContainerRecycleRepository originals,
    IContainerRecyclePurgeProtector purgeProtector, IContainerRecycleProtector originalProtector, IContainerRecycleArchiveProtector protector,
    ContainerFileUploadService authorization, IPrivilegedIdentityDirectory identities, TimeProvider clock)
{
    public async Task<ResolvedContainerRecycleArchiveSource> ResolveAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken)
    {
        await Authorize(actor, cancellationToken);
        if (id == Guid.Empty) throw new ArgumentException("CONTAINER_RECYCLE_ARCHIVE_SOURCE_REQUIRED");
        var purge = await purges.FindAsync(id, cancellationToken) ?? throw new KeyNotFoundException("CONTAINER_RECYCLE_ARCHIVE_SOURCE_NOT_FOUND");
        if (purge.Id != id || !ContainerRecyclePurgeWorkflowValidation.HasValidBinding(purge, purgeProtector)) throw Changed();
        var original = await originals.FindAsync(purge.SourceIntentId, cancellationToken) ?? throw Changed();
        var resolved = FromRows(purge, original, purgeProtector, originalProtector, protector);
        await Authorize(actor, cancellationToken); return resolved;
    }

    public static ResolvedContainerRecycleArchiveSource FromRows(ContainerRecyclePurgeIntent purge, ContainerRecycleIntent original,
        IContainerRecyclePurgeProtector purgeProtector, IContainerRecycleProtector originalProtector, IContainerRecycleArchiveProtector protector)
    {
        if (!ContainerRecyclePurgeWorkflowValidation.HasValidBinding(purge, purgeProtector) || original.Id != purge.SourceIntentId) throw Changed();
        var receipt = ContainerRecyclePurgeWorkflowValidation.ReadReceipt(purge);
        if (purge.State != "Succeeded" || purge.CompletedAtUtc is null || purge.PublishBarrier || purge.ExecutionJobId is null ||
            purge.RootStartedAtUtc is null || purge.ApplyStartedAtUtc is null || purge.ReservationJson is null ||
            receipt is not { State: "Completed", PurgeApplied: true, SourceFenced: true, OwnedFence: true }) throw Changed();
        // The recycle row is historical Trashed proof; Root's Purged lifecycle is
        // independently checked by Archive RPC. Never rewrite either old row.
        ResolvedContainerRecyclePurgeSource old;
        try { old = ContainerRecyclePurgeSourceResolver.FromRow(original, originalProtector, purgeProtector); }
        catch (InvalidOperationException error) when (error.Message == "CONTAINER_RECYCLE_PURGE_SOURCE_CHANGED") { throw Changed(); }
        var op = ContainerRecyclePurgeWorkflowValidation.Reference(purge);
        if (old.ProofHash != purge.SourceProofHash || !ContainerRecyclePurgeWorkflowValidation.SameCommand(old.Command, op.Plan.Command)) throw Changed();
        var command = ContainerRecycleArchiveWorkflowValidation.SnapshotCommand(new(new(op.Id, op.Owner, op.Plan), op.Plan.Command.HistoryDigest, "ArchiveCopy"));
        var binding = new ContainerRecycleArchiveSourceBinding(purge.Id, purge.RequestHash, purge.PlanHash!, purge.ExecutionHash!, purge.ReceiptHash!, old.ProofHash, command);
        return new(purge.Id, protector.FingerprintRecycleArchiveSource(binding), command);
    }

    private async Task Authorize(TerminalActor actor, CancellationToken token)
    {
        FileManagementService.ValidateActor(actor);
        var current = await identities.ResolveForPrivilegedActionAsync(actor.Subject, actor.Name, token);
        if (current.Subject != actor.Subject || !current.IsAdministrator || current.PermissionExpiresAtUtc <= clock.GetUtcNow())
            throw new UnauthorizedAccessException("CONTAINER_RECYCLE_ARCHIVE_CURRENT_ADMINISTRATOR_REQUIRED");
        await authorization.AuthorizeAsync(actor, token);
    }
    private static InvalidOperationException Changed() => new("CONTAINER_RECYCLE_ARCHIVE_SOURCE_CHANGED");
}
