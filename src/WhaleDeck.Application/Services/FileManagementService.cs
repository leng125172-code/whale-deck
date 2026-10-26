using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

public sealed class FileManagementService(IFileGateway gateway, IFileUploadRepository repository, IFilePlanProtector plans,
    IIdentityDirectory identity, TimeProvider clock)
{
    public const long MaximumBytes = 1024L * 1024 * 1024;
    public async Task AuthorizeAsync(TerminalActor actor, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(actor.Subject) || actor.Subject.Length > 256 || actor.Subject.Any(char.IsControl) || !Hex(actor.LoginBinding) ||
            string.IsNullOrWhiteSpace(actor.TraceId) || actor.TraceId.Length > 64 || actor.TraceId.Any(char.IsControl)) throw new ArgumentException("FILES_ACTOR_INVALID");
        var current = await identity.ResolveCurrentAsync(actor.Subject, actor.Name, actor.Groups, cancellationToken);
        if (!current.IsAdministrator || current.Subject != actor.Subject || current.PermissionExpiresAtUtc <= clock.GetUtcNow())
            throw new UnauthorizedAccessException("FILES_ADMINISTRATOR_REQUIRED");
    }
    public async Task<IReadOnlyCollection<FileRootDto>> RootsAsync(TerminalActor actor, CancellationToken cancellationToken)
    { await AuthorizeAsync(actor, cancellationToken); return await gateway.RootsAsync(cancellationToken); }
    public async Task<FilePageDto> ListAsync(TerminalActor actor, FileLocationDto location, string after, int take, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken); Location(location, true);
        if (take is < 1 or > 100 || after.Any(char.IsControl) || after.Contains('/', StringComparison.Ordinal) || Encoding.UTF8.GetByteCount(after) > 255) throw new ArgumentException("FILES_LIST_INVALID");
        return await gateway.ListFilesAsync(location, after, take, cancellationToken);
    }
    public async Task<FileEntryDto> InspectAsync(TerminalActor actor, FileLocationDto location, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken); Location(location);
        return await gateway.InspectFileAsync(location, cancellationToken) ?? throw new KeyNotFoundException("FILES_NOT_FOUND");
    }
    public async Task<FileUploadPlanDto> PlanAsync(TerminalActor actor, FileUploadPlanCommand command, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken); Command(command);
        var roots = await gateway.RootsAsync(cancellationToken);
        if (!roots.Any(item => item.Id == command.RootId && item.Actions.Contains("files-upload"))) throw new UnauthorizedAccessException("FILES_ROOT_FORBIDDEN");
        var file = await gateway.InspectFileAsync(new(command.RootId, command.Path), cancellationToken);
        if (file is not null && (file.Kind != "File" || file.IsProtected)) throw new UnauthorizedAccessException("FILES_PROTECTED");
        if ((file?.Version ?? "ABSENT") != command.ExpectedVersion) throw new InvalidOperationException("FILES_VERSION_CONFLICT");
        var expires = clock.GetUtcNow().AddMinutes(5);
        return new(plans.Sign(new(actor.Subject, actor.LoginBinding, command, expires)), expires, file is not null,
            file is null ? "文件将先暂存校验，再由任务发布。不会自动重试结果未知的写入。" : "覆盖前必须完成旧版本的HDD完整备份及校验，备份盘身份或空间不满足时不会覆盖。同盘备份属于版本留存，不代表异盘冗余；当前尚无回收站或恢复入口。外部并发写入可能导致结果未知，届时不会自动重试。");
    }
    public async Task<FileUploadIntentDto> SubmitAsync(TerminalActor actor, SubmitFileUploadCommand command, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        if (!command.Confirmed || !Guid.TryParseExact(command.IdempotencyKey, "D", out var key) || key == Guid.Empty || key.ToString("D") != command.IdempotencyKey)
            throw new ArgumentException("FILES_CONFIRMATION_AND_KEY_REQUIRED");
        var claims = plans.Verify(command.PlanHash); Command(claims.File);
        if (claims.Actor != actor.Subject || claims.LoginBinding != actor.LoginBinding) throw new UnauthorizedAccessException("FILES_PLAN_OWNER_REQUIRED");
        var id = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes("files-upload:" + actor.Subject + ":" + actor.LoginBinding + ":" + key)).AsSpan(0, 16));
        var prior = await repository.OwnedAsync(actor, id, cancellationToken);
        if (prior is not null)
        {
            if (!HasValidBinding(prior, plans) || prior.RequestHash != plans.Fingerprint(new(id, actor.Subject, actor.LoginBinding, claims.File, prior.ExpiresAtUtc)))
                throw new InvalidOperationException("FILES_IDEMPOTENCY_CONFLICT");
            return Map(prior);
        }
        if (claims.ExpiresAtUtc <= clock.GetUtcNow()) throw new InvalidOperationException("FILES_PLAN_EXPIRED");
        // Revalidate current target and authority before the permanent intent.
        var target = await gateway.InspectFileAsync(new(claims.File.RootId, claims.File.Path), cancellationToken);
        if ((target?.Version ?? "ABSENT") != claims.File.ExpectedVersion) throw new InvalidOperationException("FILES_VERSION_CONFLICT");
        await AuthorizeAsync(actor, cancellationToken);
        // PostgreSQL timestamps have microsecond precision. Seal exactly the
        // stored value so re-reading the permanent intent verifies identically.
        var expiry = clock.GetUtcNow().AddMinutes(15); expiry = expiry.AddTicks(-(expiry.Ticks % 10));
        var requestHash = plans.Fingerprint(new(id, actor.Subject, actor.LoginBinding, claims.File, expiry));
        return Map(await repository.EnqueueAsync(actor, id, command.IdempotencyKey, requestHash, claims, expiry, cancellationToken));
    }
    public async Task<FileUploadIntentDto> GetAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken)
    { await AuthorizeAsync(actor, cancellationToken); return Map(await Owned(actor, id, cancellationToken)); }
    public async Task CancelAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken)
    { await AuthorizeAsync(actor, cancellationToken); await Owned(actor, id, cancellationToken); await repository.CancelAsync(actor, id, cancellationToken); }
    public async Task<FileUploadIntentDto> ReceiveAsync(TerminalActor actor, Guid id, Stream source, long? declaredLength,
        Func<CancellationToken, Task> validateLogin, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        var owned = await Owned(actor, id, cancellationToken);
        if (declaredLength != owned.SizeBytes) throw new ArgumentException("FILES_CONTENT_LENGTH_REQUIRED");
        var operation = await repository.BeginReceiveAsync(actor, id, clock.GetUtcNow(), cancellationToken);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var remaining = operation.ExpiresAtUtc - clock.GetUtcNow();
        if (remaining <= TimeSpan.Zero) throw new InvalidOperationException("FILES_UPLOAD_EXPIRED");
        lifetime.CancelAfter(remaining < TimeSpan.FromMinutes(10) ? remaining : TimeSpan.FromMinutes(10));
        var monitor = Monitor();
        try
        {
            var receipt = await gateway.StageFileAsync(operation, Bytes(source, operation.SizeBytes, lifetime.Token), lifetime.Token);
            if (receipt.State is not ("Staged" or "Completed") || receipt.Sha256 != operation.Sha256 || receipt.SizeBytes != operation.SizeBytes)
                throw new InvalidOperationException("FILES_STAGING_UNCERTAIN");
        }
        catch (OperationCanceledException) when (monitor.IsFaulted) { await monitor; throw; }
        finally
        {
            lifetime.Cancel();
            try { await monitor; } catch (OperationCanceledException) { }
        }
        // No concurrent DbContext operation remains when marking the durable state.
        await validateLogin(cancellationToken); await AuthorizeAsync(actor, cancellationToken);
        await repository.ReadyAsync(operation, cancellationToken);
        return Map(await Owned(actor, id, cancellationToken));
        async Task Monitor()
        {
            try
            {
                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(6));
                do
                {
                    await validateLogin(lifetime.Token); await AuthorizeAsync(actor, lifetime.Token);
                    if (!await repository.MayReceiveAsync(id, operation.ReceiveToken!.Value, clock.GetUtcNow(), lifetime.Token))
                        throw new InvalidOperationException("FILES_UPLOAD_STOPPED");
                } while (await timer.WaitForNextTickAsync(lifetime.Token));
            }
            catch { lifetime.Cancel(); throw; }
        }
    }
    public async IAsyncEnumerable<FileDownloadPart> DownloadAsync(TerminalActor actor, FileLocationDto location, string version,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken); Location(location); RequireHash(version);
        await repository.AuditReadAsync(actor, location, "Started", FileReadPurpose.Download, cancellationToken);
        var completed = false; var next = clock.GetUtcNow();
        try
        {
            await foreach (var part in gateway.DownloadFileAsync(location, version, cancellationToken))
            {
                if (clock.GetUtcNow() >= next) { await AuthorizeAsync(actor, cancellationToken); next = clock.GetUtcNow().AddSeconds(6); }
                yield return part;
            }
            completed = true;
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await repository.AuditReadAsync(actor, location, completed ? "Validated" : "Incomplete", FileReadPurpose.Download, cleanup.Token);
        }
    }
    private async Task<FileUploadOperation> Owned(TerminalActor actor, Guid id, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty) throw new ArgumentException("FILES_UPLOAD_ID_REQUIRED");
        var row = await repository.OwnedAsync(actor, id, cancellationToken) ?? throw new KeyNotFoundException("FILES_UPLOAD_NOT_FOUND");
        if (!HasValidBinding(row, plans)) throw new InvalidOperationException("FILES_INTENT_BINDING_CHANGED");
        return row;
    }
    public static bool HasValidBinding(FileUploadOperation row, IFilePlanProtector plans) => Hex(row.RequestHash) &&
        row.OwnerHash == Hash("file-owner-v1:" + row.ActorSubject + ":" + row.LoginBinding) && row.TargetHash == Hash(row.RootId + ":" + row.RelativePath) &&
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(row.RequestHash), Encoding.ASCII.GetBytes(plans.Fingerprint(new(row.Id, row.ActorSubject, row.LoginBinding,
            new(row.RootId, row.RelativePath, row.SizeBytes, row.Sha256, row.ExpectedVersion), row.ExpiresAtUtc))));
    internal static async IAsyncEnumerable<ReadOnlyMemory<byte>> Bytes(Stream source, long size, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var buffer = new byte[32768]; long offset = 0; int count;
        while ((count = await source.ReadAsync(buffer, cancellationToken)) != 0)
        {
            if (offset + count > size) throw new ArgumentException("FILES_SIZE_MISMATCH");
            offset += count; yield return buffer.AsMemory(0, count);
        }
        if (offset != size) throw new ArgumentException("FILES_SIZE_MISMATCH");
    }
    public static void Location(FileLocationDto value, bool empty = false)
    {
        if (value is null || value.RootId is not { Length: >= 1 and <= 64 } || value.RootId.Any(c => !(char.IsAsciiDigit(c) || c is >= 'a' and <= 'z' or '.' or '-')) ||
            value.Path is null || Encoding.UTF8.GetByteCount(value.Path) > 4096 || value.Path.Any(char.IsControl) || value.Path.Contains('\\', StringComparison.Ordinal) ||
            !(empty && value.Path.Length == 0) && value.Path.Split('/').Any(p => p is "" or "." or ".." || Encoding.UTF8.GetByteCount(p) > 255)) throw new ArgumentException("FILES_LOCATION_INVALID");
    }
    internal static void Command(FileUploadPlanCommand value)
    {
        if (value is null || value.SizeBytes is < 0 or > MaximumBytes) throw new ArgumentException("FILES_UPLOAD_INVALID");
        Location(new(value.RootId, value.Path)); RequireHash(value.Sha256);
        if (value.ExpectedVersion != "ABSENT") RequireHash(value.ExpectedVersion);
    }
    public static bool Hex(string? value) => value is { Length: 64 } && value.All(c => char.IsAsciiDigit(c) || c is >= 'A' and <= 'F');
    public static bool IsBackupFailureCode(string? code) => code is { Length: > 13 and <= 64 } && code.StartsWith("FILES_BACKUP_", StringComparison.Ordinal) && code.All(c => c is >= 'A' and <= 'Z' or '_');
    public static void ValidateBackup(FileUploadOperation operation, FileHddBackupDto? backup)
    {
        if (operation.ExpectedVersion == "ABSENT")
        { if (backup is not null) throw new FileTransferRejectedException(); return; }
        if (backup is null || backup.OperationId != operation.Id.ToString("N") || backup.StorageId != "database-platform.backup.hdd" ||
            backup.SourceVersion != operation.ExpectedVersion || !Hex(backup.Sha256) || backup.SizeBytes is < 0 or > MaximumBytes ||
            backup.CreatedAtUnixMs is < 0 or > 253402300799999) throw new FileTransferRejectedException();
    }
    private static void RequireHash(string value) { if (!Hex(value)) throw new ArgumentException("FILES_HASH_INVALID"); }
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static FileUploadIntentDto Map(FileUploadOperation row) => new(row.Id, row.Id, row.RootId, row.RelativePath, row.SizeBytes, row.Sha256, row.State,
        row.ExpiresAtUtc, row.ResultCode, row.Version, row.PublishedVersion, row.PreviousRetained);
}

