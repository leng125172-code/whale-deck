using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

/// <summary>HTTP-sized transitions only. Retained-inode restoration belongs to the dispatcher.</summary>
public sealed class ContainerRecycleRestoreWorkflowService(ContainerFileUploadService authorization, IContainerRecycleRestoreRepository repository,
    IContainerRecycleRestoreProtector protector, IContainerRecycleRestoreGateway gateway, TimeProvider clock, IPrivilegedIdentityDirectory liveIdentity, ContainerRecycleRestoreSourceResolver sources)
{
    public async Task AuthorizeAsync(TerminalActor actor, CancellationToken cancellationToken)
    {
        FileManagementService.ValidateActor(actor);
        var current = await liveIdentity.ResolveForPrivilegedActionAsync(actor.Subject, actor.Name, cancellationToken);
        if (current.Subject != actor.Subject || !current.IsAdministrator || current.PermissionExpiresAtUtc <= clock.GetUtcNow())
            throw new UnauthorizedAccessException("CONTAINER_RECYCLE_RESTORE_CURRENT_ADMINISTRATOR_REQUIRED");
        await authorization.AuthorizeAsync(actor, cancellationToken);
    }
    public async Task<ContainerRecycleRestoreIntentDto> QueueAsync(TerminalActor actor, string containerId, Guid sourceIntentId, string key, CancellationToken cancellationToken)
    {
        ContainerFileValidation.Id(containerId); if (sourceIntentId == Guid.Empty) throw new ArgumentException("CONTAINER_RECYCLE_RESTORE_SOURCE_REQUIRED");
        await AuthorizeAsync(actor, cancellationToken);
        var idempotency = ContainerRecycleRestoreWorkflowValidation.Key(key);
        var id = ContainerRecycleRestoreWorkflowValidation.IntentId(actor.Subject, actor.LoginBinding, idempotency);
        var prior = await repository.OwnedAsync(actor, id, cancellationToken);
        if (prior is not null)
        {
            Validate(prior);
            if (prior.SourceIntentId != sourceIntentId || prior.ContainerId != containerId) throw new InvalidOperationException("CONTAINER_RECYCLE_RESTORE_IDEMPOTENCY_CONFLICT");
            await AuthorizeAsync(actor, cancellationToken);
            return Map(prior);
        }
        var source = await sources.ResolveAsync(actor, sourceIntentId, cancellationToken);
        var command = ContainerRecycleRestoreWorkflowValidation.SnapshotCommand(source.Command);
        if (command.Source.Plan.Command.Location.ContainerId != containerId) throw new KeyNotFoundException("CONTAINER_RECYCLE_RESTORE_SOURCE_NOT_FOUND");
        await gateway.RequireContainerRecycleRestoreAsync(cancellationToken); await AuthorizeAsync(actor, cancellationToken);
        var now = ContainerRecycleRestoreWorkflowValidation.Time(clock.GetUtcNow());
        var binding = new ContainerRecycleRestoreRequestBinding(id, idempotency, actor.Subject, actor.LoginBinding, actor.TraceId,
            ContainerRecycleRestoreWorkflowValidation.Owner(actor.Subject, actor.LoginBinding), source.IntentId, source.ProofHash, command, now, now.AddMinutes(15));
        ContainerRecycleRestoreWorkflowValidation.Request(binding);
        var row = new ContainerRecycleRestoreIntent { Id = id, IdempotencyKey = idempotency, ActorSubject = actor.Subject, LoginBinding = actor.LoginBinding,
            TraceId = actor.TraceId, OwnerHash = binding.Owner, SourceIntentId = source.IntentId, SourceProofHash = source.ProofHash, ContainerId = command.Source.Plan.Command.Location.ContainerId, InstanceId = command.Source.Plan.Command.Location.InstanceId,
            RelativePath = command.Source.Plan.Command.Location.Path, CommandJson = ContainerRecycleRestoreWorkflowValidation.Serialize(command),
            RequestHash = protector.FingerprintRecycleRestoreRequest(binding), CreatedAtUtc = now, PlanningExpiresAtUtc = binding.PlanningExpiresAtUtc, NextAttemptAtUtc = now };
        var queued = await repository.EnqueueAsync(row, cancellationToken);
        await AuthorizeAsync(actor, cancellationToken); return Map(queued);
    }
    public async Task<ContainerRecycleRestoreIntentDto> GetAsync(TerminalActor actor, string containerId, Guid id, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken); var row = await Owned(actor, containerId, id, cancellationToken);
        await AuthorizeAsync(actor, cancellationToken); return Map(row);
    }
    public Task<ContainerRecycleRestoreIntentDto> GetByKeyAsync(TerminalActor actor, string containerId, string key, CancellationToken cancellationToken) =>
        GetAsync(actor, containerId, ContainerRecycleRestoreWorkflowValidation.IntentId(actor.Subject, actor.LoginBinding, ContainerRecycleRestoreWorkflowValidation.Key(key)), cancellationToken);
    public async Task<ContainerRecycleRestoreIntentDto> ConfirmAsync(TerminalActor actor, string containerId, Guid id, ConfirmContainerRecycleRestoreCommand command, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        if (command is null || !command.Confirmed || command.Version < 0) throw new ArgumentException("CONTAINER_RECYCLE_RESTORE_CONFIRMATION_REQUIRED");
        var row = await Owned(actor, containerId, id, cancellationToken);
        var claims = protector.VerifyRecycleRestoreConfirmation(command.PlanHash); ContainerRecycleRestoreWorkflowValidation.Confirmation(claims);
        if (claims.Id != id || claims.Actor != actor.Subject || claims.LoginBinding != actor.LoginBinding) throw new UnauthorizedAccessException("CONTAINER_RECYCLE_RESTORE_PLAN_OWNER_REQUIRED");
        if (row.PlanHash != claims.PlanHash || row.ReadyExpiresAtUtc != claims.ExpiresAtUtc) throw new InvalidOperationException("CONTAINER_RECYCLE_RESTORE_PLAN_CHANGED");
        await AuthorizeAsync(actor, cancellationToken);
        await RequireCurrentSourceAsync(row, actor, cancellationToken);
        var confirmed = await repository.ConfirmAsync(actor, claims, command.Version, cancellationToken);
        await AuthorizeAsync(actor, cancellationToken); return Map(confirmed);
    }
    public async Task<ContainerRecycleRestoreIntentDto> CancelAsync(TerminalActor actor, string containerId, Guid id, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken); _ = await Owned(actor, containerId, id, cancellationToken);
        await AuthorizeAsync(actor, cancellationToken); await repository.CancelAsync(actor, id, cancellationToken);
        var canceled = await Owned(actor, containerId, id, cancellationToken);
        await AuthorizeAsync(actor, cancellationToken); return Map(canceled);
    }
    public async Task<IReadOnlyList<ContainerRecycleRestoreIntentDto>> UnresolvedAsync(TerminalActor actor, int take, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        if (take is < 1 or > 50) throw new ArgumentException("CONTAINER_RECYCLE_RESTORE_PAGE_INVALID");
        var rows = await repository.UnresolvedAsync(take, cancellationToken);
        await AuthorizeAsync(actor, cancellationToken); return rows.Select(Map).ToArray();
    }
    public async Task<ContainerRecycleRestoreIntentDto> RecoveryGetAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        if (id == Guid.Empty) throw new ArgumentException("CONTAINER_RECYCLE_RESTORE_ID_REQUIRED");
        var row = await repository.FindAsync(id, cancellationToken) ?? throw new KeyNotFoundException("CONTAINER_RECYCLE_RESTORE_NOT_FOUND");
        await AuthorizeAsync(actor, cancellationToken); return Map(row);
    }
    public async Task AcknowledgeAsync(TerminalActor actor, Guid id, AcknowledgeContainerRecycleRestoreCommand command, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        if (id == Guid.Empty || command is null || !command.Confirmed || command.Version < 0) throw new ArgumentException("CONTAINER_RECYCLE_RESTORE_CONFIRMATION_REQUIRED");
        var row = await repository.FindAsync(id, cancellationToken) ?? throw new KeyNotFoundException("CONTAINER_RECYCLE_RESTORE_NOT_FOUND"); Validate(row);
        if (row.State != "Unknown" || row.ExecutionJobId is null || row.CompletedAtUtc is not null || row.Version != command.Version)
            throw new InvalidOperationException("CONTAINER_RECYCLE_RESTORE_ACKNOWLEDGEMENT_CONFLICT");
        var operation = ContainerRecycleRestoreWorkflowValidation.Reference(row);
        // Discard is the permanent Root fence for the ORIGINAL execution ID.
        // Unknown retains the original source claim and outcome. Acknowledgement never restores, purges or asserts success.
        await AuthorizeAsync(actor, cancellationToken);
        var receipt = await gateway.DiscardContainerRecycleRestoreAsync(operation, cancellationToken); ContainerRecycleRestoreWorkflowValidation.RequireCompatibleReceipt(row, receipt);
        await AuthorizeAsync(actor, cancellationToken);
        await repository.AcknowledgeAsync(actor, id, command.Version, receipt, cancellationToken);
    }
    private async Task<ContainerRecycleRestoreIntent> Owned(TerminalActor actor, string containerId, Guid id, CancellationToken cancellationToken)
    {
        ContainerFileValidation.Id(containerId); if (id == Guid.Empty) throw new ArgumentException("CONTAINER_RECYCLE_RESTORE_ID_REQUIRED");
        var row = await repository.OwnedAsync(actor, id, cancellationToken) ?? throw new KeyNotFoundException("CONTAINER_RECYCLE_RESTORE_NOT_FOUND");
        if (row.ContainerId != containerId) throw new KeyNotFoundException("CONTAINER_RECYCLE_RESTORE_NOT_FOUND"); Validate(row); return row;
    }
    private void Validate(ContainerRecycleRestoreIntent row)
    { if (!ContainerRecycleRestoreWorkflowValidation.HasValidBinding(row, protector)) throw new InvalidOperationException("CONTAINER_RECYCLE_RESTORE_BINDING_CHANGED"); }
    private ContainerRecycleRestoreIntentDto Map(ContainerRecycleRestoreIntent row)
    {
        Validate(row); var plan = row.PlanJson is null ? null : ContainerRecycleRestoreWorkflowValidation.ReadPlan(row);
        var ready = row.State == "Ready" && row.CancelRequestedAtUtc is null && row.ReadyExpiresAtUtc > clock.GetUtcNow();
        var confirmation = ready ? new ContainerRecycleRestoreConfirmationDto(protector.SignRecycleRestoreConfirmation(new(row.Id, row.ActorSubject, row.LoginBinding, row.PlanHash!, row.ReadyExpiresAtUtc!.Value)),
            row.ReadyExpiresAtUtc.Value, "预检不恢复文件。确认后按原回收引用、HDD摘要和留存身份再次检查原容器实例与空缺目标，将原inode返回原路径；不覆盖目标、不重建新实例或目录。断线后查询同一恢复任务，不自动重做。Unknown保留保护，解除保护不代表恢复成功。") : null;
        var source = ContainerRecycleRestoreWorkflowValidation.ReadCommand(row).Source;
        return new(row.Id, row.Id, row.ExecutionJobId, row.SourceIntentId, source.OperationId, ContainerRecycleRestoreWorkflowValidation.ReadCommand(row).HistoryDigest, source.Plan.Command.Location, row.State == "Ready" && !ready ? "Expired" : row.State,
            plan?.RetainedIdentity.OwnerUid, plan?.RetainedIdentity.OwnerGid, plan?.RetainedIdentity.Mode, plan?.OwnerUid, plan?.OwnerGid,
            source.Plan.SourceIdentity.SizeBytes, source.Plan.Sha256, source.Plan.Attributes.Count, row.ReadyExpiresAtUtc, row.ResultCode, row.Version,
            row.HistoryVerified, row.HistoryDigest, ContainerRecycleRestoreWorkflowValidation.ReadReceipt(row), confirmation);
    }
    public async Task RequireCurrentSourceAsync(ContainerRecycleRestoreIntent row, TerminalActor actor, CancellationToken cancellationToken)
    {
        var current = await sources.ResolveAsync(actor, row.SourceIntentId, cancellationToken);
        if (current.ProofHash != row.SourceProofHash || !ContainerRecycleRestoreWorkflowValidation.SameCommand(current.Command, ContainerRecycleRestoreWorkflowValidation.ReadCommand(row)))
            throw new InvalidOperationException("CONTAINER_RECYCLE_RESTORE_SOURCE_CHANGED");
    }
}
