using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

/// <summary>HTTP-sized transitions only. Metadata attestation and ACL changes belong to the dispatcher.</summary>
public sealed partial class ContainerAclWorkflowService(ContainerFileUploadService authorization, IContainerAclRepository repository,
    IContainerAclProtector protector, IContainerAclGateway gateway, TimeProvider clock, IPrivilegedIdentityDirectory liveIdentity, IContainerAclRestoreSourceGateway? restoreSource = null)
{
    public async Task AuthorizeAsync(TerminalActor actor, CancellationToken cancellationToken)
    {
        FileManagementService.ValidateActor(actor);
        var current = await liveIdentity.ResolveForPrivilegedActionAsync(actor.Subject, actor.Name, cancellationToken);
        if (current.Subject != actor.Subject || !current.IsAdministrator || current.PermissionExpiresAtUtc <= clock.GetUtcNow())
            throw new UnauthorizedAccessException("CONTAINER_ACL_CURRENT_ADMINISTRATOR_REQUIRED");
        await authorization.AuthorizeAsync(actor, cancellationToken);
    }
    public async Task<ContainerAclIntentDto> QueueAsync(TerminalActor actor, ContainerAclCommandDto command, string key, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken); ContainerAclValidation.Command(command); command = ContainerAclWorkflowValidation.SnapshotCommand(command);
        var idempotency = ContainerAclWorkflowValidation.Key(key);
        var id = ContainerAclWorkflowValidation.IntentId(actor.Subject, actor.LoginBinding, idempotency);
        var prior = await repository.OwnedAsync(actor, id, cancellationToken);
        if (prior is not null)
        {
            Validate(prior);
            if (prior.RestoreSourceJson is not null || !ContainerAclValidation.SameCommand(ContainerAclWorkflowValidation.ReadCommand(prior), command)) throw new InvalidOperationException("CONTAINER_ACL_IDEMPOTENCY_CONFLICT");
            await AuthorizeAsync(actor, cancellationToken); return Map(prior);
        }
        await gateway.RequireContainerAclWorkflowAsync(cancellationToken); await AuthorizeAsync(actor, cancellationToken);
        var now = ContainerAclWorkflowValidation.Time(clock.GetUtcNow());
        var binding = new ContainerAclRequestBinding(id, idempotency, actor.Subject, actor.LoginBinding, actor.TraceId,
            ContainerAclWorkflowValidation.Owner(actor.Subject, actor.LoginBinding), command, now, now.AddMinutes(15));
        var row = new ContainerAclIntent { Id = id, IdempotencyKey = idempotency, ActorSubject = actor.Subject, LoginBinding = actor.LoginBinding,
            TraceId = actor.TraceId, OwnerHash = binding.Owner, ContainerId = command.Location.ContainerId, InstanceId = command.Location.InstanceId,
            RelativePath = command.Location.Path, CommandJson = ContainerAclWorkflowValidation.Serialize(command),
            RequestHash = protector.FingerprintAclRequest(binding), CreatedAtUtc = now, PlanningExpiresAtUtc = binding.PlanningExpiresAtUtc, NextAttemptAtUtc = now };
        var queued = await repository.EnqueueAsync(row, cancellationToken);
        await AuthorizeAsync(actor, cancellationToken); return Map(queued);
    }
    public async Task<ContainerAclIntentDto> GetAsync(TerminalActor actor, string containerId, Guid id, CancellationToken cancellationToken)
    { await AuthorizeAsync(actor, cancellationToken); var row = await Owned(actor, containerId, id, cancellationToken);
        await AuthorizeAsync(actor, cancellationToken); return Map(row); }
    public Task<ContainerAclIntentDto> GetByKeyAsync(TerminalActor actor, string containerId, string key, CancellationToken cancellationToken) =>
        GetAsync(actor, containerId, ContainerAclWorkflowValidation.IntentId(actor.Subject, actor.LoginBinding, ContainerAclWorkflowValidation.Key(key)), cancellationToken);
    public async Task<ContainerAclIntentDto> ConfirmAsync(TerminalActor actor, string containerId, Guid id, ConfirmContainerAclCommand command, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        if (command is null || !command.Confirmed || command.Version < 0) throw new ArgumentException("CONTAINER_ACL_CONFIRMATION_REQUIRED");
        var row = await Owned(actor, containerId, id, cancellationToken);
        var claims = protector.VerifyAclConfirmation(command.PlanHash); ContainerAclWorkflowValidation.Confirmation(claims);
        if (claims.Id != id || claims.Actor != actor.Subject || claims.LoginBinding != actor.LoginBinding) throw new UnauthorizedAccessException("CONTAINER_ACL_PLAN_OWNER_REQUIRED");
        if (row.PlanHash != claims.PlanHash || row.ReadyExpiresAtUtc != claims.ExpiresAtUtc) throw new InvalidOperationException("CONTAINER_ACL_PLAN_CHANGED");
        if (row.RestoreSourceJson is not null && row.ExecutionJobId is null) await ValidateRestoreSourceAsync(actor, row, cancellationToken);
        await AuthorizeAsync(actor, cancellationToken);
        var confirmed = await repository.ConfirmAsync(actor, claims, command.Version, cancellationToken);
        await AuthorizeAsync(actor, cancellationToken); return Map(confirmed);
    }
    public async Task<ContainerAclIntentDto> CancelAsync(TerminalActor actor, string containerId, Guid id, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken); _ = await Owned(actor, containerId, id, cancellationToken);
        await AuthorizeAsync(actor, cancellationToken); await repository.CancelAsync(actor, id, cancellationToken);
        var canceled = await Owned(actor, containerId, id, cancellationToken);
        await AuthorizeAsync(actor, cancellationToken); return Map(canceled);
    }
    public async Task<IReadOnlyList<ContainerAclIntentDto>> UnknownAsync(TerminalActor actor, int take, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        if (take is < 1 or > 50) throw new ArgumentException("CONTAINER_ACL_PAGE_INVALID");
        var rows = await repository.UnknownAsync(take, cancellationToken);
        await AuthorizeAsync(actor, cancellationToken); return rows.Select(Map).ToArray();
    }
    public async Task<ContainerAclIntentDto> RecoveryGetAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        if (id == Guid.Empty) throw new ArgumentException("CONTAINER_ACL_ID_REQUIRED");
        var row = await repository.FindAsync(id, cancellationToken) ?? throw new KeyNotFoundException("CONTAINER_ACL_NOT_FOUND");
        await AuthorizeAsync(actor, cancellationToken); return Map(row);
    }
    public async Task AcknowledgeAsync(TerminalActor actor, Guid id, AcknowledgeContainerAclCommand command, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        if (id == Guid.Empty || command is null || !command.Confirmed || command.Version < 0) throw new ArgumentException("CONTAINER_ACL_CONFIRMATION_REQUIRED");
        var row = await repository.FindAsync(id, cancellationToken) ?? throw new KeyNotFoundException("CONTAINER_ACL_NOT_FOUND"); Validate(row);
        if (row.State != "Unknown" || row.ExecutionJobId is null || row.CompletedAtUtc is not null || row.Version != command.Version)
            throw new InvalidOperationException("CONTAINER_ACL_ACKNOWLEDGEMENT_CONFLICT");
        var operation = ContainerAclWorkflowValidation.Reference(row);
        // Discard is the permanent Root fence for the ORIGINAL execution ID.
        // Unknown may retain HDD metadata or a changed ACL; acknowledgement never restores ACL or asserts success.
        var receipt = await gateway.DiscardContainerAclAsync(operation, cancellationToken); ContainerAclWorkflowValidation.RequireCompatibleReceipt(row, receipt);
        await AuthorizeAsync(actor, cancellationToken);
        await repository.AcknowledgeAsync(actor, id, command.Version, receipt, cancellationToken);
        await AuthorizeAsync(actor, cancellationToken);
    }
    private async Task<ContainerAclIntent> Owned(TerminalActor actor, string containerId, Guid id, CancellationToken cancellationToken)
    {
        ContainerFileValidation.Id(containerId); if (id == Guid.Empty) throw new ArgumentException("CONTAINER_ACL_ID_REQUIRED");
        var row = await repository.OwnedAsync(actor, id, cancellationToken) ?? throw new KeyNotFoundException("CONTAINER_ACL_NOT_FOUND");
        if (row.ContainerId != containerId) throw new KeyNotFoundException("CONTAINER_ACL_NOT_FOUND"); Validate(row); return row;
    }
    private void Validate(ContainerAclIntent row)
    { if (!ContainerAclWorkflowValidation.HasValidBinding(row, protector)) throw new InvalidOperationException("CONTAINER_ACL_BINDING_CHANGED"); }
    private ContainerAclIntentDto Map(ContainerAclIntent row)
    {
        Validate(row); var plan = row.PlanJson is null ? null : ContainerAclWorkflowValidation.ReadPlan(row);
        var ready = row.State == "Ready" && row.CancelRequestedAtUtc is null && row.ReadyExpiresAtUtc > clock.GetUtcNow();
        var confirmation = ready ? new ContainerAclConfirmationDto(protector.SignAclConfirmation(new(row.Id, row.ActorSubject, row.LoginBinding, row.PlanHash!, row.ReadyExpiresAtUtc!.Value)),
            row.ReadyExpiresAtUtc.Value, (row.RestoreSourceJson is null ? "" : "这是历史ACL恢复：" + ContainerAclRestoreValidation.Read(row)!.Entry.CaptureId + "，将恢复该原件对应的单一ACL类型，不恢复另一类当前ACL。") + "预检不修改ACL。确认后校验原实例、inode版本及属性，先保留HDD旧ACL元数据，再仅修改当前对象的access或default ACL；default空条目明确表示清空，仅限目录。named ID=0有效且必须显式提供mask，access的mask会改变普通权限的组位。保留另一类ACL、UID/GID及目录子项，不递归。断线后查询原任务；Unknown保留保护，不自动重做或恢复ACL。") : null;
        return new(row.Id, row.Id, row.ExecutionJobId, ContainerAclWorkflowValidation.ReadCommand(row), row.State == "Ready" && !ready ? "Expired" : row.State,
            plan?.Kind, plan?.SourceIdentity.OwnerUid, plan?.SourceIdentity.OwnerGid, plan?.OwnerUid, plan?.OwnerGid, plan?.AccessAcl, plan?.DefaultAcl, plan?.TargetMode, plan?.NoChange, row.ReadyExpiresAtUtc, row.ResultCode, row.Version,
            row.PublishedVersion, row.AclConfirmed, row.HistoryMayExist, row.HistoryVerified, row.HistoryDigest, ContainerAclWorkflowValidation.ReadReceipt(row), confirmation);
    }
}
