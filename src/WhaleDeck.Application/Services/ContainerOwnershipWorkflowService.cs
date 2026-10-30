using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

/// <summary>HTTP-sized transitions only. Metadata attestation and UID/GID changes belong to the dispatcher.</summary>
public sealed class ContainerOwnershipWorkflowService(ContainerFileUploadService authorization, IContainerOwnershipRepository repository,
    IContainerOwnershipProtector protector, IContainerOwnershipGateway gateway, TimeProvider clock)
{
    public Task AuthorizeAsync(TerminalActor actor, CancellationToken cancellationToken) => authorization.AuthorizeAsync(actor, cancellationToken);
    public async Task<ContainerOwnershipIntentDto> QueueAsync(TerminalActor actor, ContainerOwnershipCommandDto command, string key, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken); ContainerOwnershipValidation.Command(command);
        var idempotency = ContainerOwnershipWorkflowValidation.Key(key);
        var id = ContainerOwnershipWorkflowValidation.IntentId(actor.Subject, actor.LoginBinding, idempotency);
        var prior = await repository.OwnedAsync(actor, id, cancellationToken);
        if (prior is not null)
        {
            Validate(prior);
            if (ContainerOwnershipWorkflowValidation.ReadCommand(prior) != command) throw new InvalidOperationException("CONTAINER_OWNERSHIP_IDEMPOTENCY_CONFLICT");
            return Map(prior);
        }
        await gateway.RequireContainerOwnershipWorkflowAsync(cancellationToken); await AuthorizeAsync(actor, cancellationToken);
        var now = ContainerOwnershipWorkflowValidation.Time(clock.GetUtcNow());
        var binding = new ContainerOwnershipRequestBinding(id, idempotency, actor.Subject, actor.LoginBinding, actor.TraceId,
            ContainerOwnershipWorkflowValidation.Owner(actor.Subject, actor.LoginBinding), command, now, now.AddMinutes(15));
        var row = new ContainerOwnershipIntent { Id = id, IdempotencyKey = idempotency, ActorSubject = actor.Subject, LoginBinding = actor.LoginBinding,
            TraceId = actor.TraceId, OwnerHash = binding.Owner, ContainerId = command.Location.ContainerId, InstanceId = command.Location.InstanceId,
            RelativePath = command.Location.Path, CommandJson = ContainerOwnershipWorkflowValidation.Serialize(command),
            RequestHash = protector.FingerprintOwnershipRequest(binding), CreatedAtUtc = now, PlanningExpiresAtUtc = binding.PlanningExpiresAtUtc, NextAttemptAtUtc = now };
        return Map(await repository.EnqueueAsync(row, cancellationToken));
    }
    public async Task<ContainerOwnershipIntentDto> GetAsync(TerminalActor actor, string containerId, Guid id, CancellationToken cancellationToken)
    { await AuthorizeAsync(actor, cancellationToken); return Map(await Owned(actor, containerId, id, cancellationToken)); }
    public Task<ContainerOwnershipIntentDto> GetByKeyAsync(TerminalActor actor, string containerId, string key, CancellationToken cancellationToken) =>
        GetAsync(actor, containerId, ContainerOwnershipWorkflowValidation.IntentId(actor.Subject, actor.LoginBinding, ContainerOwnershipWorkflowValidation.Key(key)), cancellationToken);
    public async Task<ContainerOwnershipIntentDto> ConfirmAsync(TerminalActor actor, string containerId, Guid id, ConfirmContainerOwnershipCommand command, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        if (command is null || !command.Confirmed || command.Version < 0) throw new ArgumentException("CONTAINER_OWNERSHIP_CONFIRMATION_REQUIRED");
        var row = await Owned(actor, containerId, id, cancellationToken);
        var claims = protector.VerifyOwnershipConfirmation(command.PlanHash); ContainerOwnershipWorkflowValidation.Confirmation(claims);
        if (claims.Id != id || claims.Actor != actor.Subject || claims.LoginBinding != actor.LoginBinding) throw new UnauthorizedAccessException("CONTAINER_OWNERSHIP_PLAN_OWNER_REQUIRED");
        if (row.PlanHash != claims.PlanHash || row.ReadyExpiresAtUtc != claims.ExpiresAtUtc) throw new InvalidOperationException("CONTAINER_OWNERSHIP_PLAN_CHANGED");
        await AuthorizeAsync(actor, cancellationToken);
        return Map(await repository.ConfirmAsync(actor, claims, command.Version, cancellationToken));
    }
    public async Task<ContainerOwnershipIntentDto> CancelAsync(TerminalActor actor, string containerId, Guid id, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken); _ = await Owned(actor, containerId, id, cancellationToken);
        await repository.CancelAsync(actor, id, cancellationToken); return Map(await Owned(actor, containerId, id, cancellationToken));
    }
    public async Task<IReadOnlyList<ContainerOwnershipIntentDto>> UnknownAsync(TerminalActor actor, int take, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        if (take is < 1 or > 50) throw new ArgumentException("CONTAINER_OWNERSHIP_PAGE_INVALID");
        var rows = await repository.UnknownAsync(take, cancellationToken); return rows.Select(Map).ToArray();
    }
    public async Task<ContainerOwnershipIntentDto> RecoveryGetAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        if (id == Guid.Empty) throw new ArgumentException("CONTAINER_OWNERSHIP_ID_REQUIRED");
        return Map(await repository.FindAsync(id, cancellationToken) ?? throw new KeyNotFoundException("CONTAINER_OWNERSHIP_NOT_FOUND"));
    }
    public async Task AcknowledgeAsync(TerminalActor actor, Guid id, AcknowledgeContainerOwnershipCommand command, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        if (id == Guid.Empty || command is null || !command.Confirmed || command.Version < 0) throw new ArgumentException("CONTAINER_OWNERSHIP_CONFIRMATION_REQUIRED");
        var row = await repository.FindAsync(id, cancellationToken) ?? throw new KeyNotFoundException("CONTAINER_OWNERSHIP_NOT_FOUND"); Validate(row);
        if (row.State != "Unknown" || row.ExecutionJobId is null || row.CompletedAtUtc is not null || row.Version != command.Version)
            throw new InvalidOperationException("CONTAINER_OWNERSHIP_ACKNOWLEDGEMENT_CONFLICT");
        var operation = ContainerOwnershipWorkflowValidation.Reference(row);
        // Discard is the permanent Root fence for the ORIGINAL execution ID.
        // Unknown may retain HDD metadata or a changed ownership; acknowledgement never restores ownership or asserts success.
        var receipt = await gateway.DiscardContainerOwnershipAsync(operation, cancellationToken); ContainerOwnershipValidation.Receipt(operation, receipt);
        await AuthorizeAsync(actor, cancellationToken);
        await repository.AcknowledgeAsync(actor, id, command.Version, receipt, cancellationToken);
    }
    private async Task<ContainerOwnershipIntent> Owned(TerminalActor actor, string containerId, Guid id, CancellationToken cancellationToken)
    {
        ContainerFileValidation.Id(containerId); if (id == Guid.Empty) throw new ArgumentException("CONTAINER_OWNERSHIP_ID_REQUIRED");
        var row = await repository.OwnedAsync(actor, id, cancellationToken) ?? throw new KeyNotFoundException("CONTAINER_OWNERSHIP_NOT_FOUND");
        if (row.ContainerId != containerId) throw new KeyNotFoundException("CONTAINER_OWNERSHIP_NOT_FOUND"); Validate(row); return row;
    }
    private void Validate(ContainerOwnershipIntent row)
    { if (!ContainerOwnershipWorkflowValidation.HasValidBinding(row, protector)) throw new InvalidOperationException("CONTAINER_OWNERSHIP_BINDING_CHANGED"); }
    private ContainerOwnershipIntentDto Map(ContainerOwnershipIntent row)
    {
        Validate(row); var plan = row.PlanJson is null ? null : ContainerOwnershipWorkflowValidation.ReadPlan(row);
        var ready = row.State == "Ready" && row.CancelRequestedAtUtc is null && row.ReadyExpiresAtUtc > clock.GetUtcNow();
        var confirmation = ready ? new ContainerOwnershipConfirmationDto(protector.SignOwnershipConfirmation(new(row.Id, row.ActorSubject, row.LoginBinding, row.PlanHash!, row.ReadyExpiresAtUtc!.Value)),
            row.ReadyExpiresAtUtc.Value, "预检不修改所有者。确认后校验原容器实例、原inode版本及属性，先保留HDD元数据，再仅修改当前对象的UID/GID；不递归、不修改模式或ACL，目录子项保持。原文件属主与容器执行身份分别展示。断线后查询原任务，未知结果保留保护，不自动重做或恢复所有者。") : null;
        return new(row.Id, row.Id, row.ExecutionJobId, ContainerOwnershipWorkflowValidation.ReadCommand(row), row.State == "Ready" && !ready ? "Expired" : row.State,
            plan?.Kind, plan?.SourceIdentity.OwnerUid, plan?.SourceIdentity.OwnerGid, plan?.NoChange, row.ReadyExpiresAtUtc, row.ResultCode, row.Version,
            row.PublishedVersion, row.OwnerConfirmed, row.HistoryMayExist, row.HistoryVerified, row.HistoryDigest, confirmation);
    }
}
