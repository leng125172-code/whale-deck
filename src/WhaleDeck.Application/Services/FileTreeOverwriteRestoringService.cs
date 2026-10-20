using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

public sealed partial class FileTreeOverwriteRestoringService(FileManagementService authorization, IFileTreeOverwriteRestoringRepository repository,
    IFileTreeOverwriteRestoringProtector protector, IFileTreeOverwriteRestoringGateway gateway, TimeProvider clock)
{
    public async Task<FileTreeOverwriteRestoringIntentDto> QueueAsync(TerminalActor actor, CreateFileTreeOverwriteRestoringCommand command, string key, CancellationToken cancellationToken)
    {
        await authorization.AuthorizeAsync(actor, cancellationToken); FileTreeOverwriteRestoringValidation.Request(command); FileTreeOverwriteRestoringValidation.IntentId(actor, key);
        return Map(await repository.QueueAsync(actor, key, command, cancellationToken));
    }
    public async Task<FileTreeOverwriteRestoringIntentDto> GetAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken)
    { await authorization.AuthorizeAsync(actor, cancellationToken); return Map(await Owned(actor, id, cancellationToken)); }
    public Task<FileTreeOverwriteRestoringIntentDto> GetByKeyAsync(TerminalActor actor, string key, CancellationToken cancellationToken) =>
        GetAsync(actor, FileTreeOverwriteRestoringValidation.IntentId(actor, key), cancellationToken);
    public async Task<FileTreeOverwriteRestoringIntentDto> ConfirmAsync(TerminalActor actor, Guid id, ConfirmFileTreeOverwriteRestoringCommand command, CancellationToken cancellationToken)
    {
        await authorization.AuthorizeAsync(actor, cancellationToken);
        if (!command.Confirmed || command.Version < 0) throw new ArgumentException("FILES_CONFIRMATION_REQUIRED");
        var claims = protector.VerifyOverwriteRestoringConfirmation(command.PlanHash);
        if (claims.Id != id || claims.Actor != actor.Subject || claims.LoginBinding != actor.LoginBinding) throw new UnauthorizedAccessException("FILES_PLAN_OWNER_REQUIRED");
        return Map(await repository.ConfirmAsync(actor, claims, command.Version, cancellationToken));
    }
    public async Task<FileTreeOverwriteRestoringIntentDto> CancelAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken)
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
        var receipt = await gateway.DiscardTreeOverwriteRestoringAsync(row, cancellationToken);
        FileTreeOverwriteRestoringValidation.Receipt(row.ExecutionJobId!.Value, FileTreeOverwriteRestoringValidation.ReadPlan(row), receipt);
        if (receipt.State == "Completed") throw new InvalidOperationException("FILES_RECEIPT_AVAILABLE");
        await authorization.AuthorizeAsync(actor, cancellationToken); await repository.AcknowledgeAsync(actor, id, command.Version, receipt, cancellationToken);
    }
    private async Task<FileTreeOverwriteRestoringOperation> Owned(TerminalActor actor, Guid id, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty) throw new ArgumentException("FILES_OPERATION_ID_REQUIRED");
        var row = await repository.OwnedAsync(actor, id, cancellationToken) ?? throw new KeyNotFoundException("FILES_OPERATION_NOT_FOUND");
        if (!FileTreeOverwriteRestoringValidation.HasValidBinding(row, protector)) throw new InvalidOperationException("FILES_INTENT_BINDING_CHANGED"); return row;
    }
    private FileTreeOverwriteRestoringIntentDto Map(FileTreeOverwriteRestoringOperation row)
    {
        if (!FileTreeOverwriteRestoringValidation.HasValidBinding(row, protector)) throw new InvalidOperationException("FILES_INTENT_BINDING_CHANGED");
        var plan = row.PlanJson is null ? null : FileTreeOverwriteRestoringValidation.ReadPlan(row);
        var ready = row.State == "Ready" && row.CancelRequestedAtUtc is null && row.ReadyExpiresAtUtc > clock.GetUtcNow();
        var confirmation = ready ? new FileTreeOverwriteRestoringConfirmationDto(protector.SignConfirmation(new(row.Id, row.ActorSubject, row.LoginBinding, plan!.Fingerprint, row.ReadyExpiresAtUtc!.Value)),
            row.ReadyExpiresAtUtc.Value, "历史归档和当前目标目录的完整证明已核验，但尚未覆盖。确认后先完整备份当前目标到HDD，再原子替换目录并留存旧原件。历史归档保留；结果未知时不自动重复覆盖，保留锁和备份等待处置。") : null;
        var command = FileTreeOverwriteRestoringValidation.ReadCommand(row);
        return new(row.Id, row.Id, row.ExecutionJobId, row.State == "Ready" && !ready ? "Expired" : row.State,
            new(command.CaptureId, command.Source, command.Target, command.CaptureKind, command.TargetExpectedVersion),
            plan?.EntryCount, plan?.TotalBytes, plan?.TargetEntryCount, plan?.TargetTotalBytes, row.ReadyExpiresAtUtc, row.ResultCode, row.Version, row.PublishedVersion, row.PartialRetained, confirmation, row.ArchiveBytes, row.ArchiveSha256, row.OverwrittenCaptureId, row.BackupMayBeRetained, row.BackupVerified,
            row.OriginalsRetained, row.TargetMayHaveChanged, row.BackupArchiveBytes, row.BackupArchiveSha256);
    }
}
