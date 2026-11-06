using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

/// <summary>Internal transitions only; not registered until persistent exclusion and real acceptance exist.</summary>
public sealed class ContainerRecyclePurgeWorkflowService(ContainerFileUploadService authorization, IContainerRecyclePurgeRepository repository,
    IContainerRecyclePurgeProtector protector, IContainerRecyclePurgeGateway gateway, TimeProvider clock, IPrivilegedIdentityDirectory liveIdentity, ContainerRecyclePurgeSourceResolver sources)
{
    public async Task AuthorizeAsync(TerminalActor actor, CancellationToken cancellationToken)
    {
        FileManagementService.ValidateActor(actor);
        var current = await liveIdentity.ResolveForPrivilegedActionAsync(actor.Subject, actor.Name, cancellationToken);
        if (current.Subject != actor.Subject || !current.IsAdministrator || current.PermissionExpiresAtUtc <= clock.GetUtcNow())
            throw new UnauthorizedAccessException("CONTAINER_RECYCLE_PURGE_CURRENT_ADMINISTRATOR_REQUIRED");
        await authorization.AuthorizeAsync(actor, cancellationToken);
    }
    public async Task<ContainerRecyclePurgeIntentDto> QueueAsync(TerminalActor actor, string containerId, Guid sourceIntentId, string key, CancellationToken cancellationToken)
    {
        ContainerFileValidation.Id(containerId); if (sourceIntentId == Guid.Empty) throw new ArgumentException("CONTAINER_RECYCLE_PURGE_SOURCE_REQUIRED");
        await AuthorizeAsync(actor, cancellationToken);
        var idempotency = ContainerRecyclePurgeWorkflowValidation.Key(key);
        var id = ContainerRecyclePurgeWorkflowValidation.IntentId(actor.Subject, actor.LoginBinding, idempotency);
        var prior = await repository.OwnedAsync(actor, id, cancellationToken);
        if (prior is not null)
        {
            Validate(prior);
            if (prior.SourceIntentId != sourceIntentId || prior.ContainerId != containerId) throw new InvalidOperationException("CONTAINER_RECYCLE_PURGE_IDEMPOTENCY_CONFLICT");
            await AuthorizeAsync(actor, cancellationToken);
            return Map(prior);
        }
        var source = await sources.ResolveAsync(actor, sourceIntentId, cancellationToken);
        var command = ContainerRecyclePurgeWorkflowValidation.SnapshotCommand(source.Command);
        if (command.Source.Plan.Command.Location.ContainerId != containerId) throw new KeyNotFoundException("CONTAINER_RECYCLE_PURGE_SOURCE_NOT_FOUND");
        await gateway.RequireContainerRecyclePurgeAsync(cancellationToken); await AuthorizeAsync(actor, cancellationToken);
        var now = ContainerRecyclePurgeWorkflowValidation.Time(clock.GetUtcNow());
        var binding = new ContainerRecyclePurgeRequestBinding(id, idempotency, actor.Subject, actor.LoginBinding, actor.TraceId,
            ContainerRecyclePurgeWorkflowValidation.Owner(actor.Subject, actor.LoginBinding), source.IntentId, source.ProofHash, command, now, now.AddMinutes(15));
        ContainerRecyclePurgeWorkflowValidation.Request(binding);
        var row = new ContainerRecyclePurgeIntent { Id = id, IdempotencyKey = idempotency, ActorSubject = actor.Subject, LoginBinding = actor.LoginBinding,
            TraceId = actor.TraceId, OwnerHash = binding.Owner, SourceIntentId = source.IntentId, SourceProofHash = source.ProofHash, ContainerId = command.Source.Plan.Command.Location.ContainerId, InstanceId = command.Source.Plan.Command.Location.InstanceId,
            RelativePath = command.Source.Plan.Command.Location.Path, CommandJson = ContainerRecyclePurgeWorkflowValidation.Serialize(command),
            RequestHash = protector.FingerprintRecyclePurgeRequest(binding), CreatedAtUtc = now, PlanningExpiresAtUtc = binding.PlanningExpiresAtUtc, NextAttemptAtUtc = now };
        var queued = await repository.EnqueueAsync(row, cancellationToken);
        await AuthorizeAsync(actor, cancellationToken); return Map(queued);
    }
    public async Task<ContainerRecyclePurgeIntentDto> GetAsync(TerminalActor actor, string containerId, Guid id, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken); var row = await Owned(actor, containerId, id, cancellationToken);
        await AuthorizeAsync(actor, cancellationToken); return Map(row);
    }
    public Task<ContainerRecyclePurgeIntentDto> GetByKeyAsync(TerminalActor actor, string containerId, string key, CancellationToken cancellationToken) =>
        GetAsync(actor, containerId, ContainerRecyclePurgeWorkflowValidation.IntentId(actor.Subject, actor.LoginBinding, ContainerRecyclePurgeWorkflowValidation.Key(key)), cancellationToken);
    public async Task<ContainerRecyclePurgeIntentDto> ConfirmAsync(TerminalActor actor, string containerId, Guid id, ConfirmContainerRecyclePurgeCommand command, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        if (command is null || !command.Confirmed || command.Version < 0) throw new ArgumentException("CONTAINER_RECYCLE_PURGE_CONFIRMATION_REQUIRED");
        var row = await Owned(actor, containerId, id, cancellationToken);
        var claims = protector.VerifyRecyclePurgeConfirmation(command.PlanHash); ContainerRecyclePurgeWorkflowValidation.Confirmation(claims);
        if (claims.Id != id || claims.Actor != actor.Subject || claims.LoginBinding != actor.LoginBinding) throw new UnauthorizedAccessException("CONTAINER_RECYCLE_PURGE_PLAN_OWNER_REQUIRED");
        if (row.PlanHash != claims.PlanHash || row.ReadyExpiresAtUtc != claims.ExpiresAtUtc) throw new InvalidOperationException("CONTAINER_RECYCLE_PURGE_PLAN_CHANGED");
        await AuthorizeAsync(actor, cancellationToken);
        await RequireCurrentSourceAsync(row, actor, cancellationToken);
        var confirmed = await repository.ConfirmAsync(actor, claims, command.Version, cancellationToken);
        await AuthorizeAsync(actor, cancellationToken); return Map(confirmed);
    }
    public async Task<ContainerRecyclePurgeIntentDto> CancelAsync(TerminalActor actor, string containerId, Guid id, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken); _ = await Owned(actor, containerId, id, cancellationToken);
        await AuthorizeAsync(actor, cancellationToken); await repository.CancelAsync(actor, id, cancellationToken);
        var canceled = await Owned(actor, containerId, id, cancellationToken);
        await AuthorizeAsync(actor, cancellationToken); return Map(canceled);
    }
    public async Task<IReadOnlyList<ContainerRecyclePurgeIntentDto>> UnresolvedAsync(TerminalActor actor, int take, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        if (take is < 1 or > 50) throw new ArgumentException("CONTAINER_RECYCLE_PURGE_PAGE_INVALID");
        var rows = await repository.UnresolvedAsync(take, cancellationToken);
        await AuthorizeAsync(actor, cancellationToken); return rows.Select(Map).ToArray();
    }
    public async Task<ContainerRecyclePurgeIntentDto> RecoveryGetAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        if (id == Guid.Empty) throw new ArgumentException("CONTAINER_RECYCLE_PURGE_ID_REQUIRED");
        var row = await repository.FindAsync(id, cancellationToken) ?? throw new KeyNotFoundException("CONTAINER_RECYCLE_PURGE_NOT_FOUND");
        await AuthorizeAsync(actor, cancellationToken); return Map(row);
    }
    public async Task AcknowledgeAsync(TerminalActor actor, Guid id, AcknowledgeContainerRecyclePurgeCommand command, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        if (id == Guid.Empty || command is null || !command.Confirmed || command.Version < 0) throw new ArgumentException("CONTAINER_RECYCLE_PURGE_CONFIRMATION_REQUIRED");
        var row = await repository.FindAsync(id, cancellationToken) ?? throw new KeyNotFoundException("CONTAINER_RECYCLE_PURGE_NOT_FOUND"); Validate(row);
        if (row.State != "Unknown" || row.ExecutionJobId is null || row.CompletedAtUtc is not null || row.Version != command.Version)
            throw new InvalidOperationException("CONTAINER_RECYCLE_PURGE_ACKNOWLEDGEMENT_CONFLICT");
        var operation = ContainerRecyclePurgeWorkflowValidation.Reference(row);
        // Discard observes/fences only the ORIGINAL execution ID. A Reserved
        // reply is not final and cannot release database protection.
        await AuthorizeAsync(actor, cancellationToken);
        var receipt = await gateway.DiscardContainerRecyclePurgeAsync(operation, cancellationToken); ContainerRecyclePurgeWorkflowValidation.RequireCompatibleReceipt(row, receipt);
        if (receipt.State == "Reserved") throw new InvalidOperationException("CONTAINER_RECYCLE_PURGE_RESULT_UNRESOLVED");
        await AuthorizeAsync(actor, cancellationToken);
        await repository.AcknowledgeAsync(actor, id, command.Version, receipt, cancellationToken);
    }
    private async Task<ContainerRecyclePurgeIntent> Owned(TerminalActor actor, string containerId, Guid id, CancellationToken cancellationToken)
    {
        ContainerFileValidation.Id(containerId); if (id == Guid.Empty) throw new ArgumentException("CONTAINER_RECYCLE_PURGE_ID_REQUIRED");
        var row = await repository.OwnedAsync(actor, id, cancellationToken) ?? throw new KeyNotFoundException("CONTAINER_RECYCLE_PURGE_NOT_FOUND");
        if (row.ContainerId != containerId) throw new KeyNotFoundException("CONTAINER_RECYCLE_PURGE_NOT_FOUND"); Validate(row); return row;
    }
    private void Validate(ContainerRecyclePurgeIntent row)
    { if (!ContainerRecyclePurgeWorkflowValidation.HasValidBinding(row, protector)) throw new InvalidOperationException("CONTAINER_RECYCLE_PURGE_BINDING_CHANGED"); }
    private ContainerRecyclePurgeIntentDto Map(ContainerRecyclePurgeIntent row)
    {
        Validate(row); var plan = row.PlanJson is null ? null : ContainerRecyclePurgeWorkflowValidation.ReadPlan(row);
        var ready = row.State == "Ready" && row.CancelRequestedAtUtc is null && row.ReadyExpiresAtUtc > clock.GetUtcNow();
        var confirmation = ready ? new ContainerRecyclePurgeConfirmationDto(protector.SignRecyclePurgeConfirmation(new(row.Id, row.ActorSubject, row.LoginBinding, row.PlanHash!, row.ReadyExpiresAtUtc!.Value)),
            row.ReadyExpiresAtUtc.Value, "预检不会删除原件。确认后重新检查原来源、留存inode和HDD备份，仅清理这份留存原件并保留备份；原件删除不可撤销，当前尚未提供清理后的归档恢复入口。断线后查询同一任务，不自动重做。Unknown保留保护，明确处置不代表清理成功。") : null;
        var source = ContainerRecyclePurgeWorkflowValidation.ReadCommand(row).Source;
        return new(row.Id, row.Id, row.ExecutionJobId, row.SourceIntentId, source.OperationId, ContainerRecyclePurgeWorkflowValidation.ReadCommand(row).HistoryDigest, source.Plan.Command.Location, row.State == "Ready" && !ready ? "Expired" : row.State,
            plan?.RetainedIdentity.OwnerUid, plan?.RetainedIdentity.OwnerGid, plan?.RetainedIdentity.Mode, plan?.OwnerUid, plan?.OwnerGid,
            source.Plan.SourceIdentity.SizeBytes, source.Plan.Sha256, source.Plan.Attributes.Count, row.ReadyExpiresAtUtc, row.ResultCode, row.Version,
            row.ReservationJson is not null, row.PurgeApplied, ContainerRecyclePurgeWorkflowValidation.ReadReceipt(row), confirmation);
    }
    public async Task RequireCurrentSourceAsync(ContainerRecyclePurgeIntent row, TerminalActor actor, CancellationToken cancellationToken)
    {
        var current = await sources.ResolveAsync(actor, row.SourceIntentId, cancellationToken);
        if (current.ProofHash != row.SourceProofHash || !ContainerRecyclePurgeWorkflowValidation.SameCommand(current.Command, ContainerRecyclePurgeWorkflowValidation.ReadCommand(row)))
            throw new InvalidOperationException("CONTAINER_RECYCLE_PURGE_SOURCE_CHANGED");
    }
}
