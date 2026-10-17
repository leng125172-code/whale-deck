using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

/// <summary>A new administrator's own plan and audit, never a substitute original login.</summary>
public sealed class FileRecoveryService(FileManagementService authorization, IFileRecoveryRepository repository,
    IFileRecoveryProtector protector, TimeProvider clock)
{
    public async Task<FileRecoveryIntentDto> QueueAsync(TerminalActor actor, QueueFileRecoveryCommand command, string key, CancellationToken cancellationToken)
    {
        await authorization.AuthorizeAsync(actor, cancellationToken);
        FileRecoveryValidation.Command(command); _ = FileRecoveryValidation.RegistrationId(actor, key);
        var row = await repository.QueueAsync(actor, key, command, cancellationToken);
        await authorization.AuthorizeAsync(actor, cancellationToken); return Map(row);
    }
    public async Task<FileRecoveryIntentDto> GetAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken)
    {
        await authorization.AuthorizeAsync(actor, cancellationToken);
        var row = await Owned(actor, id, cancellationToken);
        await authorization.AuthorizeAsync(actor, cancellationToken); return Map(row);
    }
    public Task<FileRecoveryIntentDto> GetByKeyAsync(TerminalActor actor, string key, CancellationToken cancellationToken) =>
        GetAsync(actor, FileRecoveryValidation.RegistrationId(actor, key), cancellationToken);
    public async Task<FileRecoveryIntentDto> ConfirmAsync(TerminalActor actor, Guid id, ConfirmFileRecoveryCommand command, CancellationToken cancellationToken)
    {
        await authorization.AuthorizeAsync(actor, cancellationToken);
        if (!command.Confirmed || command.Version < 0) throw new ArgumentException("FILES_CONFIRMATION_REQUIRED");
        var claims = protector.VerifyRecovery(command.PlanHash);
        if (claims.Id != id || claims.Actor != actor.Subject || claims.LoginBinding != actor.LoginBinding)
            throw new UnauthorizedAccessException("FILES_RECOVERY_CURRENT_LOGIN_REQUIRED");
        await Owned(actor, id, cancellationToken);
        await authorization.AuthorizeAsync(actor, cancellationToken);
        var row = await repository.ConfirmAsync(actor, claims, command.Version, cancellationToken);
        await authorization.AuthorizeAsync(actor, cancellationToken); return Map(row);
    }
    public async Task<FileRecoveryIntentDto> CancelAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken)
    {
        await authorization.AuthorizeAsync(actor, cancellationToken); await Owned(actor, id, cancellationToken);
        await repository.CancelAsync(actor, id, cancellationToken);
        await authorization.AuthorizeAsync(actor, cancellationToken); return Map(await Owned(actor, id, cancellationToken));
    }
    private async Task<FileRecoveryOperation> Owned(TerminalActor actor, Guid id, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty) throw new ArgumentException("FILES_RECOVERY_ID_INVALID");
        var row = await repository.OwnedAsync(actor, id, cancellationToken) ?? throw new KeyNotFoundException("FILES_RECOVERY_NOT_FOUND");
        if (row.ActorSubject != actor.Subject || row.LoginBinding != actor.LoginBinding) throw new KeyNotFoundException("FILES_RECOVERY_NOT_FOUND");
        return row;
    }
    private FileRecoveryIntentDto Map(FileRecoveryOperation row)
    {
        if (!FileRecoveryValidation.Bound(row, protector)) throw new InvalidOperationException("FILES_RECOVERY_BINDING_CHANGED");
        var ready = row.State == "Ready" && row.CancelRequestedAtUtc is null && row.ReadyExpiresAtUtc > clock.GetUtcNow();
        var confirmation = ready ? new FileRecoveryConfirmationDto(protector.SignRecovery(FileRecoveryValidation.Ready(row)), row.ReadyExpiresAtUtc!.Value,
            row.Disposition == "ReconcileCompleted" ? "仅按原任务的完整可信回执对账，不重新执行文件操作。完整回执是历史事实，不保证当前目标未变化；对账不会覆盖当前文件。原主体和永久回执保留，当前管理员另行审计。" :
                "明确接受原文件任务的历史不确定性。只有原任务停止且迟到操作已封存后才解除原锁；不表示文件操作成功，不重做、不删除留存对象。") : null;
        return new(row.Id, row.Id, row.ExecutionJobId, row.Kind, row.OriginalIntentId, row.OriginalExecutionJobId, row.OriginalVersion,
            row.State == "Ready" && !ready ? "Expired" : row.State, row.Disposition, row.ReasonCode, row.RootState,
            row.ReadyExpiresAtUtc, row.Version, row.ResultCode, row.OriginalResolution, confirmation);
    }
}
