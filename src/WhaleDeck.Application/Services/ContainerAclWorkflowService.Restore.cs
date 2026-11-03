using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

public sealed partial class ContainerAclWorkflowService
{
    public Task<ContainerAclRestoreIntentDto> GetRestoreByKeyAsync(TerminalActor actor, string containerId, string key, CancellationToken cancellationToken) =>
        GetRestoreAsync(actor, containerId, ContainerAclWorkflowValidation.IntentId(actor.Subject, actor.LoginBinding, ContainerAclWorkflowValidation.Key(key)), cancellationToken);

    public async Task<ContainerAclRestoreIntentDto> QueueRestoreAsync(TerminalActor actor, ContainerAclRestoreCommand command,
        string key, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken); ContainerAclRestoreValidation.Command(command);
        var idempotency = ContainerAclWorkflowValidation.Key(key);
        var id = ContainerAclWorkflowValidation.IntentId(actor.Subject, actor.LoginBinding, idempotency);
        var prior = await repository.OwnedAsync(actor, id, cancellationToken);
        if (prior is not null)
        {
            Validate(prior); var saved = ContainerAclRestoreValidation.Read(prior);
            if (saved is null || saved.Location != command.Location || saved.Entry.CaptureId != command.CaptureId || saved.Entry.HistoryDigest != command.ExpectedHistoryDigest ||
                ContainerAclWorkflowValidation.ReadCommand(prior).ExpectedVersion != command.ExpectedVersion) throw new InvalidOperationException("CONTAINER_ACL_IDEMPOTENCY_CONFLICT");
            await AuthorizeAsync(actor, cancellationToken); return new(Map(prior), saved);
        }
        var source = await ReadSource(actor, command.Location, command.CaptureId, command.ExpectedHistoryDigest, cancellationToken);
        var acl = ContainerAclWorkflowValidation.SnapshotCommand(new(command.Location, source.Entry.ChangedAclType,
            source.Entry.ChangedAclType == "access" ? source.AccessAcl : source.DefaultAcl, command.ExpectedVersion));
        ContainerAclRestoreValidation.Source(source, acl);
        await gateway.RequireContainerAclWorkflowAsync(cancellationToken); await AuthorizeAsync(actor, cancellationToken);
        var now = ContainerAclWorkflowValidation.Time(clock.GetUtcNow());
        var binding = new ContainerAclRequestBinding(id, idempotency, actor.Subject, actor.LoginBinding, actor.TraceId,
            ContainerAclWorkflowValidation.Owner(actor.Subject, actor.LoginBinding), acl, now, now.AddMinutes(15), source);
        var row = new ContainerAclIntent { Id=id, IdempotencyKey=idempotency, ActorSubject=actor.Subject, LoginBinding=actor.LoginBinding,
            TraceId=actor.TraceId, OwnerHash=binding.Owner, ContainerId=acl.Location.ContainerId, InstanceId=acl.Location.InstanceId, RelativePath=acl.Location.Path,
            CommandJson=ContainerAclWorkflowValidation.Serialize(acl), RestoreSourceJson=ContainerAclWorkflowValidation.Serialize(source),
            RequestHash=protector.FingerprintAclRequest(binding), CreatedAtUtc=now, PlanningExpiresAtUtc=binding.PlanningExpiresAtUtc, NextAttemptAtUtc=now };
        var queued = await repository.EnqueueAsync(row, cancellationToken);
        await AuthorizeAsync(actor, cancellationToken); Validate(queued);
        var savedSource = ContainerAclRestoreValidation.Read(queued) ?? throw ContainerAclWorkflowValidation.Invalid();
        if (!ContainerAclRestoreValidation.Same(source, savedSource)) throw new InvalidOperationException("CONTAINER_ACL_IDEMPOTENCY_CONFLICT");
        return new(Map(queued), savedSource);
    }

    public async Task<ContainerAclRestoreIntentDto> GetRestoreAsync(TerminalActor actor, string containerId, Guid id, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken); var row = await Owned(actor, containerId, id, cancellationToken);
        var source = ContainerAclRestoreValidation.Read(row) ?? throw new KeyNotFoundException("CONTAINER_ACL_RESTORE_NOT_FOUND");
        await AuthorizeAsync(actor, cancellationToken); return new(Map(row), source);
    }

    /// <summary>Stored immutable metadata for current administrators, including after acknowledgement. Never rereads HDD or authorizes a write.</summary>
    public async Task<ContainerAclRestoreSourceSummaryDto?> RecoveryRestoreSourceAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        if (id == Guid.Empty) throw new ArgumentException("CONTAINER_ACL_ID_REQUIRED");
        var row = await repository.FindAsync(id, cancellationToken) ?? throw new KeyNotFoundException("CONTAINER_ACL_NOT_FOUND");
        Validate(row);
        var source = ContainerAclRestoreValidation.Read(row);
        var entry = source?.Entry;
        var summary = entry is null ? null : new ContainerAclRestoreSourceSummaryDto(entry.CaptureId, entry.ChangedAclType,
            entry.Kind, entry.CreatedAtUnixMs, entry.OriginalMode, entry.AccessEntryCount, entry.DefaultEntryCount);
        await AuthorizeAsync(actor, cancellationToken);
        return summary;
    }

    /// <summary>Only before a new write. Inspect/discard of the original execution never depend on source availability.</summary>
    internal async Task ValidateRestoreSourceAsync(TerminalActor actor, ContainerAclIntent row, CancellationToken cancellationToken)
    {
        var source = ContainerAclRestoreValidation.Read(row); if (source is null) return;
        var observed = await ReadSource(actor, source.Location, source.Entry.CaptureId, source.Entry.HistoryDigest, cancellationToken);
        if (!ContainerAclRestoreValidation.Same(source, observed)) throw new ContainerAclRestoreSourceChangedException();
    }
    private async Task<ContainerAclHistorySnapshotDto> ReadSource(TerminalActor actor, ContainerFileLocationDto location, string capture, string digest, CancellationToken token)
    {
        await AuthorizeAsync(actor, token);
        if (restoreSource is null) throw new InvalidOperationException("CONTAINER_ACL_RESTORE_CAPABILITY_UNAVAILABLE");
        var observed = await restoreSource.ReadContainerAclRestoreSourceAsync(actor, location, capture, digest, token);
        ContainerAclHistoryValidation.Snapshot(observed, location, capture, digest);
        var source = ContainerAclRestoreValidation.Snapshot(observed);
        await AuthorizeAsync(actor, token); return source;
    }
}
