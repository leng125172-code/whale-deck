using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

/// <summary>HTTP-sized transitions only. Metadata attestation and mode changes belong to the dispatcher.</summary>
public sealed class ContainerPermissionWorkflowService(ContainerFileUploadService authorization, IContainerPermissionRepository repository,
    IContainerPermissionProtector protector, IContainerPermissionGateway gateway, TimeProvider clock)
{
    public Task AuthorizeAsync(TerminalActor actor, CancellationToken cancellationToken) => authorization.AuthorizeAsync(actor, cancellationToken);
    public async Task<ContainerPermissionIntentDto> QueueAsync(TerminalActor actor, ContainerPermissionCommandDto command, string key, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken); ContainerPermissionValidation.Command(command);
        var idempotency = ContainerPermissionWorkflowValidation.Key(key);
        var id = ContainerPermissionWorkflowValidation.IntentId(actor.Subject, actor.LoginBinding, idempotency);
        var prior = await repository.OwnedAsync(actor, id, cancellationToken);
        if (prior is not null)
        {
            Validate(prior);
            if (ContainerPermissionWorkflowValidation.ReadCommand(prior) != command) throw new InvalidOperationException("CONTAINER_PERMISSION_IDEMPOTENCY_CONFLICT");
            return Map(prior);
        }
        await gateway.RequireContainerPermissionWorkflowAsync(cancellationToken); await AuthorizeAsync(actor, cancellationToken);
        var now = ContainerPermissionWorkflowValidation.Time(clock.GetUtcNow());
        var binding = new ContainerPermissionRequestBinding(id, idempotency, actor.Subject, actor.LoginBinding, actor.TraceId,
            ContainerPermissionWorkflowValidation.Owner(actor.Subject, actor.LoginBinding), command, now, now.AddMinutes(15));
        var row = new ContainerPermissionIntent { Id = id, IdempotencyKey = idempotency, ActorSubject = actor.Subject, LoginBinding = actor.LoginBinding,
            TraceId = actor.TraceId, OwnerHash = binding.Owner, ContainerId = command.Location.ContainerId, InstanceId = command.Location.InstanceId,
            RelativePath = command.Location.Path, CommandJson = ContainerPermissionWorkflowValidation.Serialize(command),
            RequestHash = protector.FingerprintPermissionRequest(binding), CreatedAtUtc = now, PlanningExpiresAtUtc = binding.PlanningExpiresAtUtc, NextAttemptAtUtc = now };
        return Map(await repository.EnqueueAsync(row, cancellationToken));
    }
    public async Task<ContainerPermissionIntentDto> GetAsync(TerminalActor actor, string containerId, Guid id, CancellationToken cancellationToken)
    { await AuthorizeAsync(actor, cancellationToken); return Map(await Owned(actor, containerId, id, cancellationToken)); }
    public Task<ContainerPermissionIntentDto> GetByKeyAsync(TerminalActor actor, string containerId, string key, CancellationToken cancellationToken) =>
        GetAsync(actor, containerId, ContainerPermissionWorkflowValidation.IntentId(actor.Subject, actor.LoginBinding, ContainerPermissionWorkflowValidation.Key(key)), cancellationToken);
    public async Task<ContainerPermissionIntentDto> ConfirmAsync(TerminalActor actor, string containerId, Guid id, ConfirmContainerPermissionCommand command, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        if (command is null || !command.Confirmed || command.Version < 0) throw new ArgumentException("CONTAINER_PERMISSION_CONFIRMATION_REQUIRED");
        var row = await Owned(actor, containerId, id, cancellationToken);
        var claims = protector.VerifyPermissionConfirmation(command.PlanHash); ContainerPermissionWorkflowValidation.Confirmation(claims);
        if (claims.Id != id || claims.Actor != actor.Subject || claims.LoginBinding != actor.LoginBinding) throw new UnauthorizedAccessException("CONTAINER_PERMISSION_PLAN_OWNER_REQUIRED");
        if (row.PlanHash != claims.PlanHash || row.ReadyExpiresAtUtc != claims.ExpiresAtUtc) throw new InvalidOperationException("CONTAINER_PERMISSION_PLAN_CHANGED");
        await AuthorizeAsync(actor, cancellationToken);
        return Map(await repository.ConfirmAsync(actor, claims, command.Version, cancellationToken));
    }
    public async Task<ContainerPermissionIntentDto> CancelAsync(TerminalActor actor, string containerId, Guid id, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken); _ = await Owned(actor, containerId, id, cancellationToken);
        await repository.CancelAsync(actor, id, cancellationToken); return Map(await Owned(actor, containerId, id, cancellationToken));
    }
    public async Task<IReadOnlyList<ContainerPermissionIntentDto>> UnknownAsync(TerminalActor actor, int take, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        if (take is < 1 or > 50) throw new ArgumentException("CONTAINER_PERMISSION_PAGE_INVALID");
        var rows = await repository.UnknownAsync(take, cancellationToken); return rows.Select(Map).ToArray();
    }
    public async Task<ContainerPermissionIntentDto> RecoveryGetAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        if (id == Guid.Empty) throw new ArgumentException("CONTAINER_PERMISSION_ID_REQUIRED");
        return Map(await repository.FindAsync(id, cancellationToken) ?? throw new KeyNotFoundException("CONTAINER_PERMISSION_NOT_FOUND"));
    }
    public async Task AcknowledgeAsync(TerminalActor actor, Guid id, AcknowledgeContainerPermissionCommand command, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        if (id == Guid.Empty || command is null || !command.Confirmed || command.Version < 0) throw new ArgumentException("CONTAINER_PERMISSION_CONFIRMATION_REQUIRED");
        var row = await repository.FindAsync(id, cancellationToken) ?? throw new KeyNotFoundException("CONTAINER_PERMISSION_NOT_FOUND"); Validate(row);
        if (row.State != "Unknown" || row.ExecutionJobId is null || row.CompletedAtUtc is not null || row.Version != command.Version)
            throw new InvalidOperationException("CONTAINER_PERMISSION_ACKNOWLEDGEMENT_CONFLICT");
        var operation = ContainerPermissionWorkflowValidation.Reference(row);
        // Discard is the permanent Root fence for the ORIGINAL execution ID.
        // Unknown may retain HDD metadata or a changed mode; acknowledgement never restores permissions or asserts success.
        var receipt = await gateway.DiscardContainerPermissionAsync(operation, cancellationToken); ContainerPermissionValidation.Receipt(operation, receipt);
        await AuthorizeAsync(actor, cancellationToken);
        await repository.AcknowledgeAsync(actor, id, command.Version, receipt, cancellationToken);
    }
    private async Task<ContainerPermissionIntent> Owned(TerminalActor actor, string containerId, Guid id, CancellationToken cancellationToken)
    {
        ContainerFileValidation.Id(containerId); if (id == Guid.Empty) throw new ArgumentException("CONTAINER_PERMISSION_ID_REQUIRED");
        var row = await repository.OwnedAsync(actor, id, cancellationToken) ?? throw new KeyNotFoundException("CONTAINER_PERMISSION_NOT_FOUND");
        if (row.ContainerId != containerId) throw new KeyNotFoundException("CONTAINER_PERMISSION_NOT_FOUND"); Validate(row); return row;
    }
    private void Validate(ContainerPermissionIntent row)
    { if (!ContainerPermissionWorkflowValidation.HasValidBinding(row, protector)) throw new InvalidOperationException("CONTAINER_PERMISSION_BINDING_CHANGED"); }
    private ContainerPermissionIntentDto Map(ContainerPermissionIntent row)
    {
        Validate(row); var plan = row.PlanJson is null ? null : ContainerPermissionWorkflowValidation.ReadPlan(row);
        var ready = row.State == "Ready" && row.CancelRequestedAtUtc is null && row.ReadyExpiresAtUtc > clock.GetUtcNow();
        var confirmation = ready ? new ContainerPermissionConfirmationDto(protector.SignPermissionConfirmation(new(row.Id, row.ActorSubject, row.LoginBinding, row.PlanHash!, row.ReadyExpiresAtUtc!.Value)),
            row.ReadyExpiresAtUtc.Value, "预检不修改权限。确认后校验原容器实例、原文件版本和属性，先保留HDD元数据，再仅修改当前对象的普通权限；不递归、不修改所有者或ACL。断线后查询原任务，未知结果保留保护，不自动重做或恢复权限。") : null;
        return new(row.Id, row.Id, row.ExecutionJobId, ContainerPermissionWorkflowValidation.ReadCommand(row), row.State == "Ready" && !ready ? "Expired" : row.State,
            plan?.Kind, plan is null ? null : Convert.ToString(plan.SourceIdentity.Mode & 0x1FF, 8).PadLeft(4, '0'), plan?.NoChange, row.ReadyExpiresAtUtc, row.ResultCode, row.Version,
            row.PublishedVersion, row.ModeConfirmed, row.HistoryMayExist, row.HistoryVerified, row.HistoryDigest, confirmation);
    }
}
