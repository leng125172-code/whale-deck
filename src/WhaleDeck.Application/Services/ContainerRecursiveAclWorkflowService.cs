using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

/// <summary>HTTP-sized transitions only. Metadata attestation and recursive ACL changes belong to the dispatcher.</summary>
public sealed class ContainerRecursiveAclWorkflowService(ContainerFileUploadService authorization, IContainerRecursiveAclRepository repository,
    IContainerRecursiveAclProtector protector, IContainerRecursiveAclGateway gateway, TimeProvider clock, IPrivilegedIdentityDirectory liveIdentity)
{
    public async Task AuthorizeAsync(TerminalActor actor, CancellationToken cancellationToken)
    {
        FileManagementService.ValidateActor(actor);
        var current = await liveIdentity.ResolveForPrivilegedActionAsync(actor.Subject, actor.Name, cancellationToken);
        if (current.Subject != actor.Subject || !current.IsAdministrator || current.PermissionExpiresAtUtc <= clock.GetUtcNow())
            throw new UnauthorizedAccessException("CONTAINER_RECURSIVE_ACL_CURRENT_ADMINISTRATOR_REQUIRED");
        await authorization.AuthorizeAsync(actor, cancellationToken);
    }
    public async Task<ContainerRecursiveAclIntentDto> QueueAsync(TerminalActor actor, ContainerRecursiveAclCommandDto command, string key, CancellationToken cancellationToken)
    {
        command = ContainerRecursiveAclWorkflowValidation.SnapshotCommand(command);
        await AuthorizeAsync(actor, cancellationToken);
        var idempotency = ContainerRecursiveAclWorkflowValidation.Key(key);
        var id = ContainerRecursiveAclWorkflowValidation.IntentId(actor.Subject, actor.LoginBinding, idempotency);
        var prior = await repository.OwnedAsync(actor, id, cancellationToken);
        if (prior is not null)
        {
            Validate(prior);
            if (!ContainerRecursiveAclValidation.SameCommand(ContainerRecursiveAclWorkflowValidation.ReadCommand(prior), command)) throw new InvalidOperationException("CONTAINER_RECURSIVE_ACL_IDEMPOTENCY_CONFLICT");
            await AuthorizeAsync(actor, cancellationToken);
            return Map(prior);
        }
        await gateway.RequireContainerRecursiveAclWorkflowAsync(cancellationToken); await AuthorizeAsync(actor, cancellationToken);
        var now = ContainerRecursiveAclWorkflowValidation.Time(clock.GetUtcNow());
        var binding = new ContainerRecursiveAclRequestBinding(id, idempotency, actor.Subject, actor.LoginBinding, actor.TraceId,
            ContainerRecursiveAclWorkflowValidation.Owner(actor.Subject, actor.LoginBinding), command, now, now.AddMinutes(15));
        var row = new ContainerRecursiveAclIntent { Id = id, IdempotencyKey = idempotency, ActorSubject = actor.Subject, LoginBinding = actor.LoginBinding,
            TraceId = actor.TraceId, OwnerHash = binding.Owner, ContainerId = command.Location.ContainerId, InstanceId = command.Location.InstanceId,
            RelativePath = command.Location.Path, CommandJson = ContainerRecursiveAclWorkflowValidation.Serialize(command),
            RequestHash = protector.FingerprintRecursiveAclRequest(binding), CreatedAtUtc = now, PlanningExpiresAtUtc = binding.PlanningExpiresAtUtc, NextAttemptAtUtc = now };
        var queued = await repository.EnqueueAsync(row, cancellationToken);
        await AuthorizeAsync(actor, cancellationToken); return Map(queued);
    }
    public async Task<ContainerRecursiveAclIntentDto> GetAsync(TerminalActor actor, string containerId, Guid id, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken); var row = await Owned(actor, containerId, id, cancellationToken);
        await AuthorizeAsync(actor, cancellationToken); return Map(row);
    }
    public Task<ContainerRecursiveAclIntentDto> GetByKeyAsync(TerminalActor actor, string containerId, string key, CancellationToken cancellationToken) =>
        GetAsync(actor, containerId, ContainerRecursiveAclWorkflowValidation.IntentId(actor.Subject, actor.LoginBinding, ContainerRecursiveAclWorkflowValidation.Key(key)), cancellationToken);
    public async Task<ContainerRecursiveAclIntentDto> ConfirmAsync(TerminalActor actor, string containerId, Guid id, ConfirmContainerRecursiveAclCommand command, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        if (command is null || !command.Confirmed || command.Version < 0) throw new ArgumentException("CONTAINER_RECURSIVE_ACL_CONFIRMATION_REQUIRED");
        var row = await Owned(actor, containerId, id, cancellationToken);
        var claims = protector.VerifyRecursiveAclConfirmation(command.PlanHash); ContainerRecursiveAclWorkflowValidation.Confirmation(claims);
        if (claims.Id != id || claims.Actor != actor.Subject || claims.LoginBinding != actor.LoginBinding) throw new UnauthorizedAccessException("CONTAINER_RECURSIVE_ACL_PLAN_OWNER_REQUIRED");
        if (row.PlanHash != claims.PlanHash || row.ReadyExpiresAtUtc != claims.ExpiresAtUtc) throw new InvalidOperationException("CONTAINER_RECURSIVE_ACL_PLAN_CHANGED");
        await AuthorizeAsync(actor, cancellationToken);
        var confirmed = await repository.ConfirmAsync(actor, claims, command.Version, cancellationToken);
        await AuthorizeAsync(actor, cancellationToken); return Map(confirmed);
    }
    public async Task<ContainerRecursiveAclIntentDto> CancelAsync(TerminalActor actor, string containerId, Guid id, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken); _ = await Owned(actor, containerId, id, cancellationToken);
        await AuthorizeAsync(actor, cancellationToken); await repository.CancelAsync(actor, id, cancellationToken);
        var canceled = await Owned(actor, containerId, id, cancellationToken);
        await AuthorizeAsync(actor, cancellationToken); return Map(canceled);
    }
    public async Task<IReadOnlyList<ContainerRecursiveAclIntentDto>> UnresolvedAsync(TerminalActor actor, int take, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        if (take is < 1 or > 50) throw new ArgumentException("CONTAINER_RECURSIVE_ACL_PAGE_INVALID");
        var rows = await repository.UnresolvedAsync(take, cancellationToken);
        await AuthorizeAsync(actor, cancellationToken); return rows.Select(Map).ToArray();
    }
    public async Task<ContainerRecursiveAclIntentDto> RecoveryGetAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        if (id == Guid.Empty) throw new ArgumentException("CONTAINER_RECURSIVE_ACL_ID_REQUIRED");
        var row = await repository.FindAsync(id, cancellationToken) ?? throw new KeyNotFoundException("CONTAINER_RECURSIVE_ACL_NOT_FOUND");
        await AuthorizeAsync(actor, cancellationToken); return Map(row);
    }
    public async Task AcknowledgeAsync(TerminalActor actor, Guid id, AcknowledgeContainerRecursiveAclCommand command, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        if (id == Guid.Empty || command is null || !command.Confirmed || command.Version < 0) throw new ArgumentException("CONTAINER_RECURSIVE_ACL_CONFIRMATION_REQUIRED");
        var row = await repository.FindAsync(id, cancellationToken) ?? throw new KeyNotFoundException("CONTAINER_RECURSIVE_ACL_NOT_FOUND"); Validate(row);
        if (row.State is not ("Unknown" or "Partial") || row.ExecutionJobId is null || row.CompletedAtUtc is not null || row.Version != command.Version)
            throw new InvalidOperationException("CONTAINER_RECURSIVE_ACL_ACKNOWLEDGEMENT_CONFLICT");
        var operation = ContainerRecursiveAclWorkflowValidation.Reference(row);
        // Discard is the permanent Root fence for the ORIGINAL execution ID.
        // Partial/Unknown retain actual changes and HDD proof; acknowledgement never restores ACLs or asserts whole-tree success.
        var receipt = await gateway.DiscardContainerRecursiveAclAsync(operation, cancellationToken); ContainerRecursiveAclWorkflowValidation.RequireCompatibleReceipt(row, receipt);
        await AuthorizeAsync(actor, cancellationToken);
        await repository.AcknowledgeAsync(actor, id, command.Version, receipt, cancellationToken);
    }
    private async Task<ContainerRecursiveAclIntent> Owned(TerminalActor actor, string containerId, Guid id, CancellationToken cancellationToken)
    {
        ContainerFileValidation.Id(containerId); if (id == Guid.Empty) throw new ArgumentException("CONTAINER_RECURSIVE_ACL_ID_REQUIRED");
        var row = await repository.OwnedAsync(actor, id, cancellationToken) ?? throw new KeyNotFoundException("CONTAINER_RECURSIVE_ACL_NOT_FOUND");
        if (row.ContainerId != containerId) throw new KeyNotFoundException("CONTAINER_RECURSIVE_ACL_NOT_FOUND"); Validate(row); return row;
    }
    private void Validate(ContainerRecursiveAclIntent row)
    { if (!ContainerRecursiveAclWorkflowValidation.HasValidBinding(row, protector)) throw new InvalidOperationException("CONTAINER_RECURSIVE_ACL_BINDING_CHANGED"); }
    private ContainerRecursiveAclIntentDto Map(ContainerRecursiveAclIntent row)
    {
        Validate(row); var plan = row.PlanJson is null ? null : ContainerRecursiveAclWorkflowValidation.ReadPlan(row);
        var ready = row.State == "Ready" && row.CancelRequestedAtUtc is null && row.ReadyExpiresAtUtc > clock.GetUtcNow();
        var confirmation = ready ? new ContainerRecursiveAclConfirmationDto(protector.SignRecursiveAclConfirmation(new(row.Id, row.ActorSubject, row.LoginBinding, row.PlanHash!, row.ReadyExpiresAtUtc!.Value)),
            row.ReadyExpiresAtUtc.Value, "预检不修改ACL。确认后按原实例、目录树清单和逐项版本校验，先保留HDD原ACL元数据；access分别设置文件与目录ACL，权限模式随ACL mask变化，default仅设置目录继承ACL，显式空列表表示清除。数值UID/GID是ACL条目，不更改inode所有者；目录最后处理。取消后可能部分修改；Partial/Unknown保留保护，查询原任务，不自动续做或回滚；解除保护须当前管理员明确确认。") : null;
        return new(row.Id, row.Id, row.ExecutionJobId, ContainerRecursiveAclWorkflowValidation.ReadCommand(row), row.State == "Ready" && !ready ? "Expired" : row.State,
            plan?.RootIdentity.OwnerUid, plan?.RootIdentity.OwnerGid, plan?.RootIdentity.Mode, plan?.OwnerUid, plan?.OwnerGid, plan?.EntryCount, plan?.FileCount, plan?.DirectoryCount, plan?.ChangeCount,
            plan?.ManifestDigest, row.ReadyExpiresAtUtc, row.ResultCode, row.Version, row.HistoryMayExist, row.HistoryVerified, row.HistoryDigest,
            ContainerRecursiveAclWorkflowValidation.ReadReceipt(row), confirmation);
    }
}
