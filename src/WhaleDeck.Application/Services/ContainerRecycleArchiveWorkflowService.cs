using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

/// <summary>Internal archive transitions; HTTP/UI exposure follows separate acceptance.</summary>
public sealed class ContainerRecycleArchiveWorkflowService(ContainerFileUploadService authorization, IContainerRecycleArchiveRepository repository,
    IContainerRecycleArchiveProtector protector, IContainerRecycleArchiveGateway gateway, TimeProvider clock, IPrivilegedIdentityDirectory liveIdentity, ContainerRecycleArchiveSourceResolver sources)
{
    public async Task AuthorizeAsync(TerminalActor actor, CancellationToken cancellationToken)
    {
        FileManagementService.ValidateActor(actor);
        var current = await liveIdentity.ResolveForPrivilegedActionAsync(actor.Subject, actor.Name, cancellationToken);
        if (current.Subject != actor.Subject || !current.IsAdministrator || current.PermissionExpiresAtUtc <= clock.GetUtcNow())
            throw new UnauthorizedAccessException("CONTAINER_RECYCLE_ARCHIVE_CURRENT_ADMINISTRATOR_REQUIRED");
        await authorization.AuthorizeAsync(actor, cancellationToken);
    }
    public async Task<ContainerRecycleArchiveIntentDto> QueueAsync(TerminalActor actor, string containerId, Guid sourceIntentId, string key, CancellationToken cancellationToken)
    {
        ContainerFileValidation.Id(containerId); if (sourceIntentId == Guid.Empty) throw new ArgumentException("CONTAINER_RECYCLE_ARCHIVE_SOURCE_REQUIRED");
        await AuthorizeAsync(actor, cancellationToken);
        var idempotency = ContainerRecycleArchiveWorkflowValidation.Key(key);
        var id = ContainerRecycleArchiveWorkflowValidation.IntentId(actor.Subject, actor.LoginBinding, idempotency);
        var prior = await repository.OwnedAsync(actor, id, cancellationToken);
        if (prior is not null)
        {
            Validate(prior);
            if (prior.SourceIntentId != sourceIntentId || prior.ContainerId != containerId) throw new InvalidOperationException("CONTAINER_RECYCLE_ARCHIVE_IDEMPOTENCY_CONFLICT");
            await AuthorizeAsync(actor, cancellationToken);
            return Map(prior);
        }
        var source = await sources.ResolveAsync(actor, sourceIntentId, cancellationToken);
        var command = ContainerRecycleArchiveWorkflowValidation.SnapshotCommand(source.Command);
        if (command.Purge.Plan.Command.Source.Plan.Command.Location.ContainerId != containerId) throw new KeyNotFoundException("CONTAINER_RECYCLE_ARCHIVE_SOURCE_NOT_FOUND");
        await gateway.RequireContainerRecycleArchiveAsync(cancellationToken); await AuthorizeAsync(actor, cancellationToken);
        var now = ContainerRecycleArchiveWorkflowValidation.Time(clock.GetUtcNow());
        var binding = new ContainerRecycleArchiveRequestBinding(id, idempotency, actor.Subject, actor.LoginBinding, actor.TraceId,
            ContainerRecycleArchiveWorkflowValidation.Owner(actor.Subject, actor.LoginBinding), source.IntentId, source.ProofHash, command, now, now.AddMinutes(15));
        ContainerRecycleArchiveWorkflowValidation.Request(binding);
        var row = new ContainerRecycleArchiveIntent { Id = id, IdempotencyKey = idempotency, ActorSubject = actor.Subject, LoginBinding = actor.LoginBinding,
            TraceId = actor.TraceId, OwnerHash = binding.Owner, SourceIntentId = source.IntentId, SourceProofHash = source.ProofHash, ContainerId = command.Purge.Plan.Command.Source.Plan.Command.Location.ContainerId, InstanceId = command.Purge.Plan.Command.Source.Plan.Command.Location.InstanceId,
            RelativePath = command.Purge.Plan.Command.Source.Plan.Command.Location.Path, CommandJson = ContainerRecycleArchiveWorkflowValidation.Serialize(command),
            RequestHash = protector.FingerprintRecycleArchiveRequest(binding), CreatedAtUtc = now, PlanningExpiresAtUtc = binding.PlanningExpiresAtUtc, NextAttemptAtUtc = now };
        var queued = await repository.EnqueueAsync(row, cancellationToken);
        await AuthorizeAsync(actor, cancellationToken); return Map(queued);
    }
    public async Task<ContainerRecycleArchiveIntentDto> GetAsync(TerminalActor actor, string containerId, Guid id, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken); var row = await Owned(actor, containerId, id, cancellationToken);
        await AuthorizeAsync(actor, cancellationToken); return Map(row);
    }
    public Task<ContainerRecycleArchiveIntentDto> GetByKeyAsync(TerminalActor actor, string containerId, string key, CancellationToken cancellationToken) =>
        GetAsync(actor, containerId, ContainerRecycleArchiveWorkflowValidation.IntentId(actor.Subject, actor.LoginBinding, ContainerRecycleArchiveWorkflowValidation.Key(key)), cancellationToken);
    public async Task<ContainerRecycleArchiveIntentDto> ConfirmAsync(TerminalActor actor, string containerId, Guid id, ConfirmContainerRecycleArchiveCommand command, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        if (command is null || !command.Confirmed || command.Version < 0) throw new ArgumentException("CONTAINER_RECYCLE_ARCHIVE_CONFIRMATION_REQUIRED");
        var row = await Owned(actor, containerId, id, cancellationToken);
        var claims = protector.VerifyRecycleArchiveConfirmation(command.PlanHash); ContainerRecycleArchiveWorkflowValidation.Confirmation(claims);
        if (claims.Id != id || claims.Actor != actor.Subject || claims.LoginBinding != actor.LoginBinding) throw new UnauthorizedAccessException("CONTAINER_RECYCLE_ARCHIVE_PLAN_OWNER_REQUIRED");
        if (row.PlanHash != claims.PlanHash || row.ReadyExpiresAtUtc != claims.ExpiresAtUtc) throw new InvalidOperationException("CONTAINER_RECYCLE_ARCHIVE_PLAN_CHANGED");
        await AuthorizeAsync(actor, cancellationToken);
        await RequireCurrentSourceAsync(row, actor, cancellationToken);
        var confirmed = await repository.ConfirmAsync(actor, claims, command.Version, cancellationToken);
        await AuthorizeAsync(actor, cancellationToken); return Map(confirmed);
    }
    public async Task<ContainerRecycleArchiveIntentDto> CancelAsync(TerminalActor actor, string containerId, Guid id, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken); _ = await Owned(actor, containerId, id, cancellationToken);
        await AuthorizeAsync(actor, cancellationToken); await repository.CancelAsync(actor, id, cancellationToken);
        var canceled = await Owned(actor, containerId, id, cancellationToken);
        await AuthorizeAsync(actor, cancellationToken); return Map(canceled);
    }
    public async Task<IReadOnlyList<ContainerRecycleArchiveIntentDto>> UnresolvedAsync(TerminalActor actor, int take, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        if (take is < 1 or > 50) throw new ArgumentException("CONTAINER_RECYCLE_ARCHIVE_PAGE_INVALID");
        var rows = await repository.UnresolvedAsync(take, cancellationToken);
        await AuthorizeAsync(actor, cancellationToken); return rows.Select(Map).ToArray();
    }
    public async Task<ContainerRecycleArchiveIntentDto> RecoveryGetAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        if (id == Guid.Empty) throw new ArgumentException("CONTAINER_RECYCLE_ARCHIVE_ID_REQUIRED");
        var row = await repository.FindAsync(id, cancellationToken) ?? throw new KeyNotFoundException("CONTAINER_RECYCLE_ARCHIVE_NOT_FOUND");
        await AuthorizeAsync(actor, cancellationToken); return Map(row);
    }
    public async Task AcknowledgeAsync(TerminalActor actor, Guid id, AcknowledgeContainerRecycleArchiveCommand command, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        if (id == Guid.Empty || command is null || !command.Confirmed || command.Version < 0) throw new ArgumentException("CONTAINER_RECYCLE_ARCHIVE_CONFIRMATION_REQUIRED");
        var row = await repository.FindAsync(id, cancellationToken) ?? throw new KeyNotFoundException("CONTAINER_RECYCLE_ARCHIVE_NOT_FOUND"); Validate(row);
        if (row.State != "Unknown" || row.ExecutionJobId is null || row.CompletedAtUtc is not null || row.Version != command.Version)
            throw new InvalidOperationException("CONTAINER_RECYCLE_ARCHIVE_ACKNOWLEDGEMENT_CONFLICT");
        var operation = ContainerRecycleArchiveWorkflowValidation.Reference(row);
        // Discard observes/fences only the ORIGINAL execution ID. A Reserved
        // reply is not final and cannot release database protection.
        await AuthorizeAsync(actor, cancellationToken);
        var receipt = await gateway.DiscardContainerRecycleArchiveAsync(operation, cancellationToken); ContainerRecycleArchiveWorkflowValidation.RequireCompatibleReceipt(row, receipt);
        if (receipt.State == "Reserved") throw new InvalidOperationException("CONTAINER_RECYCLE_ARCHIVE_RESULT_UNRESOLVED");
        await AuthorizeAsync(actor, cancellationToken);
        await repository.AcknowledgeAsync(actor, id, command.Version, receipt, cancellationToken);
    }
    private async Task<ContainerRecycleArchiveIntent> Owned(TerminalActor actor, string containerId, Guid id, CancellationToken cancellationToken)
    {
        ContainerFileValidation.Id(containerId); if (id == Guid.Empty) throw new ArgumentException("CONTAINER_RECYCLE_ARCHIVE_ID_REQUIRED");
        var row = await repository.OwnedAsync(actor, id, cancellationToken) ?? throw new KeyNotFoundException("CONTAINER_RECYCLE_ARCHIVE_NOT_FOUND");
        if (row.ContainerId != containerId) throw new KeyNotFoundException("CONTAINER_RECYCLE_ARCHIVE_NOT_FOUND"); Validate(row); return row;
    }
    private void Validate(ContainerRecycleArchiveIntent row)
    { if (!ContainerRecycleArchiveWorkflowValidation.HasValidBinding(row, protector)) throw new InvalidOperationException("CONTAINER_RECYCLE_ARCHIVE_BINDING_CHANGED"); }
    private ContainerRecycleArchiveIntentDto Map(ContainerRecycleArchiveIntent row)
    {
        Validate(row); var plan = row.PlanJson is null ? null : ContainerRecycleArchiveWorkflowValidation.ReadPlan(row);
        var ready = row.State == "Ready" && row.CancelRequestedAtUtc is null && row.ReadyExpiresAtUtc > clock.GetUtcNow();
        var confirmation = ready ? new ContainerRecycleArchiveConfirmationDto(protector.SignRecycleArchiveConfirmation(new(row.Id, row.ActorSubject, row.LoginBinding, row.PlanHash!, row.ReadyExpiresAtUtc!.Value)),
            row.ReadyExpiresAtUtc.Value, "预检不会恢复文件。确认后重新核对成功清理的来源与HDD备份，在原目录以新inode复制恢复，保留清理历史和HDD备份；不会覆盖已有文件。断线后查询同一任务，不自动重做。Unknown保留保护，明确处置不代表复制成功。") : null;
        var command = ContainerRecycleArchiveWorkflowValidation.ReadCommand(row); var source = command.Purge.Plan.Command.Source;
        return new(row.Id, row.Id, row.ExecutionJobId, row.SourceIntentId, command.Purge.OperationId, source.OperationId, command.HistoryDigest, source.Plan.Command.Location, row.State == "Ready" && !ready ? "Expired" : row.State,
            plan?.DeletedIdentity.OwnerUid, plan?.DeletedIdentity.OwnerGid, plan?.DeletedIdentity.Mode, plan?.OwnerUid, plan?.OwnerGid,
            source.Plan.SourceIdentity.SizeBytes, source.Plan.Sha256, source.Plan.Attributes.Count, row.ReadyExpiresAtUtc, row.ResultCode, row.Version,
            row.ReservationJson is not null, row.ArchiveCopied, ReceiptSummary(ContainerRecycleArchiveWorkflowValidation.ReadReceipt(row)), confirmation);
    }
    private static ContainerRecycleArchiveReceiptSummary? ReceiptSummary(ContainerRecycleArchiveReceiptDto? r)
    {
        if(r is null)return null;
        var i=r.PublishedIdentity;
        var published=i is null?null:new ContainerRecycleArchivePublishedIdentity(i.Device.ToString(System.Globalization.CultureInfo.InvariantCulture),i.Inode.ToString(System.Globalization.CultureInfo.InvariantCulture),i.Mode,i.OwnerUid,i.OwnerGid,i.LinkCount.ToString(System.Globalization.CultureInfo.InvariantCulture),i.SizeBytes,i.ModifiedAtUnixNs.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return new(r.Id,r.SourceOperationId,r.PurgeOperationId,r.Fingerprint,r.State,r.SourceFenced,r.OwnedFence,r.ArchiveCopied,r.BindingCurrent,published,r.PublishedChangedAtUnixNs?.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
    public async Task RequireCurrentSourceAsync(ContainerRecycleArchiveIntent row, TerminalActor actor, CancellationToken cancellationToken)
    {
        var current = await sources.ResolveAsync(actor, row.SourceIntentId, cancellationToken);
        if (current.ProofHash != row.SourceProofHash || !ContainerRecycleArchiveWorkflowValidation.SameCommand(current.Command, ContainerRecycleArchiveWorkflowValidation.ReadCommand(row)))
            throw new InvalidOperationException("CONTAINER_RECYCLE_ARCHIVE_SOURCE_CHANGED");
    }
}
