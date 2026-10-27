using System.Security.Cryptography;
using System.Text;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

public sealed class ContainerFileUploadService(FileManagementService authorization, ILoginSessionRegistry logins,
    IContainerFileWriteGateway gateway, IContainerFileUploadRepository repository, IContainerFileUploadProtector plans, TimeProvider clock,
    IContainerFileHistoryGateway? history = null)
{
    public async Task AuthorizeAsync(TerminalActor actor, CancellationToken cancellationToken)
    {
        await authorization.AuthorizeAsync(actor, cancellationToken);
        if (!await logins.IsActiveAsync(actor.Subject, actor.LoginBinding, cancellationToken))
            throw new UnauthorizedAccessException("CONTAINER_UPLOAD_LOGIN_REQUIRED");
    }
    public async Task<FileUploadPlanDto> PlanAsync(TerminalActor actor, ContainerFileUploadCommand command, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken); ContainerFileUploadIntentValidation.Command(command);
        await gateway.RequireContainerUploadWorkflowAsync(cancellationToken);
        var target = await gateway.InspectContainerUploadTargetAsync(command.Location, cancellationToken);
        ContainerFileUploadIntentValidation.Target(target);
        if (target.Location != command.Location) throw new InvalidOperationException("CONTAINER_UPLOAD_TARGET_INVALID");
        if (target.ExpectedVersion != command.ExpectedVersion) throw new InvalidOperationException("CONTAINER_UPLOAD_VERSION_CONFLICT");
        await AuthorizeAsync(actor, cancellationToken);
        var created = ContainerFileUploadIntentValidation.CanonicalTime(clock.GetUtcNow()); var expires = created.Add(ContainerFileUploadIntentValidation.PlanLifetime);
        var claims = new ContainerFileUploadPlanClaims(actor.Subject, actor.LoginBinding, target, command.SizeBytes, command.Sha256, created, expires);
        return new(plans.SignContainerUpload(claims), expires, target.ExpectedVersion != "ABSENT",
            "文件先暂存并完整校验，再由任务发布。覆盖前需验证HDD旧版本；同盘副本属于版本留存。目标或启动实例变化会拒绝，结果未知时停止自动写入并保留现场。");
    }
    public async Task<ContainerFileUploadIntentDto> SubmitAsync(TerminalActor actor, string containerId, SubmitFileUploadCommand command, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken); ContainerFileValidation.Id(containerId);
        var claims = plans.VerifyContainerUpload(command.PlanHash);
        return await SubmitBoundAsync(actor, containerId, command, claims, null, cancellationToken);
    }

    // Only signed Application plans call this shared immutable publication
    // workflow. The HTTP body is never a restoration source.
    internal async Task<ContainerFileUploadIntentDto> SubmitBoundAsync(TerminalActor actor, string containerId, SubmitFileUploadCommand command,
        ContainerFileUploadPlanClaims claims, string? historySourceJson, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken); ContainerFileValidation.Id(containerId);
        if (!command.Confirmed || !Guid.TryParseExact(command.IdempotencyKey, "D", out var key) || key == Guid.Empty || key.ToString("D") != command.IdempotencyKey)
            throw new ArgumentException("CONTAINER_UPLOAD_CONFIRMATION_REQUIRED");
        ContainerFileUploadIntentValidation.Claims(claims);
        if (historySourceJson is not null)
        {
            var source = ContainerFileRestoreValidation.Read(historySourceJson);
            _ = ContainerFileRestoreValidation.SourceLocation(claims.Target.Location, source);
            if (source.SizeBytes != claims.SizeBytes || source.Sha256 != claims.Sha256) throw ContainerFileRestoreValidation.Invalid();
        }
        if (claims.Actor != actor.Subject || claims.LoginBinding != actor.LoginBinding || claims.Target.Location.ContainerId != containerId)
            throw new UnauthorizedAccessException("CONTAINER_UPLOAD_PLAN_OWNER_REQUIRED");
        var id = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes("whaledeck-container-upload-id-v1:" + BffSessionValidation.SubjectHash(actor.Subject) + ":" + actor.LoginBinding + ":" + key.ToString("N"))).AsSpan(0, 16));
        var planHash = FileManagementService.Hash(command.PlanHash);
        var prior = await repository.OwnedAsync(actor, id, cancellationToken);
        if (prior is not null)
        {
            if (!HasValidBinding(prior, plans) || prior.PlanHash != planHash || prior.IdempotencyKey != key || prior.HistorySourceJson != historySourceJson)
                throw new InvalidOperationException("CONTAINER_UPLOAD_IDEMPOTENCY_CONFLICT");
            return Map(prior);
        }
        var now = clock.GetUtcNow();
        if (claims.ExpiresAtUtc <= now || claims.CreatedAtUtc > now) throw new InvalidOperationException("CONTAINER_UPLOAD_PLAN_EXPIRED");
        if (historySourceJson is not null)
        {
            if (history is null) throw new InvalidOperationException("CONTAINER_HISTORY_CAPABILITY_UNAVAILABLE");
            await history.RequireContainerHistoryWorkflowAsync(cancellationToken);
        }
        await gateway.RequireContainerUploadWorkflowAsync(cancellationToken);
        var current = await gateway.InspectContainerUploadTargetAsync(claims.Target.Location, cancellationToken);
        ContainerFileUploadIntentValidation.Target(current);
        if (current != claims.Target) throw new InvalidOperationException("CONTAINER_UPLOAD_VERSION_CONFLICT");
        await AuthorizeAsync(actor, cancellationToken);
        var created = ContainerFileUploadIntentValidation.CanonicalTime(clock.GetUtcNow()); var expires = created.Add(ContainerFileUploadIntentValidation.IntentLifetime);
        // Re-check after the bounded remote target inspection: an expired plan
        // must not acquire a fresh permanent intent through a slow dependency.
        if (claims.ExpiresAtUtc <= created) throw new InvalidOperationException("CONTAINER_UPLOAD_PLAN_EXPIRED");
        var binding = new ContainerFileUploadBinding(id, key, actor.Subject, actor.LoginBinding, actor.TraceId, current,
            claims.SizeBytes, claims.Sha256, planHash, created, expires, historySourceJson);
        var row = new ContainerFileUploadOperation { Id = id, IdempotencyKey = key, ActorSubject = actor.Subject, LoginBinding = actor.LoginBinding,
            TraceId = actor.TraceId, OwnerHash = ContainerFileUploadIntentValidation.Owner(actor.Subject, actor.LoginBinding), ContainerId = current.Location.ContainerId,
            InstanceId = current.Location.InstanceId, RelativePath = current.Location.Path, TargetProof = current.TargetProof, ExpectedParentVersion = current.ExpectedParentVersion,
            ExpectedVersion = current.ExpectedVersion, PreviousSizeBytes = current.CurrentSizeBytes, PlanHash = planHash, RequestHash = plans.FingerprintContainerUpload(binding),
            Sha256 = claims.Sha256, SizeBytes = claims.SizeBytes, HistorySourceJson = historySourceJson,
            CreatedAtUtc = created, ExpiresAtUtc = expires, NextAttemptAtUtc = created.AddSeconds(6) };
        return Map(await repository.EnqueueAsync(row, cancellationToken));
    }
    public async Task<ContainerFileUploadIntentDto> GetAsync(TerminalActor actor, string containerId, Guid id, CancellationToken cancellationToken)
    { await AuthorizeAsync(actor, cancellationToken); return Map(await Owned(actor, containerId, id, cancellationToken)); }
    public async Task CancelAsync(TerminalActor actor, string containerId, Guid id, CancellationToken cancellationToken)
    { await AuthorizeAsync(actor, cancellationToken); _ = await Owned(actor, containerId, id, cancellationToken); await repository.CancelAsync(actor, id, cancellationToken); }

    public async Task<ContainerFileUploadIntentDto> ReceiveAsync(TerminalActor actor, string containerId, Guid id, Stream source, long? declaredLength,
        Func<CancellationToken, Task> validateLogin, CancellationToken cancellationToken)
    {
        await validateLogin(cancellationToken); await AuthorizeAsync(actor, cancellationToken);
        var owned = await Owned(actor, containerId, id, cancellationToken);
        if (owned.HistorySourceJson is not null) throw new InvalidOperationException("CONTAINER_UPLOAD_HTTP_SOURCE_FORBIDDEN");
        if (declaredLength != owned.SizeBytes) throw new ArgumentException("CONTAINER_UPLOAD_CONTENT_LENGTH_REQUIRED");
        await gateway.RequireContainerUploadWorkflowAsync(cancellationToken);
        var row = await repository.BeginReceiveAsync(actor, id, clock.GetUtcNow(), cancellationToken);
        if (row.ContainerId != containerId || !HasValidBinding(row, plans)) throw new InvalidOperationException("CONTAINER_UPLOAD_INTENT_BINDING_CHANGED");
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var remaining = row.ExpiresAtUtc - clock.GetUtcNow();
        if (remaining <= TimeSpan.Zero) throw new InvalidOperationException("CONTAINER_UPLOAD_EXPIRED");
        lifetime.CancelAfter(remaining < TimeSpan.FromMinutes(10) ? remaining : TimeSpan.FromMinutes(10));
        var monitoring = Monitor();
        try
        {
            var receipt = await gateway.StageContainerFileAsync(ContainerFileUploadIntentValidation.Stage(row),
                FileManagementService.Bytes(source, row.SizeBytes, lifetime.Token), lifetime.Token);
            if (receipt.ContainerId != row.ContainerId || receipt.InstanceId != row.InstanceId || receipt.OperationId != row.Id || receipt.TargetProof != row.TargetProof ||
                receipt.Upload.State != "Staged" || receipt.Upload.SizeBytes != row.SizeBytes || receipt.Upload.Sha256 != row.Sha256 ||
                receipt.Upload.HddBackup is not null || receipt.Upload.PreviousRetained || receipt.Upload.BindingCurrent || receipt.Upload.IsInProgress || receipt.Upload.Version.Length != 0)
                throw new InvalidOperationException("CONTAINER_UPLOAD_STAGING_UNCERTAIN");
        }
        catch (OperationCanceledException) when (monitoring.IsFaulted) { await monitoring; throw; }
        finally
        {
            await lifetime.CancelAsync();
            try { await monitoring; } catch (OperationCanceledException) { }
        }
        // Join the monitor before using the scoped DbContext again. Only a
        // fully received stream and still-current login can mark bytes Ready.
        await validateLogin(cancellationToken); await AuthorizeAsync(actor, cancellationToken);
        if (row.ExpiresAtUtc <= clock.GetUtcNow() || !await repository.ReadyAsync(row, cancellationToken))
            throw new InvalidOperationException("CONTAINER_UPLOAD_STOPPED");
        return Map(await Owned(actor, containerId, id, cancellationToken));
        async Task Monitor()
        {
            try
            {
                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(6), clock);
                do
                {
                    await validateLogin(lifetime.Token); await AuthorizeAsync(actor, lifetime.Token);
                    if (!await repository.MayReceiveAsync(row.Id, row.ReceiveToken!.Value, clock.GetUtcNow(), lifetime.Token))
                        throw new InvalidOperationException("CONTAINER_UPLOAD_STOPPED");
                } while (await timer.WaitForNextTickAsync(lifetime.Token));
            }
            catch { await lifetime.CancelAsync(); throw; }
        }
    }
    private async Task<ContainerFileUploadOperation> Owned(TerminalActor actor, string containerId, Guid id, CancellationToken cancellationToken)
    {
        ContainerFileValidation.Id(containerId);
        if (id == Guid.Empty) throw new ArgumentException("CONTAINER_UPLOAD_ID_REQUIRED");
        var row = await repository.OwnedAsync(actor, id, cancellationToken) ?? throw new KeyNotFoundException("CONTAINER_UPLOAD_NOT_FOUND");
        if (row.ContainerId != containerId) throw new KeyNotFoundException("CONTAINER_UPLOAD_NOT_FOUND");
        if (!HasValidBinding(row, plans)) throw new InvalidOperationException("CONTAINER_UPLOAD_INTENT_BINDING_CHANGED");
        return row;
    }
    public static bool HasValidBinding(ContainerFileUploadOperation row, IContainerFileUploadProtector protector)
    {
        try
        {
            return FileManagementService.Hex(row.RequestHash) && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(row.RequestHash),
                Encoding.ASCII.GetBytes(protector.FingerprintContainerUpload(ContainerFileUploadIntentValidation.ReadBinding(row))));
        }
        catch (ArgumentException) { return false; }
    }
    public static ContainerFileUploadIntentDto Map(ContainerFileUploadOperation row) => new(row.Id, row.Id,
        new(row.ContainerId, row.InstanceId, row.RelativePath), row.SizeBytes, row.Sha256, row.State, row.ExpiresAtUtc, row.ResultCode, row.Version, row.PublishedVersion, row.PreviousRetained);
}
