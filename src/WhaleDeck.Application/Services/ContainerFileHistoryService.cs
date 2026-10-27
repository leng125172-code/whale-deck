using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Services;

public sealed class ContainerFileHistoryService(ContainerFileUploadService authorization, IContainerFileHistoryGateway gateway,
    IContainerFileHistoryRepository catalog, IContainerFileReadAudit audit)
{
    public async Task<ContainerHistoryPageDto> ListAsync(TerminalActor actor, ContainerFileLocationDto location,
        string after, int take, string expectedVersion, CancellationToken cancellationToken)
    {
        await authorization.AuthorizeAsync(actor, cancellationToken);
        ContainerFileHistoryValidation.List(location, after, take, expectedVersion);
        var page = await gateway.ListContainerHistoryAsync(location, after, take, expectedVersion, cancellationToken);
        ContainerFileHistoryValidation.Page(page, location, after, take, expectedVersion);
        await authorization.AuthorizeAsync(actor, cancellationToken);
        await catalog.ObserveAsync(actor, page, cancellationToken);
        await audit.RecordAsync(actor, location, "history", "Validated", page.Version, null, null, cancellationToken);
        return page;
    }
}

public sealed class ContainerFileRestoreService(ContainerFileUploadService uploads, IContainerFileWriteGateway gateway, IContainerFileHistoryGateway history,
    IContainerFileHistoryRepository catalog, IContainerFileRestoreProtector plans, IContainerFileUploadRepository repository, TimeProvider clock)
{
    public async Task<FileUploadPlanDto> PlanAsync(TerminalActor actor, ContainerFileRestoreCommand command, CancellationToken cancellationToken)
    {
        await uploads.AuthorizeAsync(actor, cancellationToken);
        ContainerFileValidation.Location(command.Location); ContainerFileHistoryValidation.CaptureId(command.CaptureId);
        ContainerFileValidation.Hash(command.ExpectedCaptureHash);
        if (command.ExpectedVersion != "ABSENT") ContainerFileValidation.Hash(command.ExpectedVersion);
        var source = await catalog.FindAsync(command.Location.ContainerId, command.CaptureId, cancellationToken)
            ?? throw new KeyNotFoundException("CONTAINER_HISTORY_NOT_FOUND");
        _ = ContainerFileRestoreValidation.SourceLocation(command.Location, source);
        if (source.CaptureId != command.CaptureId || source.CaptureHash != command.ExpectedCaptureHash) throw new InvalidOperationException("CONTAINER_HISTORY_CAPTURE_CHANGED");
        await history.RequireContainerHistoryWorkflowAsync(cancellationToken);
        await gateway.RequireContainerUploadWorkflowAsync(cancellationToken);
        var target = await gateway.InspectContainerUploadTargetAsync(command.Location, cancellationToken);
        ContainerFileUploadIntentValidation.Target(target);
        if (target.Location != command.Location || target.ExpectedVersion != command.ExpectedVersion)
            throw new InvalidOperationException("CONTAINER_UPLOAD_VERSION_CONFLICT");
        await uploads.AuthorizeAsync(actor, cancellationToken);
        var now = ContainerFileUploadIntentValidation.CanonicalTime(clock.GetUtcNow());
        var claims = new ContainerFileRestorePlanClaims(actor.Subject, actor.LoginBinding, target, source, now, now.Add(ContainerFileUploadIntentValidation.PlanLifetime));
        return new(plans.SignContainerRestore(claims), claims.ExpiresAtUtc, target.ExpectedVersion != "ABSENT",
            "恢复任务会从HDD完整校验旧版本后暂存，再发布到已确认的目标路径。覆盖前会另存当前文件；同盘副本只属于版本留存。此操作恢复内容，文件属性遵循目标的发布策略。备份、容器或目标变化会拒绝，不会重新发送或重复发布结果未知的任务。");
    }
    public async Task<ContainerFileUploadIntentDto> SubmitAsync(TerminalActor actor, string containerId, SubmitFileUploadCommand command, CancellationToken cancellationToken)
    {
        await uploads.AuthorizeAsync(actor, cancellationToken); ContainerFileValidation.Id(containerId);
        var claims = plans.VerifyContainerRestore(command.PlanHash); ContainerFileRestoreValidation.Claims(claims);
        return await uploads.SubmitBoundAsync(actor, containerId, command,
            new(claims.Actor, claims.LoginBinding, claims.Target, claims.Source.SizeBytes, claims.Source.Sha256, claims.CreatedAtUtc, claims.ExpiresAtUtc),
            ContainerFileRestoreValidation.Snapshot(claims.Source), cancellationToken);
    }
    public async Task<ContainerFileUploadIntentDto> GetAsync(TerminalActor actor, string containerId, Guid id, CancellationToken cancellationToken)
    {
        var result = await uploads.GetAsync(actor, containerId, id, cancellationToken);
        var row = await repository.OwnedAsync(actor, id, cancellationToken);
        if (row?.HistorySourceJson is null) throw new KeyNotFoundException("CONTAINER_HISTORY_RESTORE_NOT_FOUND");
        return result;
    }
    public async Task CancelAsync(TerminalActor actor, string containerId, Guid id, CancellationToken cancellationToken)
    {
        _ = await GetAsync(actor, containerId, id, cancellationToken);
        await uploads.CancelAsync(actor, containerId, id, cancellationToken);
    }
}
