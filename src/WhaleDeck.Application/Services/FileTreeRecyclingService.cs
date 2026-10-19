using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

public sealed class FileTreeRecyclingService(FileManagementService authorization, IFileTreeRecyclingRepository repository,
    IFileTreeRecyclingProtector protector, IFileTreeRecyclingGateway gateway, TimeProvider clock)
{
    public async Task<FileTreeRecyclingIntentDto> QueueAsync(TerminalActor actor, FileTreeRecyclingCommand command, string key, CancellationToken cancellationToken)
    {
        await authorization.AuthorizeAsync(actor, cancellationToken); FileTreeRecyclingValidation.Command(command); FileTreeRecyclingValidation.IntentId(actor, key);
        return Map(await repository.QueueAsync(actor, key, command, cancellationToken));
    }
    public async Task<FileTreeRecyclingIntentDto> GetAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken)
    { await authorization.AuthorizeAsync(actor, cancellationToken); return Map(await Owned(actor, id, cancellationToken)); }
    public Task<FileTreeRecyclingIntentDto> GetByKeyAsync(TerminalActor actor, string key, CancellationToken cancellationToken) =>
        GetAsync(actor, FileTreeRecyclingValidation.IntentId(actor, key), cancellationToken);
    public async Task<FileTreeCapturePage> CapturesAsync(TerminalActor actor, FileLocationDto location, Guid? after, int take, CancellationToken cancellationToken)
    {
        await authorization.AuthorizeAsync(actor, cancellationToken); FileManagementService.Location(location, true);
        if (take is < 1 or > 100 || after == Guid.Empty) throw new ArgumentException("FILES_PAGE_INVALID");
        return await repository.CapturesAsync(location, after, take, cancellationToken);
    }
    public async Task<FileTreeRecyclingIntentDto> ConfirmAsync(TerminalActor actor, Guid id, ConfirmFileTreeRecyclingCommand command, CancellationToken cancellationToken)
    {
        await authorization.AuthorizeAsync(actor, cancellationToken);
        if (!command.Confirmed || command.Version < 0) throw new ArgumentException("FILES_CONFIRMATION_REQUIRED");
        var claims = protector.VerifyRecyclingConfirmation(command.PlanHash);
        if (claims.Id != id || claims.Actor != actor.Subject || claims.LoginBinding != actor.LoginBinding) throw new UnauthorizedAccessException("FILES_PLAN_OWNER_REQUIRED");
        return Map(await repository.ConfirmAsync(actor, claims, command.Version, cancellationToken));
    }
    public async Task<FileTreeRecyclingIntentDto> CancelAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken)
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
        var receipt = await gateway.DiscardTreeRecyclingAsync(row, cancellationToken);
        FileTreeRecyclingValidation.Receipt(row.ExecutionJobId!.Value, FileTreeRecyclingValidation.ReadPlan(row), receipt);
        if (receipt.State == "Completed") throw new InvalidOperationException("FILES_RECEIPT_AVAILABLE");
        await authorization.AuthorizeAsync(actor, cancellationToken); await repository.AcknowledgeAsync(actor, id, command.Version, receipt, cancellationToken);
    }
    private async Task<FileTreeRecyclingOperation> Owned(TerminalActor actor, Guid id, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty) throw new ArgumentException("FILES_OPERATION_ID_REQUIRED");
        var row = await repository.OwnedAsync(actor, id, cancellationToken) ?? throw new KeyNotFoundException("FILES_OPERATION_NOT_FOUND");
        if (!FileTreeRecyclingValidation.HasValidBinding(row, protector)) throw new InvalidOperationException("FILES_INTENT_BINDING_CHANGED"); return row;
    }
    private FileTreeRecyclingIntentDto Map(FileTreeRecyclingOperation row)
    {
        if (!FileTreeRecyclingValidation.HasValidBinding(row, protector)) throw new InvalidOperationException("FILES_INTENT_BINDING_CHANGED");
        var plan = row.PlanJson is null ? null : FileTreeRecyclingValidation.ReadPlan(row);
        var ready = row.State == "Ready" && row.CancelRequestedAtUtc is null && row.ReadyExpiresAtUtc > clock.GetUtcNow();
        // A lost root response is not evidence that no source effects occurred.
        // Null until a validated receipt or a proven pre-start outcome exists.
        var known = row.RootStartedAtUtc is null || row.CaptureVerified || row.State is "Succeeded" or "Failed" or "Canceled";
        var confirmation = ready ? new FileTreeRecyclingConfirmationDto(protector.SignConfirmation(new(row.Id, row.ActorSubject, row.LoginBinding, plan!.Fingerprint, row.ReadyExpiresAtUtc!.Value)),
            row.ReadyExpiresAtUtc.Value, "确认后先生成并完整验证HDD归档，再逐文件保留原件并移除原目录路径；不直接彻底删除。归档核验失败不改源目录。结果未知时保留路径锁和现场，不自动重新执行。") : null;
        return new(row.Id, row.Id, row.ExecutionJobId, row.State == "Ready" && !ready ? "Expired" : row.State, FileTreeRecyclingValidation.ReadCommand(row), plan?.EntryCount,
            plan?.TotalBytes, row.ReadyExpiresAtUtc, row.ResultCode, row.Version, row.CaptureId, row.ArchiveBytes, row.ArchiveSha256, row.CaptureVerified,
            known ? row.CaptureMayBeRetained : null, known ? row.SourceMayHaveChanged : null, known ? row.SourceRemoved : null, known ? row.OriginalsRetained : null, confirmation);
    }
}
