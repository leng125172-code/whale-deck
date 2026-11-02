using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

/// <summary>HTTP-sized transitions only. Metadata attestation and recursive ordinary-mode changes belong to the dispatcher.</summary>
public sealed class ContainerRecursivePermissionWorkflowService(ContainerFileUploadService authorization, IContainerRecursivePermissionRepository repository,
    IContainerRecursivePermissionProtector protector, IContainerRecursivePermissionGateway gateway, TimeProvider clock, IPrivilegedIdentityDirectory liveIdentity)
{
    public async Task AuthorizeAsync(TerminalActor actor, CancellationToken cancellationToken)
    {
        FileManagementService.ValidateActor(actor);
        var current = await liveIdentity.ResolveForPrivilegedActionAsync(actor.Subject, actor.Name, cancellationToken);
        if (current.Subject != actor.Subject || !current.IsAdministrator || current.PermissionExpiresAtUtc <= clock.GetUtcNow())
            throw new UnauthorizedAccessException("CONTAINER_RECURSIVE_PERMISSION_CURRENT_ADMINISTRATOR_REQUIRED");
        await authorization.AuthorizeAsync(actor, cancellationToken);
    }
    public async Task<ContainerRecursivePermissionIntentDto> QueueAsync(TerminalActor actor, ContainerRecursivePermissionCommandDto command, string key, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken); ContainerRecursivePermissionValidation.Command(command);
        var idempotency = ContainerRecursivePermissionWorkflowValidation.Key(key);
        var id = ContainerRecursivePermissionWorkflowValidation.IntentId(actor.Subject, actor.LoginBinding, idempotency);
        var prior = await repository.OwnedAsync(actor, id, cancellationToken);
        if (prior is not null)
        {
            Validate(prior);
            if (ContainerRecursivePermissionWorkflowValidation.ReadCommand(prior) != command) throw new InvalidOperationException("CONTAINER_RECURSIVE_PERMISSION_IDEMPOTENCY_CONFLICT");
            return Map(prior);
        }
        await gateway.RequireContainerRecursivePermissionWorkflowAsync(cancellationToken); await AuthorizeAsync(actor, cancellationToken);
        var now = ContainerRecursivePermissionWorkflowValidation.Time(clock.GetUtcNow());
        var binding = new ContainerRecursivePermissionRequestBinding(id, idempotency, actor.Subject, actor.LoginBinding, actor.TraceId,
            ContainerRecursivePermissionWorkflowValidation.Owner(actor.Subject, actor.LoginBinding), command, now, now.AddMinutes(15));
        var row = new ContainerRecursivePermissionIntent { Id = id, IdempotencyKey = idempotency, ActorSubject = actor.Subject, LoginBinding = actor.LoginBinding,
            TraceId = actor.TraceId, OwnerHash = binding.Owner, ContainerId = command.Location.ContainerId, InstanceId = command.Location.InstanceId,
            RelativePath = command.Location.Path, CommandJson = ContainerRecursivePermissionWorkflowValidation.Serialize(command),
            RequestHash = protector.FingerprintRecursivePermissionRequest(binding), CreatedAtUtc = now, PlanningExpiresAtUtc = binding.PlanningExpiresAtUtc, NextAttemptAtUtc = now };
        return Map(await repository.EnqueueAsync(row, cancellationToken));
    }
    public async Task<ContainerRecursivePermissionIntentDto> GetAsync(TerminalActor actor, string containerId, Guid id, CancellationToken cancellationToken)
    { await AuthorizeAsync(actor, cancellationToken); return Map(await Owned(actor, containerId, id, cancellationToken)); }
    public Task<ContainerRecursivePermissionIntentDto> GetByKeyAsync(TerminalActor actor, string containerId, string key, CancellationToken cancellationToken) =>
        GetAsync(actor, containerId, ContainerRecursivePermissionWorkflowValidation.IntentId(actor.Subject, actor.LoginBinding, ContainerRecursivePermissionWorkflowValidation.Key(key)), cancellationToken);
    public async Task<ContainerRecursivePermissionIntentDto> ConfirmAsync(TerminalActor actor, string containerId, Guid id, ConfirmContainerRecursivePermissionCommand command, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        if (command is null || !command.Confirmed || command.Version < 0) throw new ArgumentException("CONTAINER_RECURSIVE_PERMISSION_CONFIRMATION_REQUIRED");
        var row = await Owned(actor, containerId, id, cancellationToken);
        var claims = protector.VerifyRecursivePermissionConfirmation(command.PlanHash); ContainerRecursivePermissionWorkflowValidation.Confirmation(claims);
        if (claims.Id != id || claims.Actor != actor.Subject || claims.LoginBinding != actor.LoginBinding) throw new UnauthorizedAccessException("CONTAINER_RECURSIVE_PERMISSION_PLAN_OWNER_REQUIRED");
        if (row.PlanHash != claims.PlanHash || row.ReadyExpiresAtUtc != claims.ExpiresAtUtc) throw new InvalidOperationException("CONTAINER_RECURSIVE_PERMISSION_PLAN_CHANGED");
        await AuthorizeAsync(actor, cancellationToken);
        return Map(await repository.ConfirmAsync(actor, claims, command.Version, cancellationToken));
    }
    public async Task<ContainerRecursivePermissionIntentDto> CancelAsync(TerminalActor actor, string containerId, Guid id, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken); _ = await Owned(actor, containerId, id, cancellationToken);
        await repository.CancelAsync(actor, id, cancellationToken); return Map(await Owned(actor, containerId, id, cancellationToken));
    }
    public async Task<IReadOnlyList<ContainerRecursivePermissionIntentDto>> UnresolvedAsync(TerminalActor actor, int take, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        if (take is < 1 or > 50) throw new ArgumentException("CONTAINER_RECURSIVE_PERMISSION_PAGE_INVALID");
        var rows = await repository.UnresolvedAsync(take, cancellationToken); return rows.Select(Map).ToArray();
    }
    public async Task<ContainerRecursivePermissionIntentDto> RecoveryGetAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        if (id == Guid.Empty) throw new ArgumentException("CONTAINER_RECURSIVE_PERMISSION_ID_REQUIRED");
        return Map(await repository.FindAsync(id, cancellationToken) ?? throw new KeyNotFoundException("CONTAINER_RECURSIVE_PERMISSION_NOT_FOUND"));
    }
    public async Task AcknowledgeAsync(TerminalActor actor, Guid id, AcknowledgeContainerRecursivePermissionCommand command, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        if (id == Guid.Empty || command is null || !command.Confirmed || command.Version < 0) throw new ArgumentException("CONTAINER_RECURSIVE_PERMISSION_CONFIRMATION_REQUIRED");
        var row = await repository.FindAsync(id, cancellationToken) ?? throw new KeyNotFoundException("CONTAINER_RECURSIVE_PERMISSION_NOT_FOUND"); Validate(row);
        if (row.State is not ("Unknown" or "Partial") || row.ExecutionJobId is null || row.CompletedAtUtc is not null || row.Version != command.Version)
            throw new InvalidOperationException("CONTAINER_RECURSIVE_PERMISSION_ACKNOWLEDGEMENT_CONFLICT");
        var operation = ContainerRecursivePermissionWorkflowValidation.Reference(row);
        // Discard is the permanent Root fence for the ORIGINAL execution ID.
        // Partial/Unknown retain actual changes and HDD proof; acknowledgement never restores modes or asserts whole-tree success.
        var receipt = await gateway.DiscardContainerRecursivePermissionAsync(operation, cancellationToken); ContainerRecursivePermissionWorkflowValidation.RequireCompatibleReceipt(row, receipt);
        await AuthorizeAsync(actor, cancellationToken);
        await repository.AcknowledgeAsync(actor, id, command.Version, receipt, cancellationToken);
    }
    private async Task<ContainerRecursivePermissionIntent> Owned(TerminalActor actor, string containerId, Guid id, CancellationToken cancellationToken)
    {
        ContainerFileValidation.Id(containerId); if (id == Guid.Empty) throw new ArgumentException("CONTAINER_RECURSIVE_PERMISSION_ID_REQUIRED");
        var row = await repository.OwnedAsync(actor, id, cancellationToken) ?? throw new KeyNotFoundException("CONTAINER_RECURSIVE_PERMISSION_NOT_FOUND");
        if (row.ContainerId != containerId) throw new KeyNotFoundException("CONTAINER_RECURSIVE_PERMISSION_NOT_FOUND"); Validate(row); return row;
    }
    private void Validate(ContainerRecursivePermissionIntent row)
    { if (!ContainerRecursivePermissionWorkflowValidation.HasValidBinding(row, protector)) throw new InvalidOperationException("CONTAINER_RECURSIVE_PERMISSION_BINDING_CHANGED"); }
    private ContainerRecursivePermissionIntentDto Map(ContainerRecursivePermissionIntent row)
    {
        Validate(row); var plan = row.PlanJson is null ? null : ContainerRecursivePermissionWorkflowValidation.ReadPlan(row);
        var ready = row.State == "Ready" && row.CancelRequestedAtUtc is null && row.ReadyExpiresAtUtc > clock.GetUtcNow();
        var confirmation = ready ? new ContainerRecursivePermissionConfirmationDto(protector.SignRecursivePermissionConfirmation(new(row.Id, row.ActorSubject, row.LoginBinding, row.PlanHash!, row.ReadyExpiresAtUtc!.Value)),
            row.ReadyExpiresAtUtc.Value, "预检不修改权限。确认后按原实例、目录树清单和逐项版本校验，先保留HDD元数据，再按文件/目录各自普通模式修改原inode。ACL、所有者和特殊位不在本操作中。取消后可能部分修改；Partial/Unknown保留保护，查询原任务，不自动续做或回滚；解除保护须当前管理员明确确认。") : null;
        return new(row.Id, row.Id, row.ExecutionJobId, ContainerRecursivePermissionWorkflowValidation.ReadCommand(row), row.State == "Ready" && !ready ? "Expired" : row.State,
            plan?.RootIdentity.OwnerUid, plan?.RootIdentity.OwnerGid, plan?.OwnerUid, plan?.OwnerGid, plan?.EntryCount, plan?.FileCount, plan?.DirectoryCount, plan?.ChangeCount,
            plan?.ManifestDigest, row.ReadyExpiresAtUtc, row.ResultCode, row.Version, row.HistoryMayExist, row.HistoryVerified, row.HistoryDigest,
            ContainerRecursivePermissionWorkflowValidation.ReadReceipt(row), confirmation);
    }
}
