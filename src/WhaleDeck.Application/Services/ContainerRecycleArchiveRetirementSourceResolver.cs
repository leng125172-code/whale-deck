using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

/// <summary>Resolve a durable archive copy for retirement planning under a current administrator session.</summary>
public sealed class ContainerRecycleArchiveRetirementSourceResolver(IContainerRecycleArchiveRepository archives, IContainerRecycleArchiveProtector archiveProtector,
    IContainerRecycleArchiveRetirementSourceProtector protector, ContainerRecycleArchiveSourceResolver ancestors, ContainerRecycleArchiveWorkflowService authorization)
{
    public async Task<ResolvedContainerRecycleArchiveRetirementSource> ResolveAsync(TerminalActor actor, string containerId, Guid archiveIntentId,
        uint retainDays, CancellationToken cancellationToken)
    {
        ContainerFileValidation.Id(containerId);
        if (archiveIntentId == Guid.Empty || retainDays is < 1 or > 3650) throw new ArgumentException("CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_SOURCE_REQUIRED");
        await authorization.AuthorizeAsync(actor, cancellationToken);
        var archive = await archives.FindAsync(archiveIntentId, cancellationToken);
        if (archive is null || archive.Id != archiveIntentId || archive.ContainerId != containerId)
            throw new KeyNotFoundException("CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_SOURCE_NOT_FOUND");
        if (!ContainerRecycleArchiveWorkflowValidation.HasValidBinding(archive, archiveProtector)) throw Changed();
        var source = await ancestors.ResolveAsync(actor, archive.SourceIntentId, cancellationToken);
        var resolved = FromVerifiedRows(archive, source, retainDays, archiveProtector, protector);
        await authorization.AuthorizeAsync(actor, cancellationToken);
        return resolved;
    }

    /// <summary>Call with a revalidated purge and recycle source. Transactional callers must hold all three rows locked.</summary>
    public static ResolvedContainerRecycleArchiveRetirementSource FromVerifiedRows(ContainerRecycleArchiveIntent archive, ResolvedContainerRecycleArchiveSource source,
        uint retainDays, IContainerRecycleArchiveProtector archiveProtector, IContainerRecycleArchiveRetirementSourceProtector protector)
    {
        if (archive.State != "Succeeded" || archive.PublishBarrier || archive.CompletedAtUtc is null ||
            !ContainerRecycleArchiveWorkflowValidation.HasValidBinding(archive, archiveProtector) ||
            source.IntentId != archive.SourceIntentId || source.ProofHash != archive.SourceProofHash ||
            !ContainerRecycleArchiveWorkflowValidation.SameCommand(source.Command, ContainerRecycleArchiveWorkflowValidation.ReadCommand(archive))) throw Changed();

        var receipt = ContainerRecycleArchiveWorkflowValidation.ReadReceipt(archive);
        if (receipt is not { State: "Completed", ArchiveCopied: true, SourceFenced: true, OwnedFence: true, BindingCurrent: true,
            PublishedIdentity: { } published, PublishedChangedAtUnixNs: long changedAt }) throw Changed();

        var operation = ContainerRecycleArchiveWorkflowValidation.Reference(archive);
        var command = new ContainerRecycleArchiveRetirementCommandDto(new(operation.Id, operation.Owner, operation.Plan), retainDays, "ArchiveData");
        ContainerRecycleArchiveRetirementValidation.Command(command);
        var binding = new ContainerRecycleArchiveRetirementSourceBinding(archive.Id, archive.RequestHash, archive.PlanHash!, archive.ExecutionHash!,
            archive.ReservationHash!, archive.ReceiptHash!, source.ProofHash, command, published, changedAt);
        return new(binding, protector.FingerprintRetirementSource(binding));
    }

    public static void RequireCurrentPlan(ResolvedContainerRecycleArchiveRetirementSource source, ContainerRecycleArchiveRetirementPlanDto plan)
    {
        ContainerRecycleArchiveRetirementValidation.Plan(plan);
        if (!plan.BindingCurrent || plan.PublishedIdentity != source.Binding.PublishedIdentity ||
            plan.PublishedChangedAtUnixNs != source.Binding.PublishedChangedAtUnixNs ||
            ContainerRecycleArchiveWorkflowValidation.Serialize(plan.Command) != ContainerRecycleArchiveWorkflowValidation.Serialize(source.Binding.Command))
            throw Changed();
    }

    private static InvalidOperationException Changed() => new("CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_SOURCE_CHANGED");
}
