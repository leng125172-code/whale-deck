using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

/// <summary>HTTP-sized transitions only. Full source scans and moves belong to the dispatcher.</summary>
public sealed class ContainerMoveWorkflowService(ContainerFileUploadService authorization, IContainerMoveRepository repository,
    IContainerMoveProtector protector, IContainerMoveGateway gateway, TimeProvider clock)
{
    public Task AuthorizeAsync(TerminalActor actor, CancellationToken cancellationToken) => authorization.AuthorizeAsync(actor, cancellationToken);
    public async Task<ContainerMoveIntentDto> QueueAsync(TerminalActor actor, ContainerMoveCommandDto command, string key, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken); ContainerMoveValidation.Command(command);
        var idempotency = ContainerMoveWorkflowValidation.Key(key);
        var id = ContainerMoveWorkflowValidation.IntentId(actor.Subject, actor.LoginBinding, idempotency);
        var prior = await repository.OwnedAsync(actor, id, cancellationToken);
        if (prior is not null)
        {
            Validate(prior);
            if (ContainerMoveWorkflowValidation.ReadCommand(prior) != command) throw new InvalidOperationException("CONTAINER_MOVE_IDEMPOTENCY_CONFLICT");
            return Map(prior);
        }
        await gateway.RequireContainerMoveWorkflowAsync(cancellationToken); await AuthorizeAsync(actor, cancellationToken);
        var now = ContainerMoveWorkflowValidation.Time(clock.GetUtcNow());
        var binding = new ContainerMoveRequestBinding(id, idempotency, actor.Subject, actor.LoginBinding, actor.TraceId,
            ContainerMoveWorkflowValidation.Owner(actor.Subject, actor.LoginBinding), command, now, now.AddMinutes(15));
        var row = new ContainerMoveIntent { Id = id, IdempotencyKey = idempotency, ActorSubject = actor.Subject, LoginBinding = actor.LoginBinding,
            TraceId = actor.TraceId, OwnerHash = binding.Owner, ContainerId = command.Source.ContainerId, InstanceId = command.Source.InstanceId,
            SourcePath = command.Source.Path, TargetPath = command.TargetPath, CommandJson = ContainerMoveWorkflowValidation.Serialize(command),
            RequestHash = protector.FingerprintContainerMoveRequest(binding), CreatedAtUtc = now, PlanningExpiresAtUtc = binding.PlanningExpiresAtUtc, NextAttemptAtUtc = now };
        return Map(await repository.EnqueueAsync(row, cancellationToken));
    }
    public async Task<ContainerMoveIntentDto> GetAsync(TerminalActor actor, string containerId, Guid id, CancellationToken cancellationToken)
    { await AuthorizeAsync(actor, cancellationToken); return Map(await Owned(actor, containerId, id, cancellationToken)); }
    public Task<ContainerMoveIntentDto> GetByKeyAsync(TerminalActor actor, string containerId, string key, CancellationToken cancellationToken) =>
        GetAsync(actor, containerId, ContainerMoveWorkflowValidation.IntentId(actor.Subject, actor.LoginBinding, ContainerMoveWorkflowValidation.Key(key)), cancellationToken);
    public async Task<ContainerMoveIntentDto> ConfirmAsync(TerminalActor actor, string containerId, Guid id, ConfirmContainerMoveCommand command, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        if (command is null || !command.Confirmed || command.Version < 0) throw new ArgumentException("CONTAINER_MOVE_CONFIRMATION_REQUIRED");
        var row = await Owned(actor, containerId, id, cancellationToken);
        var claims = protector.VerifyContainerMoveConfirmation(command.PlanHash); ContainerMoveWorkflowValidation.Confirmation(claims);
        if (claims.Id != id || claims.Actor != actor.Subject || claims.LoginBinding != actor.LoginBinding) throw new UnauthorizedAccessException("CONTAINER_MOVE_PLAN_OWNER_REQUIRED");
        if (row.PlanHash != claims.PlanHash || row.ReadyExpiresAtUtc != claims.ExpiresAtUtc) throw new InvalidOperationException("CONTAINER_MOVE_PLAN_CHANGED");
        await AuthorizeAsync(actor, cancellationToken);
        return Map(await repository.ConfirmAsync(actor, claims, command.Version, cancellationToken));
    }
    public async Task<ContainerMoveIntentDto> CancelAsync(TerminalActor actor, string containerId, Guid id, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken); _ = await Owned(actor, containerId, id, cancellationToken);
        await repository.CancelAsync(actor, id, cancellationToken); return Map(await Owned(actor, containerId, id, cancellationToken));
    }
    public async Task<IReadOnlyList<ContainerMoveIntentDto>> UnknownAsync(TerminalActor actor, int take, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        if (take is < 1 or > 50) throw new ArgumentException("CONTAINER_MOVE_PAGE_INVALID");
        var rows = await repository.UnknownAsync(take, cancellationToken); return rows.Select(Map).ToArray();
    }
    public async Task<ContainerMoveIntentDto> RecoveryGetAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        if (id == Guid.Empty) throw new ArgumentException("CONTAINER_MOVE_ID_REQUIRED");
        return Map(await repository.FindAsync(id, cancellationToken) ?? throw new KeyNotFoundException("CONTAINER_MOVE_NOT_FOUND"));
    }
    public async Task AcknowledgeAsync(TerminalActor actor, Guid id, AcknowledgeContainerMoveCommand command, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        if (id == Guid.Empty || command is null || !command.Confirmed || command.Version < 0) throw new ArgumentException("CONTAINER_MOVE_CONFIRMATION_REQUIRED");
        var row = await repository.FindAsync(id, cancellationToken) ?? throw new KeyNotFoundException("CONTAINER_MOVE_NOT_FOUND"); Validate(row);
        if (row.State != "Unknown" || row.ExecutionJobId is null || row.CompletedAtUtc is not null || row.Version != command.Version)
            throw new InvalidOperationException("CONTAINER_MOVE_ACKNOWLEDGEMENT_CONFLICT");
        var operation = ContainerMoveWorkflowValidation.Reference(row);
        // Discard is the permanent Root fence for the ORIGINAL execution ID.
        // Unknown may retain private objects; acknowledgement never deletes them or asserts success.
        var receipt = await gateway.DiscardContainerMoveAsync(operation, cancellationToken); ContainerMoveValidation.Receipt(operation, receipt);
        await AuthorizeAsync(actor, cancellationToken);
        await repository.AcknowledgeAsync(actor, id, command.Version, receipt, cancellationToken);
    }
    private async Task<ContainerMoveIntent> Owned(TerminalActor actor, string containerId, Guid id, CancellationToken cancellationToken)
    {
        ContainerFileValidation.Id(containerId); if (id == Guid.Empty) throw new ArgumentException("CONTAINER_MOVE_ID_REQUIRED");
        var row = await repository.OwnedAsync(actor, id, cancellationToken) ?? throw new KeyNotFoundException("CONTAINER_MOVE_NOT_FOUND");
        if (row.ContainerId != containerId) throw new KeyNotFoundException("CONTAINER_MOVE_NOT_FOUND"); Validate(row); return row;
    }
    private void Validate(ContainerMoveIntent row)
    { if (!ContainerMoveWorkflowValidation.HasValidBinding(row, protector)) throw new InvalidOperationException("CONTAINER_MOVE_BINDING_CHANGED"); }
    private ContainerMoveIntentDto Map(ContainerMoveIntent row)
    {
        Validate(row); var plan = row.PlanJson is null ? null : ContainerMoveWorkflowValidation.ReadPlan(row);
        var ready = row.State == "Ready" && row.CancelRequestedAtUtc is null && row.ReadyExpiresAtUtc > clock.GetUtcNow();
        var confirmation = ready ? new ContainerMoveConfirmationDto(protector.SignContainerMoveConfirmation(new(row.Id, row.ActorSubject, row.LoginBinding, row.PlanHash!, row.ReadyExpiresAtUtc!.Value)),
            row.ReadyExpiresAtUtc.Value, "预检不移动数据。确认后再次校验原容器实例和完整源内容，仅创建不存在的目标；完整校验目标后，将源文件转存到私有原件区并移除原路径。断线后查询原任务；未知结果保留保护，发布、源移除和原件保留事实分别展示，不自动重做。") : null;
        return new(row.Id, row.Id, row.ExecutionJobId, ContainerMoveWorkflowValidation.ReadCommand(row), "Move", row.State == "Ready" && !ready ? "Expired" : row.State,
            plan?.Kind, plan?.Kind == "Directory" ? plan.TreePlan!.CopyPlan.EntryCount : plan is null ? null : 1,
            plan?.Kind == "Directory" ? plan.TreePlan!.CopyPlan.TotalBytes : plan?.SourceIdentity.SizeBytes, row.ReadyExpiresAtUtc, row.ResultCode, row.Version,
            row.PublishedVersion, row.PrivateMayExist, confirmation, row.DestinationPublished, row.SourceRemoved, row.SourceRetained);
    }
}
