using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

/// <summary>HTTP-sized transitions only. Metadata attestation and recursive ownership changes belong to the dispatcher.</summary>
public sealed class ContainerRecursiveOwnershipWorkflowService(ContainerFileUploadService authorization, IContainerRecursiveOwnershipRepository repository,
    IContainerRecursiveOwnershipProtector protector, IContainerRecursiveOwnershipGateway gateway, TimeProvider clock, IPrivilegedIdentityDirectory liveIdentity)
{
    public async Task AuthorizeAsync(TerminalActor actor, CancellationToken cancellationToken)
    {
        FileManagementService.ValidateActor(actor);
        var current = await liveIdentity.ResolveForPrivilegedActionAsync(actor.Subject, actor.Name, cancellationToken);
        if (current.Subject != actor.Subject || !current.IsAdministrator || current.PermissionExpiresAtUtc <= clock.GetUtcNow())
            throw new UnauthorizedAccessException("CONTAINER_RECURSIVE_OWNERSHIP_CURRENT_ADMINISTRATOR_REQUIRED");
        await authorization.AuthorizeAsync(actor, cancellationToken);
    }
    public async Task<ContainerRecursiveOwnershipIntentDto> QueueAsync(TerminalActor actor, ContainerRecursiveOwnershipCommandDto command, string key, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken); ContainerRecursiveOwnershipValidation.Command(command);
        var idempotency = ContainerRecursiveOwnershipWorkflowValidation.Key(key);
        var id = ContainerRecursiveOwnershipWorkflowValidation.IntentId(actor.Subject, actor.LoginBinding, idempotency);
        var prior = await repository.OwnedAsync(actor, id, cancellationToken);
        if (prior is not null)
        {
            Validate(prior);
            if (ContainerRecursiveOwnershipWorkflowValidation.ReadCommand(prior) != command) throw new InvalidOperationException("CONTAINER_RECURSIVE_OWNERSHIP_IDEMPOTENCY_CONFLICT");
            await AuthorizeAsync(actor, cancellationToken);
            return Map(prior);
        }
        await gateway.RequireContainerRecursiveOwnershipWorkflowAsync(cancellationToken); await AuthorizeAsync(actor, cancellationToken);
        var now = ContainerRecursiveOwnershipWorkflowValidation.Time(clock.GetUtcNow());
        var binding = new ContainerRecursiveOwnershipRequestBinding(id, idempotency, actor.Subject, actor.LoginBinding, actor.TraceId,
            ContainerRecursiveOwnershipWorkflowValidation.Owner(actor.Subject, actor.LoginBinding), command, now, now.AddMinutes(15));
        var row = new ContainerRecursiveOwnershipIntent { Id = id, IdempotencyKey = idempotency, ActorSubject = actor.Subject, LoginBinding = actor.LoginBinding,
            TraceId = actor.TraceId, OwnerHash = binding.Owner, ContainerId = command.Location.ContainerId, InstanceId = command.Location.InstanceId,
            RelativePath = command.Location.Path, CommandJson = ContainerRecursiveOwnershipWorkflowValidation.Serialize(command),
            RequestHash = protector.FingerprintRecursiveOwnershipRequest(binding), CreatedAtUtc = now, PlanningExpiresAtUtc = binding.PlanningExpiresAtUtc, NextAttemptAtUtc = now };
        var queued = await repository.EnqueueAsync(row, cancellationToken);
        await AuthorizeAsync(actor, cancellationToken); return Map(queued);
    }
    public async Task<ContainerRecursiveOwnershipIntentDto> GetAsync(TerminalActor actor, string containerId, Guid id, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken); var row = await Owned(actor, containerId, id, cancellationToken);
        await AuthorizeAsync(actor, cancellationToken); return Map(row);
    }
    public Task<ContainerRecursiveOwnershipIntentDto> GetByKeyAsync(TerminalActor actor, string containerId, string key, CancellationToken cancellationToken) =>
        GetAsync(actor, containerId, ContainerRecursiveOwnershipWorkflowValidation.IntentId(actor.Subject, actor.LoginBinding, ContainerRecursiveOwnershipWorkflowValidation.Key(key)), cancellationToken);
    public async Task<ContainerRecursiveOwnershipIntentDto> ConfirmAsync(TerminalActor actor, string containerId, Guid id, ConfirmContainerRecursiveOwnershipCommand command, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        if (command is null || !command.Confirmed || command.Version < 0) throw new ArgumentException("CONTAINER_RECURSIVE_OWNERSHIP_CONFIRMATION_REQUIRED");
        var row = await Owned(actor, containerId, id, cancellationToken);
        var claims = protector.VerifyRecursiveOwnershipConfirmation(command.PlanHash); ContainerRecursiveOwnershipWorkflowValidation.Confirmation(claims);
        if (claims.Id != id || claims.Actor != actor.Subject || claims.LoginBinding != actor.LoginBinding) throw new UnauthorizedAccessException("CONTAINER_RECURSIVE_OWNERSHIP_PLAN_OWNER_REQUIRED");
        if (row.PlanHash != claims.PlanHash || row.ReadyExpiresAtUtc != claims.ExpiresAtUtc) throw new InvalidOperationException("CONTAINER_RECURSIVE_OWNERSHIP_PLAN_CHANGED");
        await AuthorizeAsync(actor, cancellationToken);
        var confirmed = await repository.ConfirmAsync(actor, claims, command.Version, cancellationToken);
        await AuthorizeAsync(actor, cancellationToken); return Map(confirmed);
    }
    public async Task<ContainerRecursiveOwnershipIntentDto> CancelAsync(TerminalActor actor, string containerId, Guid id, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken); _ = await Owned(actor, containerId, id, cancellationToken);
        await AuthorizeAsync(actor, cancellationToken); await repository.CancelAsync(actor, id, cancellationToken);
        var canceled = await Owned(actor, containerId, id, cancellationToken);
        await AuthorizeAsync(actor, cancellationToken); return Map(canceled);
    }
    public async Task<IReadOnlyList<ContainerRecursiveOwnershipIntentDto>> UnresolvedAsync(TerminalActor actor, int take, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        if (take is < 1 or > 50) throw new ArgumentException("CONTAINER_RECURSIVE_OWNERSHIP_PAGE_INVALID");
        var rows = await repository.UnresolvedAsync(take, cancellationToken);
        await AuthorizeAsync(actor, cancellationToken); return rows.Select(Map).ToArray();
    }
    public async Task<ContainerRecursiveOwnershipIntentDto> RecoveryGetAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        if (id == Guid.Empty) throw new ArgumentException("CONTAINER_RECURSIVE_OWNERSHIP_ID_REQUIRED");
        var row = await repository.FindAsync(id, cancellationToken) ?? throw new KeyNotFoundException("CONTAINER_RECURSIVE_OWNERSHIP_NOT_FOUND");
        await AuthorizeAsync(actor, cancellationToken); return Map(row);
    }
    public async Task AcknowledgeAsync(TerminalActor actor, Guid id, AcknowledgeContainerRecursiveOwnershipCommand command, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        if (id == Guid.Empty || command is null || !command.Confirmed || command.Version < 0) throw new ArgumentException("CONTAINER_RECURSIVE_OWNERSHIP_CONFIRMATION_REQUIRED");
        var row = await repository.FindAsync(id, cancellationToken) ?? throw new KeyNotFoundException("CONTAINER_RECURSIVE_OWNERSHIP_NOT_FOUND"); Validate(row);
        if (row.State is not ("Unknown" or "Partial") || row.ExecutionJobId is null || row.CompletedAtUtc is not null || row.Version != command.Version)
            throw new InvalidOperationException("CONTAINER_RECURSIVE_OWNERSHIP_ACKNOWLEDGEMENT_CONFLICT");
        var operation = ContainerRecursiveOwnershipWorkflowValidation.Reference(row);
        // Discard is the permanent Root fence for the ORIGINAL execution ID.
        // Partial/Unknown retain actual changes and HDD proof; acknowledgement never restores owners or asserts whole-tree success.
        var receipt = await gateway.DiscardContainerRecursiveOwnershipAsync(operation, cancellationToken); ContainerRecursiveOwnershipWorkflowValidation.RequireCompatibleReceipt(row, receipt);
        await AuthorizeAsync(actor, cancellationToken);
        await repository.AcknowledgeAsync(actor, id, command.Version, receipt, cancellationToken);
    }
    private async Task<ContainerRecursiveOwnershipIntent> Owned(TerminalActor actor, string containerId, Guid id, CancellationToken cancellationToken)
    {
        ContainerFileValidation.Id(containerId); if (id == Guid.Empty) throw new ArgumentException("CONTAINER_RECURSIVE_OWNERSHIP_ID_REQUIRED");
        var row = await repository.OwnedAsync(actor, id, cancellationToken) ?? throw new KeyNotFoundException("CONTAINER_RECURSIVE_OWNERSHIP_NOT_FOUND");
        if (row.ContainerId != containerId) throw new KeyNotFoundException("CONTAINER_RECURSIVE_OWNERSHIP_NOT_FOUND"); Validate(row); return row;
    }
    private void Validate(ContainerRecursiveOwnershipIntent row)
    { if (!ContainerRecursiveOwnershipWorkflowValidation.HasValidBinding(row, protector)) throw new InvalidOperationException("CONTAINER_RECURSIVE_OWNERSHIP_BINDING_CHANGED"); }
    private ContainerRecursiveOwnershipIntentDto Map(ContainerRecursiveOwnershipIntent row)
    {
        Validate(row); var plan = row.PlanJson is null ? null : ContainerRecursiveOwnershipWorkflowValidation.ReadPlan(row);
        var ready = row.State == "Ready" && row.CancelRequestedAtUtc is null && row.ReadyExpiresAtUtc > clock.GetUtcNow();
        var confirmation = ready ? new ContainerRecursiveOwnershipConfirmationDto(protector.SignRecursiveOwnershipConfirmation(new(row.Id, row.ActorSubject, row.LoginBinding, row.PlanHash!, row.ReadyExpiresAtUtc!.Value)),
            row.ReadyExpiresAtUtc.Value, "预检不修改所有者。确认后按原实例、目录树清单和逐项版本校验，先保留HDD旧属主元数据，再将每项原inode显式修改为目标UID/GID（0表示root），目录最后处理。权限模式、ACL和特殊位不在本操作中。取消后可能部分修改；Partial/Unknown保留保护，查询原任务，不自动续做或回滚；解除保护须当前管理员明确确认。") : null;
        return new(row.Id, row.Id, row.ExecutionJobId, ContainerRecursiveOwnershipWorkflowValidation.ReadCommand(row), row.State == "Ready" && !ready ? "Expired" : row.State,
            plan?.RootIdentity.OwnerUid, plan?.RootIdentity.OwnerGid, plan?.OwnerUid, plan?.OwnerGid, plan?.EntryCount, plan?.FileCount, plan?.DirectoryCount, plan?.ChangeCount,
            plan?.ManifestDigest, row.ReadyExpiresAtUtc, row.ResultCode, row.Version, row.HistoryMayExist, row.HistoryVerified, row.HistoryDigest,
            ContainerRecursiveOwnershipWorkflowValidation.ReadReceipt(row), confirmation);
    }
}
