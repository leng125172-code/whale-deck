using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

public sealed partial class FileTreePackingService(FileManagementService authorization, IFileTreePackingRepository repository,
    IFileTreePackingProtector protector, IFileTreePackingGateway gateway, TimeProvider clock)
{
    public async Task<FileTreePackingIntentDto> QueueAsync(TerminalActor actor, FileTreePackingCommand command, string key, CancellationToken cancellationToken)
    {
        await authorization.AuthorizeAsync(actor, cancellationToken); FileTreePackingValidation.Command(command); FileTreePackingValidation.IntentId(actor, key);
        return Map(await repository.QueueAsync(actor, key, command, cancellationToken));
    }
    public async Task<FileTreePackingIntentDto> GetAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken)
    { await authorization.AuthorizeAsync(actor, cancellationToken); return Map(await Owned(actor, id, cancellationToken)); }
    public Task<FileTreePackingIntentDto> GetByKeyAsync(TerminalActor actor, string key, CancellationToken cancellationToken) =>
        GetAsync(actor, FileTreePackingValidation.IntentId(actor, key), cancellationToken);
    public async Task<FileTreePackingIntentDto> ConfirmAsync(TerminalActor actor, Guid id, ConfirmFileTreePackingCommand command, CancellationToken cancellationToken)
    {
        await authorization.AuthorizeAsync(actor, cancellationToken);
        if (!command.Confirmed || command.Version < 0) throw new ArgumentException("FILES_CONFIRMATION_REQUIRED");
        var claims = protector.VerifyPackingConfirmation(command.PlanHash);
        if (claims.Id != id || claims.Actor != actor.Subject || claims.LoginBinding != actor.LoginBinding) throw new UnauthorizedAccessException("FILES_PLAN_OWNER_REQUIRED");
        return Map(await repository.ConfirmAsync(actor, claims, command.Version, cancellationToken));
    }
    public async Task<FileTreePackingIntentDto> CancelAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken)
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
        var receipt = await gateway.DiscardTreePackingAsync(row, cancellationToken);
        FileTreePackingValidation.Receipt(row.ExecutionJobId!.Value, FileTreePackingValidation.ReadPlan(row), receipt);
        if (receipt.State == "Completed") throw new InvalidOperationException("FILES_RECEIPT_AVAILABLE");
        await authorization.AuthorizeAsync(actor, cancellationToken); await repository.AcknowledgeAsync(actor, id, command.Version, receipt, cancellationToken);
    }
    private async Task<FileTreePackingOperation> Owned(TerminalActor actor, Guid id, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty) throw new ArgumentException("FILES_OPERATION_ID_REQUIRED");
        var row = await repository.OwnedAsync(actor, id, cancellationToken) ?? throw new KeyNotFoundException("FILES_OPERATION_NOT_FOUND");
        if (!FileTreePackingValidation.HasValidBinding(row, protector)) throw new InvalidOperationException("FILES_INTENT_BINDING_CHANGED"); return row;
    }
    private FileTreePackingIntentDto Map(FileTreePackingOperation row)
    {
        if (!FileTreePackingValidation.HasValidBinding(row, protector)) throw new InvalidOperationException("FILES_INTENT_BINDING_CHANGED");
        var plan = row.PlanJson is null ? null : FileTreePackingValidation.ReadPlan(row);
        var ready = row.State == "Ready" && row.CancelRequestedAtUtc is null && row.ReadyExpiresAtUtc > clock.GetUtcNow();
        var confirmation = ready ? new FileTreePackingConfirmationDto(protector.SignConfirmation(new(row.Id, row.ActorSubject, row.LoginBinding, plan!.Fingerprint, row.ReadyExpiresAtUtc!.Value)),
            row.ReadyExpiresAtUtc.Value, "完整清单已验证，但尚未压缩。确认后再次校验源树，只创建不存在的 TAR/TAR.GZ 目标，不覆盖；源保留。结果未知时不自动重新压缩，保留锁与私有部分归档等待处置。") : null;
        return new(row.Id, row.Id, row.ExecutionJobId, row.State == "Ready" && !ready ? "Expired" : row.State, FileTreePackingValidation.ReadCommand(row),
            plan?.EntryCount, plan?.TotalBytes, row.ReadyExpiresAtUtc, row.ResultCode, row.Version, row.PublishedVersion, row.PartialRetained, confirmation, row.ArchiveBytes, row.ArchiveSha256);
    }
}
