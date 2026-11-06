using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

/// <summary>Only durable, MAC-verified recycle intents supply purge authority; never HTTP plan/owner fields.</summary>
public sealed class ContainerRecyclePurgeSourceResolver(IContainerRecycleRepository sources, IContainerRecycleProtector sourceProtector,
    IContainerRecyclePurgeProtector protector, ContainerFileUploadService authorization, IPrivilegedIdentityDirectory identities, TimeProvider clock)
{
    public async Task<ResolvedContainerRecyclePurgeSource> ResolveAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken)
    {
        await Authorize(actor, cancellationToken);
        if (id == Guid.Empty) throw new ArgumentException("CONTAINER_RECYCLE_PURGE_SOURCE_REQUIRED");
        var row = await sources.FindAsync(id, cancellationToken) ?? throw new KeyNotFoundException("CONTAINER_RECYCLE_PURGE_SOURCE_NOT_FOUND");
        if (row.Id != id) throw Changed();
        var resolved = FromRow(row, sourceProtector, protector);
        await Authorize(actor, cancellationToken); return resolved;
    }
    public static ResolvedContainerRecyclePurgeSource FromRow(ContainerRecycleIntent row, IContainerRecycleProtector sourceProtector, IContainerRecyclePurgeProtector protector)
    {
        if (!ContainerRecycleWorkflowValidation.HasValidBinding(row, sourceProtector)) throw Changed();
        var receipt = ContainerRecycleWorkflowValidation.ReadReceipt(row);
        if (row.State != "Succeeded" || row.CompletedAtUtc is null || row.PublishBarrier || row.CancelRequestedAtUtc is not null ||
            row.ExecutionJobId is null || row.RootStartedAtUtc is null || receipt is not { State: "Trashed", HistoryVerified: true, HistoryAvailable: true, BindingCurrent: true, SourceRetained: true, SourceRestored: false }) throw Changed();
        var original = ContainerRecycleWorkflowValidation.Reference(row);
        var command = ContainerRecyclePurgeWorkflowValidation.SnapshotCommand(new(new(original.Id, original.Owner, original.Plan), receipt.HistoryDigest, "Originals"));
        var binding = new ContainerRecyclePurgeSourceBinding(row.Id, row.RequestHash, row.PlanHash!, row.ExecutionHash!, row.ReceiptHash!, command);
        var proof = protector.FingerprintRecyclePurgeSource(binding);
        return new(row.Id, proof, command);
    }
    private async Task Authorize(TerminalActor actor, CancellationToken token)
    {
        FileManagementService.ValidateActor(actor);
        var current = await identities.ResolveForPrivilegedActionAsync(actor.Subject, actor.Name, token);
        if (current.Subject != actor.Subject || !current.IsAdministrator || current.PermissionExpiresAtUtc <= clock.GetUtcNow())
            throw new UnauthorizedAccessException("CONTAINER_RECYCLE_PURGE_CURRENT_ADMINISTRATOR_REQUIRED");
        await authorization.AuthorizeAsync(actor, token);
    }
    private static InvalidOperationException Changed() => new("CONTAINER_RECYCLE_PURGE_SOURCE_CHANGED");
}
