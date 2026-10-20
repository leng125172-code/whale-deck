using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

public sealed partial class FileTreeRestoringService(FileManagementService authorization, IFileTreeRestoringRepository repository,
    IFileTreeRestoringProtector protector, IFileTreeRestoringGateway gateway, TimeProvider clock)
{
    public async Task<FileTreeRestoringIntentDto> QueueAsync(TerminalActor actor, CreateFileTreeRestoringCommand command, string key, CancellationToken cancellationToken)
    {
        await authorization.AuthorizeAsync(actor, cancellationToken); FileTreeRestoringValidation.Request(command); FileTreeRestoringValidation.IntentId(actor, key);
        return Map(await repository.QueueAsync(actor, key, command, cancellationToken));
    }
    public async Task<FileTreeRestoringIntentDto> GetAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken)
    { await authorization.AuthorizeAsync(actor, cancellationToken); return Map(await Owned(actor, id, cancellationToken)); }
    public Task<FileTreeRestoringIntentDto> GetByKeyAsync(TerminalActor actor, string key, CancellationToken cancellationToken) =>
        GetAsync(actor, FileTreeRestoringValidation.IntentId(actor, key), cancellationToken);
    public async Task<FileTreeRestoringIntentDto> ConfirmAsync(TerminalActor actor, Guid id, ConfirmFileTreeRestoringCommand command, CancellationToken cancellationToken)
    {
        await authorization.AuthorizeAsync(actor, cancellationToken);
        if (!command.Confirmed || command.Version < 0) throw new ArgumentException("FILES_CONFIRMATION_REQUIRED");
        var claims = protector.VerifyRestoringConfirmation(command.PlanHash);
        if (claims.Id != id || claims.Actor != actor.Subject || claims.LoginBinding != actor.LoginBinding) throw new UnauthorizedAccessException("FILES_PLAN_OWNER_REQUIRED");
        return Map(await repository.ConfirmAsync(actor, claims, command.Version, cancellationToken));
    }
    public async Task<FileTreeRestoringIntentDto> CancelAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken)
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
        var receipt = await gateway.DiscardTreeRestoringAsync(row, cancellationToken);
        FileTreeRestoringValidation.Receipt(row.ExecutionJobId!.Value, FileTreeRestoringValidation.ReadPlan(row), receipt);
        if (receipt.State == "Completed") throw new InvalidOperationException("FILES_RECEIPT_AVAILABLE");
        await authorization.AuthorizeAsync(actor, cancellationToken); await repository.AcknowledgeAsync(actor, id, command.Version, receipt, cancellationToken);
    }
    private async Task<FileTreeRestoringOperation> Owned(TerminalActor actor, Guid id, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty) throw new ArgumentException("FILES_OPERATION_ID_REQUIRED");
        var row = await repository.OwnedAsync(actor, id, cancellationToken) ?? throw new KeyNotFoundException("FILES_OPERATION_NOT_FOUND");
        if (!FileTreeRestoringValidation.HasValidBinding(row, protector)) throw new InvalidOperationException("FILES_INTENT_BINDING_CHANGED"); return row;
    }
    private FileTreeRestoringIntentDto Map(FileTreeRestoringOperation row)
    {
        if (!FileTreeRestoringValidation.HasValidBinding(row, protector)) throw new InvalidOperationException("FILES_INTENT_BINDING_CHANGED");
        var plan = row.PlanJson is null ? null : FileTreeRestoringValidation.ReadPlan(row);
        var ready = row.State == "Ready" && row.CancelRequestedAtUtc is null && row.ReadyExpiresAtUtc > clock.GetUtcNow();
        var confirmation = ready ? new FileTreeRestoringConfirmationDto(protector.SignConfirmation(new(row.Id, row.ActorSubject, row.LoginBinding, plan!.Fingerprint, row.ReadyExpiresAtUtc!.Value)),
            row.ReadyExpiresAtUtc.Value, "历史归档及完整清单已验证，但尚未恢复。确认后再次核验原捕获，只恢复到不存在的目标目录，不覆盖；历史归档和私有原件保留。结果未知时不自动重新恢复，保留锁与私有部分目录等待处置。") : null;
        var command = FileTreeRestoringValidation.ReadCommand(row);
        return new(row.Id, row.Id, row.ExecutionJobId, row.State == "Ready" && !ready ? "Expired" : row.State,
            new(command.CaptureId, command.Source, command.Target, command.CaptureKind),
            plan?.EntryCount, plan?.TotalBytes, row.ReadyExpiresAtUtc, row.ResultCode, row.Version, row.PublishedVersion, row.PartialRetained, confirmation, row.ArchiveBytes, row.ArchiveSha256);
    }
}
