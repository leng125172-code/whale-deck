using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

public sealed class FileArchiveDispatcher(IFileArchiveRepository repository, IFileArchiveGateway gateway,
    IIdentityDirectory identity, IFileArchiveProtector protector, TimeProvider clock)
{
    public async Task<bool> TickAsync(CancellationToken cancellationToken)
    {
        var row = await repository.ClaimAsync(clock.GetUtcNow(), cancellationToken); if (row is null) return false;
        try
        {
            if (!FileArchiveValidation.HasValidBinding(row, protector))
            { await repository.FinishAsync(row, "Unknown", "FILES_INTENT_BINDING_CHANGED", null, cancellationToken); return true; }
            var receipt = await Guarded(row, false, token => gateway.InspectArchiveAsync(row, token), cancellationToken);
            if (receipt is not null) { await Finish(receipt, receipt.State == "Aborted" && await repository.CancellationRequestedAsync(row.Id, cancellationToken)); return true; }
            var canceled = await repository.CancellationRequestedAsync(row.Id, cancellationToken) || !await Administrator(row, cancellationToken);
            if (canceled || row.ExpiresAtUtc <= clock.GetUtcNow() || row.RootStartedAtUtc is not null)
            { await Finish(await Guarded(row, false, async token => await gateway.DiscardArchiveAsync(row, token), cancellationToken) ?? throw new FileTransferRejectedException(), canceled); return true; }
            if (await repository.StartedAsync(row, cancellationToken) != FileUploadStartResult.Started)
            { await repository.DeferAsync(row, cancellationToken); return true; }
            await Finish(await Guarded(row, true, async token => await gateway.ApplyArchiveAsync(row, token), cancellationToken) ?? throw new FileTransferRejectedException(), false);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested) { await repository.DeferAsync(row, cancellationToken); }
        return true;
        async Task Finish(FileArchiveReceipt result, bool canceled)
        {
            FileArchiveValidation.Receipt(row.Id, FileArchiveValidation.ReadPlan(row), result);
            var state = result.State == "Completed" ? result.BindingCurrent ? "Succeeded" : "Superseded" : result.State == "Unknown" ? "Unknown" : canceled ? "Canceled" : "Failed";
            await repository.FinishAsync(row, state, state switch { "Succeeded" => "FILES_ARCHIVE_COMPLETED", "Superseded" => "FILES_TARGET_CHANGED", "Unknown" => "FILES_RESULT_UNKNOWN",
                "Canceled" => "FILES_CANCELED", _ => "FILES_OPERATION_ABORTED" }, result, cancellationToken);
        }
    }
    private async Task<bool> Administrator(FileArchiveOperation row, CancellationToken cancellationToken)
    {
        try
        {
            var user = await identity.ResolveCurrentAsync(row.ActorSubject, null, [], cancellationToken);
            return user.Subject == row.ActorSubject && user.IsAdministrator && user.PermissionExpiresAtUtc > clock.GetUtcNow();
        }
        catch (UnauthorizedAccessException) { return false; }
    }
    private async Task<FileArchiveReceipt?> Guarded(FileArchiveOperation row, bool applying,
        Func<CancellationToken, Task<FileArchiveReceipt?>> action, CancellationToken cancellationToken)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); lifetime.CancelAfter(TimeSpan.FromMinutes(10));
        await Guard(); var call = action(lifetime.Token);
        try
        {
            while (!call.IsCompleted)
            {
                using var waiting = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                var interval = Task.Delay(TimeSpan.FromSeconds(6), clock, waiting.Token);
                if (await Task.WhenAny(call, interval) == call) { await waiting.CancelAsync(); break; }
                await interval; await Guard();
            }
            return await call;
        }
        finally
        {
            // Agent cancels the exact helper invocation. A transport loss is
            // resolved from its permanent journal, never blind re-application.
            await lifetime.CancelAsync();
            try { await call; } catch (Exception) { /* Observe without sensitive exception data. */ }
        }
        async Task Guard()
        {
            using var checking = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); checking.CancelAfter(TimeSpan.FromSeconds(10));
            if (applying && (row.ExpiresAtUtc <= clock.GetUtcNow() || await repository.CancellationRequestedAsync(row.Id, checking.Token) || !await Administrator(row, checking.Token)) ||
                !await repository.RenewAsync(row, clock.GetUtcNow(), checking.Token)) throw new FileTransferRejectedException();
        }
    }
}
