using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Services;

/// <summary>Only durable, MAC-verified recycle intents supply restoration authority; never HTTP plan/owner fields.</summary>
public sealed class ContainerRecycleRestoreSourceResolver(IContainerRecycleRepository sources, IContainerRecycleProtector sourceProtector,
    IContainerRecycleRestoreProtector protector, ContainerFileUploadService authorization, IPrivilegedIdentityDirectory identities, TimeProvider clock)
{
    public async Task<ResolvedContainerRecycleRestoreSource> ResolveAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken)
    {
        await Authorize(actor, cancellationToken);
        if (id == Guid.Empty) throw new ArgumentException("CONTAINER_RECYCLE_RESTORE_SOURCE_REQUIRED");
        var row = await sources.FindAsync(id, cancellationToken) ?? throw new KeyNotFoundException("CONTAINER_RECYCLE_RESTORE_SOURCE_NOT_FOUND");
        if (!ContainerRecycleWorkflowValidation.HasValidBinding(row, sourceProtector)) throw Changed();
        var receipt = ContainerRecycleWorkflowValidation.ReadReceipt(row);
        if (row.Id != id || row.State != "Succeeded" || row.CompletedAtUtc is null || row.PublishBarrier || row.CancelRequestedAtUtc is not null ||
            row.ExecutionJobId is null || row.RootStartedAtUtc is null || receipt is not { State: "Trashed", HistoryVerified: true, HistoryAvailable: true, BindingCurrent: true, SourceRetained: true, SourceRestored: false }) throw Changed();
        var original = ContainerRecycleWorkflowValidation.Reference(row);
        var command = ContainerRecycleRestoreWorkflowValidation.SnapshotCommand(new(new(original.Id, original.Owner, original.Plan), receipt.HistoryDigest));
        var binding = new ContainerRecycleRestoreSourceBinding(id, row.RequestHash, row.PlanHash!, row.ExecutionHash!, row.ReceiptHash!, command);
        var proof = protector.FingerprintRecycleRestoreSource(binding);
        await Authorize(actor, cancellationToken);
        return new(id, proof, command);
    }
    private async Task Authorize(TerminalActor actor, CancellationToken token)
    {
        FileManagementService.ValidateActor(actor);
        var current = await identities.ResolveForPrivilegedActionAsync(actor.Subject, actor.Name, token);
        if (current.Subject != actor.Subject || !current.IsAdministrator || current.PermissionExpiresAtUtc <= clock.GetUtcNow())
            throw new UnauthorizedAccessException("CONTAINER_RECYCLE_RESTORE_CURRENT_ADMINISTRATOR_REQUIRED");
        await authorization.AuthorizeAsync(actor, token);
    }
    private static InvalidOperationException Changed() => new("CONTAINER_RECYCLE_RESTORE_SOURCE_CHANGED");
}
