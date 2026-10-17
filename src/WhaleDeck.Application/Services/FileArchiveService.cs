using System.Security.Cryptography;
using System.Text;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

public sealed class FileArchiveService(FileManagementService authorization, IFileArchiveGateway gateway,
    IFileArchiveRepository repository, IFileArchiveProtector protector, TimeProvider clock)
{
    public async Task<FileHistoryPage> HistoryAsync(TerminalActor actor, FileHistoryCommand command, CancellationToken cancellationToken)
    {
        await authorization.AuthorizeAsync(actor, cancellationToken); FileArchiveValidation.HistoryCommand(command);
        var page = await gateway.ListHistoryAsync(command, cancellationToken); FileArchiveValidation.History(command, page);
        await authorization.AuthorizeAsync(actor, cancellationToken); await repository.ObserveAsync(page.Entries, cancellationToken); return page;
    }
    public async Task<FileArchivePlanDto> PlanAsync(TerminalActor actor, FileArchiveCommand command, CancellationToken cancellationToken)
    {
        await authorization.AuthorizeAsync(actor, cancellationToken); FileArchiveValidation.Command(command);
        var plan = await gateway.PlanArchiveAsync(actor, command, cancellationToken); FileArchiveValidation.Plan(plan);
        if (plan.Command != command) throw new InvalidOperationException("FILES_ARCHIVE_TARGET_MISMATCH");
        if (command.VersionId is not null) await ObserveSelectedVersion(actor, command, cancellationToken);
        var expiry = clock.GetUtcNow().AddMinutes(5);
        return new(protector.Sign(new(actor.Subject, actor.LoginBinding, plan, expiry)), expiry, command, plan.SizeBytes,
            command.Kind == "purge" ? "永久清理仅针对所选HDD版本，需要确认；本地私有保留对象按独立策略清理，不删除永久任务与审计。结果未知时不得自动重复删除。" :
            "仅操作计划中的文件版本；回收和恢复覆盖前必须完成完整HDD备份。恢复保持原普通权限、所有者和时间，剥离特殊权限位。结果未知时保留证据，不自动重做。");
    }
    public async Task<FileArchiveIntentDto> SubmitAsync(TerminalActor actor, SubmitFileArchiveCommand command, CancellationToken cancellationToken)
    {
        await authorization.AuthorizeAsync(actor, cancellationToken); var claims = Claims(actor, command); var id = Id(actor, command.IdempotencyKey);
        var prior = await repository.OwnedAsync(actor, id, cancellationToken);
        if (prior is not null) { Match(prior, claims); return Map(prior); }
        if (claims.ExpiresAtUtc <= clock.GetUtcNow()) throw new InvalidOperationException("FILES_PLAN_EXPIRED");
        var current = await gateway.PlanArchiveAsync(actor, claims.Plan.Command, cancellationToken);
        FileArchiveValidation.Plan(current);
        if (current != claims.Plan) throw new InvalidOperationException("FILES_VERSION_CONFLICT");
        if (claims.Plan.Command.VersionId is not null) await ObserveSelectedVersion(actor, claims.Plan.Command, cancellationToken);
        await authorization.AuthorizeAsync(actor, cancellationToken);
        var expiry = clock.GetUtcNow().AddMinutes(15); expiry = expiry.AddTicks(-(expiry.Ticks % 10));
        return Map(await repository.EnqueueAsync(actor, id, command.IdempotencyKey,
            protector.Fingerprint(new(id, actor.Subject, actor.LoginBinding, claims.Plan, expiry)), claims, expiry, cancellationToken));
    }
    public async Task<FileArchiveIntentDto?> ResolveAsync(TerminalActor actor, SubmitFileArchiveCommand command, CancellationToken cancellationToken)
    {
        await authorization.AuthorizeAsync(actor, cancellationToken); var claims = Claims(actor, command);
        var row = await repository.ResolveExpiredRegistrationAsync(actor, Id(actor, command.IdempotencyKey), claims.ExpiresAtUtc, cancellationToken);
        if (row is null) return null; Match(row, claims); return Map(row);
    }
    public async Task<FileArchiveIntentDto> GetAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken)
    { await authorization.AuthorizeAsync(actor, cancellationToken); return Map(await Owned(actor, id, cancellationToken)); }
    public async Task CancelAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken)
    { await authorization.AuthorizeAsync(actor, cancellationToken); await Owned(actor, id, cancellationToken); await repository.CancelAsync(actor, id, cancellationToken); }
    public async Task AcknowledgeAsync(TerminalActor actor, Guid id, AcknowledgeFileArchiveCommand command, CancellationToken cancellationToken)
    {
        await authorization.AuthorizeAsync(actor, cancellationToken);
        if (!command.Confirmed) throw new ArgumentException("FILES_CONFIRMATION_REQUIRED");
        var row = await Owned(actor, id, cancellationToken);
        if (row.State != "Unknown" || row.Version != command.Version) throw new InvalidOperationException("FILES_ACKNOWLEDGMENT_CONFLICT");
        var receipt = await gateway.DiscardArchiveAsync(row, cancellationToken); FileArchiveValidation.Receipt(id, FileArchiveValidation.ReadPlan(row), receipt);
        await authorization.AuthorizeAsync(actor, cancellationToken); await repository.AcknowledgeAsync(actor, id, command.Version, receipt, cancellationToken);
    }
    private FileArchivePlanClaims Claims(TerminalActor actor, SubmitFileArchiveCommand command)
    {
        if (!command.Confirmed || !Guid.TryParseExact(command.IdempotencyKey, "D", out var key) || key == Guid.Empty || key.ToString("D") != command.IdempotencyKey)
            throw new ArgumentException("FILES_CONFIRMATION_AND_KEY_REQUIRED");
        var claims = protector.VerifyArchive(command.PlanHash); FileArchiveValidation.Plan(claims.Plan);
        if (claims.Actor != actor.Subject || claims.LoginBinding != actor.LoginBinding) throw new UnauthorizedAccessException("FILES_PLAN_OWNER_REQUIRED");
        return claims;
    }
    private void Match(FileArchiveOperation row, FileArchivePlanClaims claims)
    {
        if (!FileArchiveValidation.HasValidBinding(row, protector) || row.RequestHash != protector.Fingerprint(new(row.Id, row.ActorSubject, row.LoginBinding, claims.Plan, row.ExpiresAtUtc)))
            throw new InvalidOperationException("FILES_IDEMPOTENCY_CONFLICT");
    }
    private async Task<FileArchiveOperation> Owned(TerminalActor actor, Guid id, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty) throw new ArgumentException("FILES_OPERATION_ID_REQUIRED");
        var row = await repository.OwnedAsync(actor, id, cancellationToken) ?? throw new KeyNotFoundException("FILES_OPERATION_NOT_FOUND");
        if (!FileArchiveValidation.HasValidBinding(row, protector)) throw new InvalidOperationException("FILES_INTENT_BINDING_CHANGED"); return row;
    }
    private async Task ObserveSelectedVersion(TerminalActor actor, FileArchiveCommand command, CancellationToken cancellationToken)
    {
        // An exact predecessor makes the selected UUID the first possible
        // entry, without loading the full catalog or trusting a browser row.
        var digits = command.VersionId!.Value.ToString("N").ToCharArray();
        for (var index = digits.Length - 1; index >= 0; index--)
        {
            if (digits[index] == '0') { digits[index] = 'f'; continue; }
            digits[index] = digits[index] == 'a' ? '9' : (char)(digits[index] - 1); break;
        }
        var after = digits.All(value => value == '0') ? "" : new string(digits);
        var page = await HistoryAsync(actor, new(command.Location.RootId, command.Location.Path, after, 1, ""), cancellationToken);
        if (page.Entries.SingleOrDefault()?.Id != command.VersionId) throw new KeyNotFoundException("FILES_ARCHIVE_NOT_FOUND");
    }
    private static Guid Id(TerminalActor actor, string key) => new(SHA256.HashData(Encoding.UTF8.GetBytes("files-archive:" + actor.Subject + ":" + actor.LoginBinding + ":" + key)).AsSpan(0, 16));
    public static string[] PathHashes(FileArchivePlan plan) => plan.Command.VersionId is null ? [FileManagementService.Hash(plan.Command.Location.RootId + ":" + plan.Command.Location.Path)] :
        new[] { FileManagementService.Hash(plan.Command.Location.RootId + ":" + plan.Command.Location.Path), FileManagementService.Hash("file-version-v1:" + plan.Command.VersionId.Value.ToString("N")) }.Order(StringComparer.Ordinal).ToArray();
    public static FileArchiveIntentDto Map(FileArchiveOperation row) => new(row.Id, row.Id, FileArchiveValidation.ReadPlan(row).Command,
        row.SizeBytes, row.State, row.ExpiresAtUtc, row.ResultCode, row.Version, row.PublishedVersion, row.SourceRetained);
}
