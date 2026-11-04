using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

/// <summary>HTTP-sized transitions only. Metadata attestation and heterogeneous recursive ACL restoration belong to the dispatcher.</summary>
public sealed class ContainerRecursiveAclRestoreWorkflowService(ContainerFileUploadService authorization, IContainerRecursiveAclRestoreRepository repository,
    IContainerRecursiveAclRestoreProtector protector, IContainerRecursiveAclRestoreGateway gateway, TimeProvider clock, IPrivilegedIdentityDirectory liveIdentity)
{
    public async Task AuthorizeAsync(TerminalActor actor, CancellationToken cancellationToken)
    {
        FileManagementService.ValidateActor(actor);
        var current = await liveIdentity.ResolveForPrivilegedActionAsync(actor.Subject, actor.Name, cancellationToken);
        if (current.Subject != actor.Subject || !current.IsAdministrator || current.PermissionExpiresAtUtc <= clock.GetUtcNow())
            throw new UnauthorizedAccessException("CONTAINER_RECURSIVE_ACL_RESTORE_CURRENT_ADMINISTRATOR_REQUIRED");
        await authorization.AuthorizeAsync(actor, cancellationToken);
    }
    public async Task<ContainerRecursiveAclRestoreIntentDto> QueueAsync(TerminalActor actor, ContainerRecursiveAclRestoreCommandDto command, string key, CancellationToken cancellationToken)
    {
        command = ContainerRecursiveAclRestoreWorkflowValidation.SnapshotCommand(command);
        await AuthorizeAsync(actor, cancellationToken);
        var idempotency = ContainerRecursiveAclRestoreWorkflowValidation.Key(key);
        var id = ContainerRecursiveAclRestoreWorkflowValidation.IntentId(actor.Subject, actor.LoginBinding, idempotency);
        var prior = await repository.OwnedAsync(actor, id, cancellationToken);
        if (prior is not null)
        {
            Validate(prior);
            if (!ContainerRecursiveAclRestoreValidation.SameCommand(ContainerRecursiveAclRestoreWorkflowValidation.ReadCommand(prior), command)) throw new InvalidOperationException("CONTAINER_RECURSIVE_ACL_RESTORE_IDEMPOTENCY_CONFLICT");
            await AuthorizeAsync(actor, cancellationToken);
            return Map(prior);
        }
        await gateway.RequireContainerRecursiveAclRestoreWorkflowAsync(cancellationToken); await AuthorizeAsync(actor, cancellationToken);
        var now = ContainerRecursiveAclRestoreWorkflowValidation.Time(clock.GetUtcNow());
        var binding = new ContainerRecursiveAclRestoreRequestBinding(id, idempotency, actor.Subject, actor.LoginBinding, actor.TraceId,
            ContainerRecursiveAclRestoreWorkflowValidation.Owner(actor.Subject, actor.LoginBinding), command, now, now.AddMinutes(15));
        var row = new ContainerRecursiveAclRestoreIntent { Id = id, IdempotencyKey = idempotency, ActorSubject = actor.Subject, LoginBinding = actor.LoginBinding,
            TraceId = actor.TraceId, OwnerHash = binding.Owner, ContainerId = command.Location.ContainerId, InstanceId = command.Location.InstanceId,
            RelativePath = command.Location.Path, CommandJson = ContainerRecursiveAclRestoreWorkflowValidation.Serialize(command),
            RequestHash = protector.FingerprintRecursiveAclRestoreRequest(binding), CreatedAtUtc = now, PlanningExpiresAtUtc = binding.PlanningExpiresAtUtc, NextAttemptAtUtc = now };
        var queued = await repository.EnqueueAsync(row, cancellationToken);
        await AuthorizeAsync(actor, cancellationToken); return Map(queued);
    }
    public async Task<ContainerRecursiveAclRestoreIntentDto> GetAsync(TerminalActor actor, string containerId, Guid id, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken); var row = await Owned(actor, containerId, id, cancellationToken);
        await AuthorizeAsync(actor, cancellationToken); return Map(row);
    }
    public Task<ContainerRecursiveAclRestoreIntentDto> GetByKeyAsync(TerminalActor actor, string containerId, string key, CancellationToken cancellationToken) =>
        GetAsync(actor, containerId, ContainerRecursiveAclRestoreWorkflowValidation.IntentId(actor.Subject, actor.LoginBinding, ContainerRecursiveAclRestoreWorkflowValidation.Key(key)), cancellationToken);
    public async Task<ContainerRecursiveAclRestoreIntentDto> ConfirmAsync(TerminalActor actor, string containerId, Guid id, ConfirmContainerRecursiveAclRestoreCommand command, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        if (command is null || !command.Confirmed || command.Version < 0) throw new ArgumentException("CONTAINER_RECURSIVE_ACL_RESTORE_CONFIRMATION_REQUIRED");
        var row = await Owned(actor, containerId, id, cancellationToken);
        var claims = protector.VerifyRecursiveAclRestoreConfirmation(command.PlanHash); ContainerRecursiveAclRestoreWorkflowValidation.Confirmation(claims);
        if (claims.Id != id || claims.Actor != actor.Subject || claims.LoginBinding != actor.LoginBinding) throw new UnauthorizedAccessException("CONTAINER_RECURSIVE_ACL_RESTORE_PLAN_OWNER_REQUIRED");
        if (row.PlanHash != claims.PlanHash || row.ReadyExpiresAtUtc != claims.ExpiresAtUtc) throw new InvalidOperationException("CONTAINER_RECURSIVE_ACL_RESTORE_PLAN_CHANGED");
        await AuthorizeAsync(actor, cancellationToken);
        var confirmed = await repository.ConfirmAsync(actor, claims, command.Version, cancellationToken);
        await AuthorizeAsync(actor, cancellationToken); return Map(confirmed);
    }
    public async Task<ContainerRecursiveAclRestoreIntentDto> CancelAsync(TerminalActor actor, string containerId, Guid id, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken); _ = await Owned(actor, containerId, id, cancellationToken);
        await AuthorizeAsync(actor, cancellationToken); await repository.CancelAsync(actor, id, cancellationToken);
        var canceled = await Owned(actor, containerId, id, cancellationToken);
        await AuthorizeAsync(actor, cancellationToken); return Map(canceled);
    }
    public async Task<IReadOnlyList<ContainerRecursiveAclRestoreIntentDto>> UnresolvedAsync(TerminalActor actor, int take, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        if (take is < 1 or > 50) throw new ArgumentException("CONTAINER_RECURSIVE_ACL_RESTORE_PAGE_INVALID");
        var rows = await repository.UnresolvedAsync(take, cancellationToken);
        await AuthorizeAsync(actor, cancellationToken); return rows.Select(Map).ToArray();
    }
    public async Task<ContainerRecursiveAclRestoreIntentDto> RecoveryGetAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        if (id == Guid.Empty) throw new ArgumentException("CONTAINER_RECURSIVE_ACL_RESTORE_ID_REQUIRED");
        var row = await repository.FindAsync(id, cancellationToken) ?? throw new KeyNotFoundException("CONTAINER_RECURSIVE_ACL_RESTORE_NOT_FOUND");
        await AuthorizeAsync(actor, cancellationToken); return Map(row);
    }
    public async Task AcknowledgeAsync(TerminalActor actor, Guid id, AcknowledgeContainerRecursiveAclRestoreCommand command, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        if (id == Guid.Empty || command is null || !command.Confirmed || command.Version < 0) throw new ArgumentException("CONTAINER_RECURSIVE_ACL_RESTORE_CONFIRMATION_REQUIRED");
        var row = await repository.FindAsync(id, cancellationToken) ?? throw new KeyNotFoundException("CONTAINER_RECURSIVE_ACL_RESTORE_NOT_FOUND"); Validate(row);
        if (row.State is not ("Unknown" or "Partial") || row.ExecutionJobId is null || row.CompletedAtUtc is not null || row.Version != command.Version)
            throw new InvalidOperationException("CONTAINER_RECURSIVE_ACL_RESTORE_ACKNOWLEDGEMENT_CONFLICT");
        var operation = ContainerRecursiveAclRestoreWorkflowValidation.Reference(row);
        // Discard is the permanent Root fence for the ORIGINAL execution ID.
        // Partial/Unknown retain actual changes and HDD proof; acknowledgement never restores ACLs or asserts whole-tree success.
        var receipt = await gateway.DiscardContainerRecursiveAclRestoreAsync(operation, cancellationToken); ContainerRecursiveAclRestoreWorkflowValidation.RequireCompatibleReceipt(row, receipt);
        await AuthorizeAsync(actor, cancellationToken);
        await repository.AcknowledgeAsync(actor, id, command.Version, receipt, cancellationToken);
    }
    private async Task<ContainerRecursiveAclRestoreIntent> Owned(TerminalActor actor, string containerId, Guid id, CancellationToken cancellationToken)
    {
        ContainerFileValidation.Id(containerId); if (id == Guid.Empty) throw new ArgumentException("CONTAINER_RECURSIVE_ACL_RESTORE_ID_REQUIRED");
        var row = await repository.OwnedAsync(actor, id, cancellationToken) ?? throw new KeyNotFoundException("CONTAINER_RECURSIVE_ACL_RESTORE_NOT_FOUND");
        if (row.ContainerId != containerId) throw new KeyNotFoundException("CONTAINER_RECURSIVE_ACL_RESTORE_NOT_FOUND"); Validate(row); return row;
    }
    private void Validate(ContainerRecursiveAclRestoreIntent row)
    { if (!ContainerRecursiveAclRestoreWorkflowValidation.HasValidBinding(row, protector)) throw new InvalidOperationException("CONTAINER_RECURSIVE_ACL_RESTORE_BINDING_CHANGED"); }
    private ContainerRecursiveAclRestoreIntentDto Map(ContainerRecursiveAclRestoreIntent row)
    {
        Validate(row); var plan = row.PlanJson is null ? null : ContainerRecursiveAclRestoreWorkflowValidation.ReadPlan(row);
        var ready = row.State == "Ready" && row.CancelRequestedAtUtc is null && row.ReadyExpiresAtUtc > clock.GetUtcNow();
        var confirmation = ready ? new ContainerRecursiveAclRestoreConfirmationDto(protector.SignRecursiveAclRestoreConfirmation(new(row.Id, row.ActorSubject, row.LoginBinding, row.PlanHash!, row.ReadyExpiresAtUtc!.Value)),
            row.ReadyExpiresAtUtc.Value, "这是递归历史ACL恢复：" + plan!.RestoreSource.CaptureId + "，仅恢复该来源原修改的" + plan.RestoreSource.ChangedAclType +
            "类型，按路径和对象类型逐项恢复异构ACL，不恢复历史inode/所有者或另一类当前ACL。预检不写入；确认后仍校验来源摘要/文件身份与当前清单，先留存完整当前前像。来源原计划的变更计数不等于本次恢复计数。取消可能部分修改；Partial/Unknown保留保护，不自动续做、重试写入或回滚；解除保护须当前管理员明确确认。") : null;
        return new(row.Id, row.Id, row.ExecutionJobId, ContainerRecursiveAclRestoreWorkflowValidation.ReadCommand(row), row.State == "Ready" && !ready ? "Expired" : row.State,
            plan?.RootIdentity.OwnerUid, plan?.RootIdentity.OwnerGid, plan?.RootIdentity.Mode, plan?.OwnerUid, plan?.OwnerGid, plan?.EntryCount, plan?.FileCount, plan?.DirectoryCount, plan?.ChangeCount,
            plan?.ManifestDigest, plan?.RestoreSource, row.ReadyExpiresAtUtc, row.ResultCode, row.Version, row.HistoryMayExist, row.HistoryVerified, row.HistoryDigest,
            ContainerRecursiveAclRestoreWorkflowValidation.ReadReceipt(row), confirmation);
    }
}
