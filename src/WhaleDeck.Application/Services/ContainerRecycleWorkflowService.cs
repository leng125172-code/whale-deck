using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

/// <summary>HTTP-sized transitions only. Content attestation and reversible file removal belong to the dispatcher.</summary>
public sealed class ContainerRecycleWorkflowService(ContainerFileUploadService authorization, IContainerRecycleRepository repository,
    IContainerRecycleProtector protector, IContainerRecycleGateway gateway, TimeProvider clock, IPrivilegedIdentityDirectory liveIdentity)
{
    public async Task AuthorizeAsync(TerminalActor actor, CancellationToken cancellationToken)
    {
        FileManagementService.ValidateActor(actor);
        var current = await liveIdentity.ResolveForPrivilegedActionAsync(actor.Subject, actor.Name, cancellationToken);
        if (current.Subject != actor.Subject || !current.IsAdministrator || current.PermissionExpiresAtUtc <= clock.GetUtcNow())
            throw new UnauthorizedAccessException("CONTAINER_RECYCLE_CURRENT_ADMINISTRATOR_REQUIRED");
        await authorization.AuthorizeAsync(actor, cancellationToken);
    }
    public async Task<ContainerRecycleIntentDto> QueueAsync(TerminalActor actor, ContainerRecycleCommandDto command, string key, CancellationToken cancellationToken)
    {
        command = ContainerRecycleWorkflowValidation.SnapshotCommand(command);
        await AuthorizeAsync(actor, cancellationToken);
        var idempotency = ContainerRecycleWorkflowValidation.Key(key);
        var id = ContainerRecycleWorkflowValidation.IntentId(actor.Subject, actor.LoginBinding, idempotency);
        var prior = await repository.OwnedAsync(actor, id, cancellationToken);
        if (prior is not null)
        {
            Validate(prior);
            if (ContainerRecycleWorkflowValidation.ReadCommand(prior) != command) throw new InvalidOperationException("CONTAINER_RECYCLE_IDEMPOTENCY_CONFLICT");
            await AuthorizeAsync(actor, cancellationToken);
            return Map(prior);
        }
        await gateway.RequireContainerRecycleAsync(cancellationToken); await AuthorizeAsync(actor, cancellationToken);
        var now = ContainerRecycleWorkflowValidation.Time(clock.GetUtcNow());
        var binding = new ContainerRecycleRequestBinding(id, idempotency, actor.Subject, actor.LoginBinding, actor.TraceId,
            ContainerRecycleWorkflowValidation.Owner(actor.Subject, actor.LoginBinding), command, now, now.AddMinutes(15));
        var row = new ContainerRecycleIntent { Id = id, IdempotencyKey = idempotency, ActorSubject = actor.Subject, LoginBinding = actor.LoginBinding,
            TraceId = actor.TraceId, OwnerHash = binding.Owner, ContainerId = command.Location.ContainerId, InstanceId = command.Location.InstanceId,
            RelativePath = command.Location.Path, CommandJson = ContainerRecycleWorkflowValidation.Serialize(command),
            RequestHash = protector.FingerprintRecycleRequest(binding), CreatedAtUtc = now, PlanningExpiresAtUtc = binding.PlanningExpiresAtUtc, NextAttemptAtUtc = now };
        var queued = await repository.EnqueueAsync(row, cancellationToken);
        await AuthorizeAsync(actor, cancellationToken); return Map(queued);
    }
    public async Task<ContainerRecycleIntentDto> GetAsync(TerminalActor actor, string containerId, Guid id, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken); var row = await Owned(actor, containerId, id, cancellationToken);
        await AuthorizeAsync(actor, cancellationToken); return Map(row);
    }
    public Task<ContainerRecycleIntentDto> GetByKeyAsync(TerminalActor actor, string containerId, string key, CancellationToken cancellationToken) =>
        GetAsync(actor, containerId, ContainerRecycleWorkflowValidation.IntentId(actor.Subject, actor.LoginBinding, ContainerRecycleWorkflowValidation.Key(key)), cancellationToken);
    public async Task<ContainerRecycleIntentDto> ConfirmAsync(TerminalActor actor, string containerId, Guid id, ConfirmContainerRecycleCommand command, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        if (command is null || !command.Confirmed || command.Version < 0) throw new ArgumentException("CONTAINER_RECYCLE_CONFIRMATION_REQUIRED");
        var row = await Owned(actor, containerId, id, cancellationToken);
        var claims = protector.VerifyRecycleConfirmation(command.PlanHash); ContainerRecycleWorkflowValidation.Confirmation(claims);
        if (claims.Id != id || claims.Actor != actor.Subject || claims.LoginBinding != actor.LoginBinding) throw new UnauthorizedAccessException("CONTAINER_RECYCLE_PLAN_OWNER_REQUIRED");
        if (row.PlanHash != claims.PlanHash || row.ReadyExpiresAtUtc != claims.ExpiresAtUtc) throw new InvalidOperationException("CONTAINER_RECYCLE_PLAN_CHANGED");
        await AuthorizeAsync(actor, cancellationToken);
        var confirmed = await repository.ConfirmAsync(actor, claims, command.Version, cancellationToken);
        await AuthorizeAsync(actor, cancellationToken); return Map(confirmed);
    }
    public async Task<ContainerRecycleIntentDto> CancelAsync(TerminalActor actor, string containerId, Guid id, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken); _ = await Owned(actor, containerId, id, cancellationToken);
        await AuthorizeAsync(actor, cancellationToken); await repository.CancelAsync(actor, id, cancellationToken);
        var canceled = await Owned(actor, containerId, id, cancellationToken);
        await AuthorizeAsync(actor, cancellationToken); return Map(canceled);
    }
    public async Task<IReadOnlyList<ContainerRecycleIntentDto>> UnresolvedAsync(TerminalActor actor, int take, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        if (take is < 1 or > 50) throw new ArgumentException("CONTAINER_RECYCLE_PAGE_INVALID");
        var rows = await repository.UnresolvedAsync(take, cancellationToken);
        await AuthorizeAsync(actor, cancellationToken); return rows.Select(Map).ToArray();
    }
    public async Task<ContainerRecycleIntentDto> RecoveryGetAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        if (id == Guid.Empty) throw new ArgumentException("CONTAINER_RECYCLE_ID_REQUIRED");
        var row = await repository.FindAsync(id, cancellationToken) ?? throw new KeyNotFoundException("CONTAINER_RECYCLE_NOT_FOUND");
        await AuthorizeAsync(actor, cancellationToken); return Map(row);
    }
    public async Task AcknowledgeAsync(TerminalActor actor, Guid id, AcknowledgeContainerRecycleCommand command, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        if (id == Guid.Empty || command is null || !command.Confirmed || command.Version < 0) throw new ArgumentException("CONTAINER_RECYCLE_CONFIRMATION_REQUIRED");
        var row = await repository.FindAsync(id, cancellationToken) ?? throw new KeyNotFoundException("CONTAINER_RECYCLE_NOT_FOUND"); Validate(row);
        if (row.State != "Unknown" || row.ExecutionJobId is null || row.CompletedAtUtc is not null || row.Version != command.Version)
            throw new InvalidOperationException("CONTAINER_RECYCLE_ACKNOWLEDGEMENT_CONFLICT");
        var operation = ContainerRecycleWorkflowValidation.Reference(row);
        // Discard is the permanent Root fence for the ORIGINAL execution ID.
        // Unknown may retain removed files and HDD originals. Acknowledgement never restores, purges or asserts successful recycling.
        await AuthorizeAsync(actor, cancellationToken);
        var receipt = await gateway.DiscardContainerRecycleAsync(operation, cancellationToken); ContainerRecycleWorkflowValidation.RequireCompatibleReceipt(row, receipt);
        await AuthorizeAsync(actor, cancellationToken);
        await repository.AcknowledgeAsync(actor, id, command.Version, receipt, cancellationToken);
    }
    private async Task<ContainerRecycleIntent> Owned(TerminalActor actor, string containerId, Guid id, CancellationToken cancellationToken)
    {
        ContainerFileValidation.Id(containerId); if (id == Guid.Empty) throw new ArgumentException("CONTAINER_RECYCLE_ID_REQUIRED");
        var row = await repository.OwnedAsync(actor, id, cancellationToken) ?? throw new KeyNotFoundException("CONTAINER_RECYCLE_NOT_FOUND");
        if (row.ContainerId != containerId) throw new KeyNotFoundException("CONTAINER_RECYCLE_NOT_FOUND"); Validate(row); return row;
    }
    private void Validate(ContainerRecycleIntent row)
    { if (!ContainerRecycleWorkflowValidation.HasValidBinding(row, protector)) throw new InvalidOperationException("CONTAINER_RECYCLE_BINDING_CHANGED"); }
    private ContainerRecycleIntentDto Map(ContainerRecycleIntent row)
    {
        Validate(row); var plan = row.PlanJson is null ? null : ContainerRecycleWorkflowValidation.ReadPlan(row);
        var ready = row.State == "Ready" && row.CancelRequestedAtUtc is null && row.ReadyExpiresAtUtc > clock.GetUtcNow();
        var confirmation = ready ? new ContainerRecycleConfirmationDto(protector.SignRecycleConfirmation(new(row.Id, row.ActorSubject, row.LoginBinding, row.PlanHash!, row.ReadyExpiresAtUtc!.Value)),
            row.ReadyExpiresAtUtc.Value, "预检不移除文件。确认后再次校验原容器实例、文件版本和属性，保存HDD原件并将原inode移至私有回收区；不彻底删除、不覆盖目标、不处理目录。断线后查询原任务，Unknown保留保护，解除保护不代表回收或恢复成功。恢复须通过独立确认流程。") : null;
        return new(row.Id, row.Id, row.ExecutionJobId, ContainerRecycleWorkflowValidation.ReadCommand(row), row.State == "Ready" && !ready ? "Expired" : row.State,
            plan?.SourceIdentity.OwnerUid, plan?.SourceIdentity.OwnerGid, plan?.SourceIdentity.Mode, plan?.OwnerUid, plan?.OwnerGid,
            plan?.SourceIdentity.SizeBytes, plan?.Sha256, plan?.Attributes.Count, row.ReadyExpiresAtUtc, row.ResultCode, row.Version,
            row.HistoryMayExist, row.HistoryVerified, row.HistoryDigest, ContainerRecycleWorkflowValidation.ReadReceipt(row), confirmation);
    }
}
