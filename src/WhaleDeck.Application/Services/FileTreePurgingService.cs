using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

public sealed class FileTreePurgingService(FileManagementService authorization, IFileTreePurgingRepository repository,
    IFileTreePurgingProtector protector, IFileTreePurgingGateway gateway, TimeProvider clock)
{
    public async Task<FileTreePurgingIntentDto> QueueAsync(TerminalActor actor, CreateFileTreePurgingCommand request, string key, CancellationToken cancellationToken)
    {
        await authorization.AuthorizeAsync(actor, cancellationToken); FileTreePurgingValidation.Request(request); FileTreePurgingValidation.IntentId(actor, key);
        var row = await repository.QueueAsync(actor, key, request, cancellationToken);
        var command = FileTreePurgingValidation.ReadCommand(row);
        if (row.Id != FileTreePurgingValidation.IntentId(actor, key) || row.ActorSubject != actor.Subject || row.LoginBinding != actor.LoginBinding ||
            command.CaptureId != request.CaptureId || command.Scope != request.Scope) throw new InvalidOperationException("FILES_INTENT_BINDING_CHANGED");
        return Map(row);
    }
    public async Task<FileTreePurgingIntentDto> GetAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken)
    { await authorization.AuthorizeAsync(actor, cancellationToken); return Map(await Owned(actor, id, cancellationToken)); }
    public Task<FileTreePurgingIntentDto> GetByKeyAsync(TerminalActor actor, string key, CancellationToken cancellationToken) =>
        GetAsync(actor, FileTreePurgingValidation.IntentId(actor, key), cancellationToken);
    public async Task<FileTreePurgingIntentDto> ConfirmAsync(TerminalActor actor, Guid id, ConfirmFileTreePurgingCommand request, CancellationToken cancellationToken)
    {
        await authorization.AuthorizeAsync(actor, cancellationToken);
        if (request is null || !request.Confirmed || request.Version < 0) throw new ArgumentException("FILES_CONFIRMATION_REQUIRED");
        var claims = protector.VerifyPurgingConfirmation(request.PlanHash);
        if (claims.Id != id || claims.Actor != actor.Subject || claims.LoginBinding != actor.LoginBinding) throw new UnauthorizedAccessException("FILES_PLAN_OWNER_REQUIRED");
        var row = await Owned(actor, id, cancellationToken); var command = FileTreePurgingValidation.ReadCommand(row);
        if (claims.CaptureId != command.CaptureId || claims.Scope != command.Scope || claims.Fingerprint != FileTreePurgingValidation.ReadPlan(row).Fingerprint ||
            claims.ExpiresAtUtc != row.ReadyExpiresAtUtc) throw new InvalidOperationException("FILES_INTENT_BINDING_CHANGED");
        if (row.ExecutionJobId is null && (row.State != "Ready" || row.CancelRequestedAtUtc is not null || row.ReadyExpiresAtUtc <= clock.GetUtcNow()))
            throw new InvalidOperationException("FILES_PLAN_EXPIRED_OR_CANCELED");
        if (row.ExecutionJobId is null && row.Version != request.Version) throw new InvalidOperationException("FILES_CONFIRMATION_CONFLICT");
        // The repository must validate Ready TTL/version and acquire the same
        // capture/hierarchy locks as restores in its confirmation transaction.
        return Map(await repository.ConfirmAsync(actor, claims, request.Version, cancellationToken));
    }
    public async Task<FileTreePurgingIntentDto> CancelAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken)
    {
        await authorization.AuthorizeAsync(actor, cancellationToken); await Owned(actor, id, cancellationToken);
        await repository.CancelAsync(actor, id, cancellationToken); return Map(await Owned(actor, id, cancellationToken));
    }
    public async Task AcknowledgeAsync(TerminalActor actor, Guid id, AcknowledgeFileMutationCommand request, CancellationToken cancellationToken)
    {
        await authorization.AuthorizeAsync(actor, cancellationToken);
        if (request is null || !request.Confirmed) throw new ArgumentException("FILES_CONFIRMATION_REQUIRED");
        var row = await Owned(actor, id, cancellationToken);
        if (row.State != "Unknown" || row.Version != request.Version) throw new InvalidOperationException("FILES_ACKNOWLEDGMENT_CONFLICT");
        var receipt = await gateway.DiscardTreePurgingAsync(row, cancellationToken);
        FileTreePurgingValidation.Receipt(row.ExecutionJobId!.Value, FileTreePurgingValidation.ReadPlan(row), receipt);
        if (row.ReceiptJson is not null) FileTreePurgingValidation.MonotonicReceipt(FileTreePurgingValidation.ReadReceipt(row), receipt);
        if (receipt.State == "Completed") throw new InvalidOperationException("FILES_RECEIPT_AVAILABLE");
        await authorization.AuthorizeAsync(actor, cancellationToken);
        await repository.AcknowledgeAsync(actor, id, request.Version, receipt, cancellationToken);
    }
    private async Task<FileTreePurgingOperation> Owned(TerminalActor actor, Guid id, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty) throw new ArgumentException("FILES_OPERATION_ID_REQUIRED");
        var row = await repository.OwnedAsync(actor, id, cancellationToken) ?? throw new KeyNotFoundException("FILES_OPERATION_NOT_FOUND");
        if (row.ActorSubject != actor.Subject || row.LoginBinding != actor.LoginBinding || !FileTreePurgingValidation.HasValidBinding(row, protector))
            throw new InvalidOperationException("FILES_INTENT_BINDING_CHANGED");
        return row;
    }
    private FileTreePurgingIntentDto Map(FileTreePurgingOperation row)
    {
        if (!FileTreePurgingValidation.HasValidBinding(row, protector)) throw new InvalidOperationException("FILES_INTENT_BINDING_CHANGED");
        var command = FileTreePurgingValidation.ReadCommand(row); var plan = row.PlanJson is null ? null : FileTreePurgingValidation.ReadPlan(row);
        var ready = row.State == "Ready" && row.CancelRequestedAtUtc is null && row.ReadyExpiresAtUtc > clock.GetUtcNow();
        var confirmation = ready ? new FileTreePurgingConfirmationDto(protector.SignConfirmation(new(row.Id, row.ActorSubject, row.LoginBinding,
            command.CaptureId, command.Scope, plan!.Fingerprint, row.ReadyExpiresAtUtc!.Value)), row.ReadyExpiresAtUtc.Value, Warning(command.Scope)) : null;
        // RootStarted is only an admission fact, not evidence that deletion did
        // or did not happen. A lost result remains nullable, never false-success.
        var facts = FileTreePurgingValidation.RecoverySummary(row, protector);
        return new(row.Id, row.Id, row.ExecutionJobId, row.State == "Ready" && !ready ? "Expired" : row.State,
            new(command.CaptureId, command.Source, command.CaptureKind, command.Scope), plan?.EntryCount, plan?.TotalBytes, plan?.ArchiveBytes,
            row.ReadyExpiresAtUtc, row.ResultCode, row.Version, facts.DeletionStarted,
            facts.ArchiveRemoved, facts.OriginalsRemoved, confirmation);
    }
    private static string Warning(string scope) => scope switch
    {
        "Archive" => "将永久删除此捕获的HDD归档，不可撤销；不会检查或删除保留原件，原件可能已经不可用。原捕获记录和审计保留。结果未知时不自动续删。",
        "Originals" => "将永久删除此捕获的保留原件，不可撤销；若归档已被清理，这可能删除最后副本。未清理的归档会先完整验证。原记录和审计保留，结果未知不自动续删。",
        "All" => "将永久删除此捕获的保留原件和HDD归档，可能移除全部可恢复副本，不可撤销。原件先清理，归档最后清理；原记录和审计保留，结果未知不自动续删。",
        _ => throw new ArgumentException("FILES_TREE_PURGE_SCOPE_INVALID")
    };
}
