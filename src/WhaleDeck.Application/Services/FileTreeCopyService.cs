using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

public sealed class FileTreeCopyService(FileManagementService authorization, IFileTreeCopyRepository repository,
    IFileTreeCopyProtector protector, IFileTreeCopyGateway gateway, TimeProvider clock)
{
    public async Task<FileTreeCopyIntentDto> QueueAsync(TerminalActor actor, FileTreeCopyCommand command, string key, CancellationToken cancellationToken)
    {
        await authorization.AuthorizeAsync(actor, cancellationToken); FileTreeCopyValidation.Command(command); FileTreeCopyValidation.IntentId(actor, key);
        return Map(await repository.QueueAsync(actor, key, command, cancellationToken));
    }
    public async Task<FileTreeCopyIntentDto> GetAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken)
    { await authorization.AuthorizeAsync(actor, cancellationToken); return Map(await Owned(actor, id, cancellationToken)); }
    public Task<FileTreeCopyIntentDto> GetByKeyAsync(TerminalActor actor, string key, CancellationToken cancellationToken) =>
        GetAsync(actor, FileTreeCopyValidation.IntentId(actor, key), cancellationToken);
    public async Task<FileTreeCopyIntentDto> ConfirmAsync(TerminalActor actor, Guid id, ConfirmFileTreeCopyCommand command, CancellationToken cancellationToken)
    {
        await authorization.AuthorizeAsync(actor, cancellationToken);
        if (!command.Confirmed || command.Version < 0) throw new ArgumentException("FILES_CONFIRMATION_REQUIRED");
        var claims = protector.VerifyConfirmation(command.PlanHash);
        if (claims.Id != id || claims.Actor != actor.Subject || claims.LoginBinding != actor.LoginBinding) throw new UnauthorizedAccessException("FILES_PLAN_OWNER_REQUIRED");
        return Map(await repository.ConfirmAsync(actor, claims, command.Version, cancellationToken));
    }
    public async Task<FileTreeCopyIntentDto> CancelAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken)
    {
        await authorization.AuthorizeAsync(actor, cancellationToken); await Owned(actor, id, cancellationToken);
        await repository.CancelAsync(actor, id, cancellationToken); return Map(await Owned(actor, id, cancellationToken));
    }
    public async Task AcknowledgeAsync(TerminalActor actor, Guid id, AcknowledgeFileMutationCommand command, CancellationToken cancellationToken)
    {
        await authorization.AuthorizeAsync(actor, cancellationToken);
        if (!command.Confirmed) throw new ArgumentException("FILES_CONFIRMATION_REQUIRED");
        var row = await Owned(actor, id, cancellationToken);
        if (row.State != "Unknown" || row.Version != command.Version) throw new InvalidOperationException("FILES_ACKNOWLEDGMENT_CONFLICT");
        var receipt = await gateway.DiscardTreeCopyAsync(row, cancellationToken);
        FileTreeCopyValidation.Receipt(row.ExecutionJobId!.Value, FileTreeCopyValidation.ReadPlan(row), receipt);
        if (receipt.State == "Completed") throw new InvalidOperationException("FILES_RECEIPT_AVAILABLE");
        await authorization.AuthorizeAsync(actor, cancellationToken); await repository.AcknowledgeAsync(actor, id, command.Version, receipt, cancellationToken);
    }
    private async Task<FileTreeCopyOperation> Owned(TerminalActor actor, Guid id, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty) throw new ArgumentException("FILES_OPERATION_ID_REQUIRED");
        var row = await repository.OwnedAsync(actor, id, cancellationToken) ?? throw new KeyNotFoundException("FILES_OPERATION_NOT_FOUND");
        if (!FileTreeCopyValidation.HasValidBinding(row, protector)) throw new InvalidOperationException("FILES_INTENT_BINDING_CHANGED"); return row;
    }
    private FileTreeCopyIntentDto Map(FileTreeCopyOperation row)
    {
        if (!FileTreeCopyValidation.HasValidBinding(row, protector)) throw new InvalidOperationException("FILES_INTENT_BINDING_CHANGED");
        var plan = row.PlanJson is null ? null : FileTreeCopyValidation.ReadPlan(row);
        var ready = row.State == "Ready" && row.CancelRequestedAtUtc is null && row.ReadyExpiresAtUtc > clock.GetUtcNow();
        var confirmation = ready ? new FileTreeCopyConfirmationDto(protector.SignConfirmation(new(row.Id, row.ActorSubject, row.LoginBinding, plan!.Fingerprint, row.ReadyExpiresAtUtc!.Value)),
            row.ReadyExpiresAtUtc.Value, "完整清单已验证，但尚未复制。确认后会再次校验完整源树，只创建不存在的目标，不覆盖；源保留。原任务断线后不自动重做，未知或私有部分副本保留待处置。") : null;
        return new(row.Id, row.Id, row.ExecutionJobId, row.State == "Ready" && !ready ? "Expired" : row.State, FileTreeCopyValidation.ReadCommand(row),
            plan?.EntryCount, plan?.TotalBytes, row.ReadyExpiresAtUtc, row.ResultCode, row.Version, row.PublishedVersion, row.PartialRetained, confirmation);
    }
}