public sealed class FileUploadDispatcher(IFileUploadRepository repository, IFileGateway gateway, IIdentityDirectory identity, TimeProvider clock, IFilePlanProtector plans,
    ILoginSessionRegistry logins)
{
    public async Task<bool> TickAsync(CancellationToken cancellationToken)
    {
        var operation = await repository.ClaimAsync(clock.GetUtcNow(), cancellationToken);
        if (operation is null) return false;
        try
        {
            if (!FileManagementService.HasValidBinding(operation, plans))
            { await repository.FinishAsync(operation, "Unknown", "FILES_INTENT_BINDING_CHANGED", null, cancellationToken); return true; }
            var receipt = await gateway.UploadReceiptAsync(operation, cancellationToken);
            if (receipt?.IsInProgress == true) { await repository.DeferAsync(operation, cancellationToken); return true; }
            if (receipt?.State == "Completed")
            {
                await Complete(receipt); return true;
            }
            if (receipt?.State == "Unknown")
            { await repository.FinishAsync(operation, "Unknown", "FILES_RESULT_UNKNOWN", receipt, cancellationToken); return true; }
            bool administrator;
            try { administrator = await AdministratorAsync(operation, cancellationToken); }
            catch (UnauthorizedAccessException) { administrator = false; }
            var canceled = await repository.CancellationRequestedAsync(operation.Id, cancellationToken);
            if (canceled || !administrator || operation.ExpiresAtUtc <= clock.GetUtcNow() || receipt?.State == "Aborted")
            {
                // Even before the first root receipt, write an owner-bound
                // tombstone: a delayed HTTP sender cannot stage orphan data.
                var discarded = await gateway.DiscardFileAsync(operation, cancellationToken);
                if (discarded.State == "Completed") await Complete(discarded);
                else if (discarded.State == "Aborted") await repository.FinishAsync(operation, canceled || !administrator ? "Canceled" : "Failed",
                    canceled || !administrator ? "FILES_CANCELED" : FileManagementService.IsBackupFailureCode(discarded.ResultCode) ? discarded.ResultCode! : "FILES_UPLOAD_EXPIRED_OR_ABORTED", discarded, cancellationToken);
                else await repository.FinishAsync(operation, "Unknown", "FILES_RESULT_UNKNOWN", discarded, cancellationToken);
                return true;
            }
            if (receipt?.State != "Staged") { await repository.DeferAsync(operation, cancellationToken); return true; }
            if (receipt.SizeBytes != operation.SizeBytes || receipt.Sha256 != operation.Sha256) throw new FileTransferRejectedException();
            var started = await repository.StartedAsync(operation, clock.GetUtcNow(), cancellationToken);
            if (started != FileUploadStartResult.Started) { await repository.DeferAsync(operation, cancellationToken); return true; }
            var published = await PublishWithLeaseAsync(operation, cancellationToken);
            if (published.State == "Completed") await Complete(published);
            else if (published.State == "Unknown") await repository.FinishAsync(operation, "Unknown", "FILES_RESULT_UNKNOWN", published, cancellationToken);
            else await repository.DeferAsync(operation, cancellationToken);
        }
        catch (FileTransferRejectedException)
        {
            // Reconcile/discard on the next pass; a rejection is not proof
            // that a preceding publish never happened.
            await repository.DeferAsync(operation, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        { await repository.DeferAsync(operation, cancellationToken); }
        return true;
        async Task Complete(FileUploadReceiptDto result)
        {
            if (result.SizeBytes != operation.SizeBytes || result.Sha256 != operation.Sha256 || !FileManagementService.Hex(result.Version)) throw new FileTransferRejectedException();
            try { FileManagementService.ValidateBackup(operation, result.HddBackup); }
            catch (FileTransferRejectedException)
            {
                await repository.FinishAsync(operation, "Unknown", "FILES_BACKUP_PROOF_INVALID", null, cancellationToken);
                return;
            }
            await repository.FinishAsync(operation, result.BindingCurrent ? "Succeeded" : "Superseded", result.BindingCurrent ? "FILES_PUBLISHED" : "FILES_TARGET_CHANGED", result, cancellationToken);
        }
    }

    private async Task<bool> AdministratorAsync(FileUploadOperation operation, CancellationToken cancellationToken)
    {
        var current = await identity.ResolveCurrentAsync(operation.ActorSubject, null, [], cancellationToken);
        return current.IsAdministrator && current.Subject == operation.ActorSubject && current.PermissionExpiresAtUtc > clock.GetUtcNow() &&
            await logins.IsActiveAsync(operation.ActorSubject, operation.LoginBinding, cancellationToken);
    }

    private async Task<FileUploadReceiptDto> PublishWithLeaseAsync(FileUploadOperation operation, CancellationToken cancellationToken)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lifetime.CancelAfter(TimeSpan.FromMinutes(10));
        await Guard();
        var publish = gateway.PublishFileAsync(operation, lifetime.Token);
        try
        {
            while (!publish.IsCompleted)
            {
                using var waiting = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                var interval = Task.Delay(TimeSpan.FromSeconds(6), clock, waiting.Token);
                if (await Task.WhenAny(publish, interval) == publish)
                {
                    await waiting.CancelAsync();
                    break;
                }
                await interval;
                // This does not bypass the directory's two-hour snapshot TTL.
                // Expired/unavailable authorization cannot extend publication.
                await Guard();
            }
            return await publish;
        }
        finally
        {
            // Cancel the one in-flight invocation even when a DB/identity check
            // fails. Agent stops its exact helper invocation; a lost response
            // is reconciled from durable receipts, never blind re-publication.
            await lifetime.CancelAsync();
            try { await publish; }
            catch (Exception) { /* Observe only; preserve the original outcome. */ }
        }
        async Task Guard()
        {
            using var checking = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            checking.CancelAfter(TimeSpan.FromSeconds(10));
            checking.Token.ThrowIfCancellationRequested();
            if (operation.ExpiresAtUtc <= clock.GetUtcNow() ||
                await repository.CancellationRequestedAsync(operation.Id, checking.Token) ||
                !await AdministratorAsync(operation, checking.Token) ||
                !await repository.RenewAsync(operation, clock.GetUtcNow(), checking.Token))
                throw new FileTransferRejectedException();
        }
    }
}
