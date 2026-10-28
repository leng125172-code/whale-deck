using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

/// <summary>HTTP-sized transitions only. Full source scans and copies belong to the dispatcher.</summary>
public sealed class ContainerCopyWorkflowService(ContainerFileUploadService authorization, IContainerCopyRepository repository,
    IContainerCopyProtector protector, IContainerCopyGateway gateway, TimeProvider clock)
{
    public Task AuthorizeAsync(TerminalActor actor, CancellationToken cancellationToken) => authorization.AuthorizeAsync(actor, cancellationToken);
    public async Task<ContainerCopyIntentDto> QueueAsync(TerminalActor actor, ContainerCopyCommandDto command, string key, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken); ContainerCopyValidation.Command(command);
        var idempotency = ContainerCopyWorkflowValidation.Key(key);
        var id = ContainerCopyWorkflowValidation.IntentId(actor.Subject, actor.LoginBinding, idempotency);
        var prior = await repository.OwnedAsync(actor, id, cancellationToken);
        if (prior is not null)
        {
            Validate(prior);
            if (ContainerCopyWorkflowValidation.ReadCommand(prior) != command) throw new InvalidOperationException("CONTAINER_COPY_IDEMPOTENCY_CONFLICT");
            return Map(prior);
        }
        await gateway.RequireContainerCopyWorkflowAsync(cancellationToken); await AuthorizeAsync(actor, cancellationToken);
        var now = ContainerCopyWorkflowValidation.Time(clock.GetUtcNow());
        var binding = new ContainerCopyRequestBinding(id, idempotency, actor.Subject, actor.LoginBinding, actor.TraceId,
            ContainerCopyWorkflowValidation.Owner(actor.Subject, actor.LoginBinding), command, now, now.AddMinutes(15));
        var row = new ContainerCopyIntent { Id = id, IdempotencyKey = idempotency, ActorSubject = actor.Subject, LoginBinding = actor.LoginBinding,
            TraceId = actor.TraceId, OwnerHash = binding.Owner, ContainerId = command.Source.ContainerId, InstanceId = command.Source.InstanceId,
            SourcePath = command.Source.Path, TargetPath = command.TargetPath, CommandJson = ContainerCopyWorkflowValidation.Serialize(command),
            RequestHash = protector.FingerprintCopyRequest(binding), CreatedAtUtc = now, PlanningExpiresAtUtc = binding.PlanningExpiresAtUtc, NextAttemptAtUtc = now };
        return Map(await repository.EnqueueAsync(row, cancellationToken));
    }
    public async Task<ContainerCopyIntentDto> GetAsync(TerminalActor actor, string containerId, Guid id, CancellationToken cancellationToken)
    { await AuthorizeAsync(actor, cancellationToken); return Map(await Owned(actor, containerId, id, cancellationToken)); }
    public Task<ContainerCopyIntentDto> GetByKeyAsync(TerminalActor actor, string containerId, string key, CancellationToken cancellationToken) =>
        GetAsync(actor, containerId, ContainerCopyWorkflowValidation.IntentId(actor.Subject, actor.LoginBinding, ContainerCopyWorkflowValidation.Key(key)), cancellationToken);
    public async Task<ContainerCopyIntentDto> ConfirmAsync(TerminalActor actor, string containerId, Guid id, ConfirmContainerCopyCommand command, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        if (command is null || !command.Confirmed || command.Version < 0) throw new ArgumentException("CONTAINER_COPY_CONFIRMATION_REQUIRED");
        var row = await Owned(actor, containerId, id, cancellationToken);
        var claims = protector.VerifyCopyConfirmation(command.PlanHash); ContainerCopyWorkflowValidation.Confirmation(claims);
        if (claims.Id != id || claims.Actor != actor.Subject || claims.LoginBinding != actor.LoginBinding) throw new UnauthorizedAccessException("CONTAINER_COPY_PLAN_OWNER_REQUIRED");
        if (row.PlanHash != claims.PlanHash || row.ReadyExpiresAtUtc != claims.ExpiresAtUtc) throw new InvalidOperationException("CONTAINER_COPY_PLAN_CHANGED");
        await AuthorizeAsync(actor, cancellationToken);
        return Map(await repository.ConfirmAsync(actor, claims, command.Version, cancellationToken));
    }
    public async Task<ContainerCopyIntentDto> CancelAsync(TerminalActor actor, string containerId, Guid id, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken); _ = await Owned(actor, containerId, id, cancellationToken);
        await repository.CancelAsync(actor, id, cancellationToken); return Map(await Owned(actor, containerId, id, cancellationToken));
    }
    public async Task<IReadOnlyList<ContainerCopyIntentDto>> UnknownAsync(TerminalActor actor, int take, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        if (take is < 1 or > 50) throw new ArgumentException("CONTAINER_COPY_PAGE_INVALID");
        var rows = await repository.UnknownAsync(take, cancellationToken); return rows.Select(Map).ToArray();
    }
    public async Task<ContainerCopyIntentDto> RecoveryGetAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        if (id == Guid.Empty) throw new ArgumentException("CONTAINER_COPY_ID_REQUIRED");
        return Map(await repository.FindAsync(id, cancellationToken) ?? throw new KeyNotFoundException("CONTAINER_COPY_NOT_FOUND"));
    }
    public async Task AcknowledgeAsync(TerminalActor actor, Guid id, AcknowledgeContainerCopyCommand command, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        if (id == Guid.Empty || command is null || !command.Confirmed || command.Version < 0) throw new ArgumentException("CONTAINER_COPY_CONFIRMATION_REQUIRED");
        var row = await repository.FindAsync(id, cancellationToken) ?? throw new KeyNotFoundException("CONTAINER_COPY_NOT_FOUND"); Validate(row);
        if (row.State != "Unknown" || row.ExecutionJobId is null || row.CompletedAtUtc is not null || row.Version != command.Version)
            throw new InvalidOperationException("CONTAINER_COPY_ACKNOWLEDGEMENT_CONFLICT");
        var operation = ContainerCopyWorkflowValidation.Reference(row);
        // Discard is the permanent Root fence for the ORIGINAL execution ID.
        // Unknown may retain private objects; acknowledgement never deletes them or asserts success.
        var receipt = await gateway.DiscardContainerCopyAsync(operation, cancellationToken); ContainerCopyValidation.Receipt(operation, receipt);
        await AuthorizeAsync(actor, cancellationToken);
        await repository.AcknowledgeAsync(actor, id, command.Version, receipt, cancellationToken);
    }
    private async Task<ContainerCopyIntent> Owned(TerminalActor actor, string containerId, Guid id, CancellationToken cancellationToken)
    {
        ContainerFileValidation.Id(containerId); if (id == Guid.Empty) throw new ArgumentException("CONTAINER_COPY_ID_REQUIRED");
        var row = await repository.OwnedAsync(actor, id, cancellationToken) ?? throw new KeyNotFoundException("CONTAINER_COPY_NOT_FOUND");
        if (row.ContainerId != containerId) throw new KeyNotFoundException("CONTAINER_COPY_NOT_FOUND"); Validate(row); return row;
    }
    private void Validate(ContainerCopyIntent row)
    { if (!ContainerCopyWorkflowValidation.HasValidBinding(row, protector)) throw new InvalidOperationException("CONTAINER_COPY_BINDING_CHANGED"); }
    private ContainerCopyIntentDto Map(ContainerCopyIntent row)
    {
        Validate(row); var plan = row.PlanJson is null ? null : ContainerCopyWorkflowValidation.ReadPlan(row);
        var ready = row.State == "Ready" && row.CancelRequestedAtUtc is null && row.ReadyExpiresAtUtc > clock.GetUtcNow();
        var confirmation = ready ? new ContainerCopyConfirmationDto(protector.SignCopyConfirmation(new(row.Id, row.ActorSubject, row.LoginBinding, row.PlanHash!, row.ReadyExpiresAtUtc!.Value)),
            row.ReadyExpiresAtUtc.Value, "预检未复制数据。确认后再次校验原容器实例和完整源内容，仅创建不存在的目标，保留源。断线后查询原任务；未知结果保留保护与可能存在的私有副本，不自动重做。") : null;
        return new(row.Id, row.Id, row.ExecutionJobId, ContainerCopyWorkflowValidation.ReadCommand(row), row.State == "Ready" && !ready ? "Expired" : row.State,
            plan?.Kind, plan?.Kind == "Directory" ? plan.TreePlan!.EntryCount : plan is null ? null : 1,
            plan?.Kind == "Directory" ? plan.TreePlan!.TotalBytes : plan?.SourceIdentity.SizeBytes, row.ReadyExpiresAtUtc, row.ResultCode, row.Version,
            row.PublishedVersion, row.PrivateMayExist, confirmation);
    }
}
