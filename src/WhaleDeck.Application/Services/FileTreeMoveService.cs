using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

public sealed class FileTreeMoveService(FileManagementService authorization, IFileTreeMoveRepository repository,
    IFileTreeMoveProtector protector, IFileTreeMoveGateway gateway, TimeProvider clock)
{
    public async Task<FileTreeMoveIntentDto> QueueAsync(TerminalActor actor, FileTreeCopyCommand command, string key, CancellationToken cancellationToken)
    {
        await authorization.AuthorizeAsync(actor, cancellationToken); FileTreeMoveValidation.Command(command); FileTreeMoveValidation.IntentId(actor, key);
        return Map(await repository.QueueAsync(actor, key, command, cancellationToken));
    }
    public async Task<FileTreeMoveIntentDto> GetAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken)
    { await authorization.AuthorizeAsync(actor, cancellationToken); return Map(await Owned(actor, id, cancellationToken)); }
    public Task<FileTreeMoveIntentDto> GetByKeyAsync(TerminalActor actor, string key, CancellationToken cancellationToken) =>
        GetAsync(actor, FileTreeMoveValidation.IntentId(actor, key), cancellationToken);
    public async Task<FileTreeMoveIntentDto> ConfirmAsync(TerminalActor actor, Guid id, ConfirmFileTreeCopyCommand command, CancellationToken cancellationToken)
    {
        await authorization.AuthorizeAsync(actor, cancellationToken);
        if (!command.Confirmed || command.Version < 0) throw new ArgumentException("FILES_CONFIRMATION_REQUIRED");
        var claims = protector.VerifyMoveConfirmation(command.PlanHash);
        if (claims.Id != id || claims.Actor != actor.Subject || claims.LoginBinding != actor.LoginBinding) throw new UnauthorizedAccessException("FILES_PLAN_OWNER_REQUIRED");
        return Map(await repository.ConfirmAsync(actor, claims, command.Version, cancellationToken));
    }
    public async Task<FileTreeMoveIntentDto> CancelAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken)
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
        var receipt = await gateway.DiscardTreeMoveAsync(row, cancellationToken);
        FileTreeMoveValidation.Receipt(row.ExecutionJobId!.Value, FileTreeMoveValidation.ReadPlan(row), receipt);
        if (receipt.State == "Completed") throw new InvalidOperationException("FILES_RECEIPT_AVAILABLE");
        await authorization.AuthorizeAsync(actor, cancellationToken); await repository.AcknowledgeAsync(actor, id, command.Version, receipt, cancellationToken);
    }
    private async Task<FileTreeMoveOperation> Owned(TerminalActor actor, Guid id, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty) throw new ArgumentException("FILES_OPERATION_ID_REQUIRED");
        var row = await repository.OwnedAsync(actor, id, cancellationToken) ?? throw new KeyNotFoundException("FILES_OPERATION_NOT_FOUND");
        if (!FileTreeMoveValidation.HasValidBinding(row, protector)) throw new InvalidOperationException("FILES_INTENT_BINDING_CHANGED"); return row;
    }
    private FileTreeMoveIntentDto Map(FileTreeMoveOperation row)
    {
        if (!FileTreeMoveValidation.HasValidBinding(row, protector)) throw new InvalidOperationException("FILES_INTENT_BINDING_CHANGED");
        var plan = row.PlanJson is null ? null : FileTreeMoveValidation.ReadPlan(row);
        var ready = row.State == "Ready" && row.CancelRequestedAtUtc is null && row.ReadyExpiresAtUtc > clock.GetUtcNow();
        var confirmation = ready ? new FileTreeMoveConfirmationDto(protector.SignConfirmation(new(row.Id, row.ActorSubject, row.LoginBinding, plan!.Fingerprint, row.ReadyExpiresAtUtc!.Value)),
            row.ReadyExpiresAtUtc.Value, "完整清单已验证，尚未移动。确认后完整核验并发布新目标，再逐文件私有保留原件、移除源空目录，不覆盖。私有原件不是HDD备份；断线或未知结果仅查询原任务，不自动重做。") : null;
        return new(row.Id, row.Id, row.ExecutionJobId, row.State == "Ready" && !ready ? "Expired" : row.State, FileTreeMoveValidation.ReadCommand(row),
            plan?.EntryCount, plan?.TotalBytes, row.ReadyExpiresAtUtc, row.ResultCode, row.Version, row.PublishedVersion, row.DestinationPublished, row.SourceRemoved, row.SourceRetained, row.PartialRetained, confirmation);
    }
}
