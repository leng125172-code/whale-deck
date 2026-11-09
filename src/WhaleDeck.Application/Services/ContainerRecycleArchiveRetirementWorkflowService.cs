using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;
using Retirement = WhaleDeck.Application.Services.ContainerRecycleArchiveRetirementWorkflowValidation;

namespace WhaleDeck.Application.Services;

public sealed class ContainerRecycleArchiveRetirementWorkflowService(IContainerRecycleArchiveRetirementRepository repository,
    IContainerRecycleArchiveRepository archives, IContainerRecycleArchiveProtector archiveProtector, IContainerRecycleArchiveRetirementProtector protector,
    IContainerRecycleArchiveRetirementGateway gateway, ContainerRecycleArchiveRetirementSourceResolver sources,
    ContainerRecycleArchiveWorkflowService authorization, TimeProvider clock)
{
    public async Task<ContainerRecycleArchiveRetirementIntentDto> QueueAsync(TerminalActor actor, string containerId, Guid archiveId,
        uint retainDays, string key, CancellationToken token)
    {
        await authorization.AuthorizeAsync(actor, token);
        ContainerFileValidation.Id(containerId);
        if (archiveId == Guid.Empty || retainDays is < 1 or > 3650) throw new ArgumentException(Retirement.Prefix + "SOURCE_REQUIRED");
        var idempotency = Key(key);
        var id = Retirement.IntentId(actor.Subject, actor.LoginBinding, idempotency);
        if (await repository.FindAsync(id, token) is { } prior)
        {
            await RequireOwned(prior, actor, containerId, token);
            if (prior.ArchiveIntentId != archiveId || prior.RetainDays != retainDays)
                throw new InvalidOperationException(Retirement.Prefix + "IDEMPOTENCY_CONFLICT");
            return Map(prior);
        }
        await gateway.RequireContainerRecycleArchiveRetirementAsync(token);
        var source = await sources.ResolveAsync(actor, containerId, archiveId, retainDays, token);
        var now = Retirement.Time(clock.GetUtcNow());
        var row = new ContainerRecycleArchiveRetirementIntent
        {
            Id = id, ArchiveIntentId = archiveId, RetainDays = checked((int)retainDays), SourceProofHash = source.ProofHash, IdempotencyKey = idempotency,
            ActorSubject = actor.Subject, LoginBinding = actor.LoginBinding, TraceId = actor.TraceId, OwnerHash = Retirement.Owner(actor.Subject, actor.LoginBinding),
            CreatedAtUtc = now, PlanningExpiresAtUtc = now.AddMinutes(15), NextAttemptAtUtc = now, RequestHash = ""
        };
        var queued = await repository.EnqueueAsync(new()
        {
            Id = row.Id, ArchiveIntentId = row.ArchiveIntentId, RetainDays = row.RetainDays, SourceProofHash = row.SourceProofHash, IdempotencyKey = row.IdempotencyKey,
            ActorSubject = row.ActorSubject, LoginBinding = row.LoginBinding, TraceId = row.TraceId, OwnerHash = row.OwnerHash,
            CreatedAtUtc = row.CreatedAtUtc, PlanningExpiresAtUtc = row.PlanningExpiresAtUtc, NextAttemptAtUtc = row.NextAttemptAtUtc,
            RequestHash = protector.FingerprintRetirementRequest(row)
        }, token);
        await authorization.AuthorizeAsync(actor, token);
        return Map(queued);
    }

    public async Task<ContainerRecycleArchiveRetirementIntentDto> GetAsync(TerminalActor actor, string containerId, Guid id, CancellationToken token)
    {
        await authorization.AuthorizeAsync(actor, token);
        var row = await Load(id, token);
        await RequireOwned(row, actor, containerId, token);
        return Map(row);
    }

    public Task<ContainerRecycleArchiveRetirementIntentDto> GetByKeyAsync(TerminalActor actor, string containerId, string key, CancellationToken token) =>
        GetAsync(actor, containerId, Retirement.IntentId(actor.Subject, actor.LoginBinding, Key(key)), token);

    public async Task<ContainerRecycleArchiveRetirementIntentDto> ConfirmAsync(TerminalActor actor, string containerId, Guid id, string confirmation,
        long version, bool confirmed, CancellationToken token)
    {
        await authorization.AuthorizeAsync(actor, token);
        if (!confirmed || version < 0) throw new ArgumentException(Retirement.Prefix + "CONFIRMATION_REQUIRED");
        var row = await Load(id, token);
        await RequireOwned(row, actor, containerId, token);
        var claims = protector.VerifyRetirementConfirmation(confirmation);
        if (claims.Id != id || claims.Actor != actor.Subject || claims.LoginBinding != actor.LoginBinding ||
            claims.PlanHash != row.PlanHash || claims.ExpiresAtUtc != row.ReadyExpiresAtUtc)
            throw new InvalidOperationException(Retirement.Prefix + "PLAN_CHANGED");
        if (row.ExecutionJobId is null)
        {
            await gateway.RequireContainerRecycleArchiveRetirementAsync(token);
            await RequireCurrentSourceAsync(row, token);
        }
        var result = await repository.ConfirmAsync(actor, confirmation, version, token);
        await authorization.AuthorizeAsync(actor, token);
        return Map(result);
    }

    public async Task CancelAsync(TerminalActor actor, string containerId, Guid id, CancellationToken token)
    {
        await authorization.AuthorizeAsync(actor, token);
        await RequireOwned(await Load(id, token), actor, containerId, token);
        await repository.CancelAsync(actor, id, token);
    }

    public async Task<ContainerRecycleArchiveRetirementIntentDto> GetForRecoveryAsync(TerminalActor actor, string containerId, Guid id, CancellationToken token)
    {
        await authorization.AuthorizeAsync(actor, token);
        var row = await Load(id, token);
        await RequireContainer(row, containerId, token);
        await authorization.AuthorizeAsync(actor, token);
        // The recovery administrator can see facts, never the owner's token.
        return Map(row, includeConfirmation: false);
    }

    public async Task AcknowledgeAsync(TerminalActor actor, string containerId, Guid id, long version, bool confirmed, CancellationToken token)
    {
        await authorization.AuthorizeAsync(actor, token);
        if (!confirmed || version < 0) throw new ArgumentException(Retirement.Prefix + "CONFIRMATION_REQUIRED");
        var row = await Load(id, token);
        await RequireContainer(row, containerId, token);
        if (row.State != "Unknown" || row.Version != version || !row.PublishBarrier)
            throw new InvalidOperationException(Retirement.Prefix + "ACKNOWLEDGEMENT_CONFLICT");
        await gateway.RequireContainerRecycleArchiveRetirementAsync(token);
        var operation = Retirement.Operation(row);
        await authorization.AuthorizeAsync(actor, token);
        var observed = await gateway.InspectContainerRecycleArchiveRetirementAsync(operation, token);
        if (observed is null or { State: "Reserved" })
        {
            await authorization.AuthorizeAsync(actor, token);
            // Discard is scoped to the existing execution. It can settle
            // scheduling metadata but cannot delete the retained payload.
            observed = await gateway.DiscardContainerRecycleArchiveRetirementAsync(operation, token);
        }
        await authorization.AuthorizeAsync(actor, token);
        await repository.AcknowledgeAsync(actor, id, version, observed, token);
    }

    internal async Task<ResolvedContainerRecycleArchiveRetirementSource> RequireCurrentSourceAsync(ContainerRecycleArchiveRetirementIntent row, CancellationToken token)
    {
        Retirement.Verify(row, protector);
        var actor = Actor(row);
        await authorization.AuthorizeAsync(actor, token);
        var archive = await FindArchive(row.ArchiveIntentId, token);
        var source = await sources.ResolveAsync(actor, archive.ContainerId, archive.Id, checked((uint)row.RetainDays), token);
        if (!Retirement.EqualMac(source.ProofHash, row.SourceProofHash))
            throw new InvalidOperationException(Retirement.Prefix + "SOURCE_CHANGED");
        if (row.PlanJson is not null) ContainerRecycleArchiveRetirementSourceResolver.RequireCurrentPlan(source, Retirement.ReadPlan(row));
        return source;
    }

    internal static TerminalActor Actor(ContainerRecycleArchiveRetirementIntent row) => new(row.ActorSubject, null, [], row.LoginBinding, row.TraceId, null);

    private async Task<ContainerRecycleArchiveRetirementIntent> Load(Guid id, CancellationToken token)
    {
        if (id == Guid.Empty) throw Missing();
        var row = await repository.FindAsync(id, token) ?? throw Missing();
        Retirement.Verify(row, protector);
        return row;
    }

    private async Task RequireOwned(ContainerRecycleArchiveRetirementIntent row, TerminalActor actor, string containerId, CancellationToken token)
    {
        if (row.ActorSubject != actor.Subject || row.LoginBinding != actor.LoginBinding) throw Missing();
        Retirement.Verify(row, protector);
        await RequireContainer(row, containerId, token);
        await authorization.AuthorizeAsync(actor, token);
    }

    private async Task RequireContainer(ContainerRecycleArchiveRetirementIntent row, string containerId, CancellationToken token)
    {
        ContainerFileValidation.Id(containerId);
        if ((await FindArchive(row.ArchiveIntentId, token)).ContainerId != containerId) throw Missing();
    }

    private async Task<ContainerRecycleArchiveIntent> FindArchive(Guid id, CancellationToken token)
    {
        var archive = await archives.FindAsync(id, token) ?? throw Missing();
        if (archive.Id != id || !ContainerRecycleArchiveWorkflowValidation.HasValidBinding(archive, archiveProtector)) throw Retirement.Invalid();
        return archive;
    }

    private ContainerRecycleArchiveRetirementIntentDto Map(ContainerRecycleArchiveRetirementIntent row, bool includeConfirmation = true)
    {
        Retirement.Verify(row, protector);
        var plan = row.PlanJson is null ? null : Retirement.ReadPlan(row);
        var now = clock.GetUtcNow();
        var confirmation = includeConfirmation && row.State == "Ready" && row.CancelRequestedAtUtc is null && row.ReadyExpiresAtUtc > now &&
            plan is { RetirementEligible: true } && plan.NotBeforeUnixMs <= now.ToUnixTimeMilliseconds()
            ? new ContainerRecycleArchiveRetirementConfirmationDto(protector.SignRetirementConfirmation(
                new(row.Id, row.ActorSubject, row.LoginBinding, row.PlanHash!, row.ReadyExpiresAtUtc!.Value)), row.ReadyExpiresAtUtc.Value)
            : null;
        return new(row.Id, row.Id, row.ExecutionJobId, row.ArchiveIntentId, row.RetainDays, row.State, row.Version, row.ReadyExpiresAtUtc,
            plan?.NotBeforeUnixMs, plan?.ArchiveBytes, plan?.RetirementEligible, row.ReservationJson is not null, row.ApplyStartedAtUtc is not null,
            row.PublishBarrier, row.RetirementApplied, row.RootState, row.ResultCode, confirmation);
    }

    private static Guid Key(string key)
    {
        if (!Guid.TryParseExact(key, "D", out var value) || value == Guid.Empty || value.ToString("D") != key)
            throw new ArgumentException(Retirement.Prefix + "IDEMPOTENCY_KEY_REQUIRED");
        return value;
    }
    private static KeyNotFoundException Missing() => new(Retirement.Prefix + "NOT_FOUND");
}
