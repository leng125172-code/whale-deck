using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

/// <summary>A publication is invoked once; subsequent claims observe or fence it, never resend it.</summary>
public sealed class ContainerFileUploadDispatcher(IContainerFileUploadRepository repository, IContainerFileWriteGateway gateway,
    ContainerFileUploadService authorization, IContainerFileUploadProtector plans, TimeProvider clock, IContainerFileHistoryGateway? history = null)
{
    public async Task<bool> TickAsync(CancellationToken cancellationToken)
    {
        var row = await repository.ClaimAsync(clock.GetUtcNow(), cancellationToken);
        if (row is null) return false;
        try
        {
            if (!ContainerFileUploadService.HasValidBinding(row, plans))
            { await Unknown("CONTAINER_UPLOAD_BINDING_CHANGED"); return true; }
            var reference = ContainerFileUploadIntentValidation.Reference(row);
            var receipt = await gateway.InspectContainerUploadAsync(reference, cancellationToken);
            if (receipt is not null) Identity(receipt);
            // Already-performed publication is a fact, even after logout. It
            // must be reconciled, not replayed or relabeled as a cancellation.
            if (receipt?.Upload.State == "Published") { await Complete(receipt); return true; }
            if (receipt?.Upload.State == "Unknown") { await Unknown("CONTAINER_UPLOAD_RESULT_UNKNOWN"); return true; }
            var canceled = await repository.CancellationRequestedAsync(row.Id, cancellationToken);
            var administrator = true;
            try { await Authority(row, cancellationToken); }
            catch (UnauthorizedAccessException) { administrator = false; }
            if (canceled || !administrator || row.ExpiresAtUtc <= clock.GetUtcNow())
            {
                await Prevent(canceled || !administrator ? "Canceled" : "Failed",
                    canceled || !administrator ? "CONTAINER_UPLOAD_CANCELED" : "CONTAINER_UPLOAD_EXPIRED");
                return true;
            }
            if (receipt?.Upload.State is "PublishPreparing" or "PublishApplying" or "PublishDiscarding")
            { await repository.DeferAsync(row, cancellationToken); return true; }
            if (receipt?.Upload.State == "PublishInterrupted")
            { await Unknown("CONTAINER_UPLOAD_RESULT_UNKNOWN"); return true; }
            if (row.RootStartedAtUtc is not null)
            {
                // A previous worker may have lost its connection before the
                // request reached Root. Write the durable fence to reject a
                // delayed request; only positive discard proof can close it.
                await Prevent("Failed", "CONTAINER_UPLOAD_INTERRUPTED"); return true;
            }
            if (receipt?.Upload.State is "Interrupted" or "Aborted" or "Discarded" or "Prevented" or "PublishDiscarded")
            { await Prevent("Failed", "CONTAINER_UPLOAD_INTERRUPTED"); return true; }
            if (row.HistorySourceJson is not null && row.State is "AwaitingUpload" or "Receiving")
            {
                if (history is null) throw new InvalidOperationException("CONTAINER_HISTORY_CAPABILITY_UNAVAILABLE");
                if (row.State == "AwaitingUpload" && receipt is null)
                {
                    await history.RequireContainerHistoryWorkflowAsync(cancellationToken);
                    await gateway.RequireContainerUploadWorkflowAsync(cancellationToken);
                    if (!await repository.BeginRestoreReceiveAsync(row, clock.GetUtcNow(), cancellationToken))
                    { await repository.DeferAsync(row, cancellationToken); return true; }
                    var source = ContainerFileRestoreValidation.Read(row.HistorySourceJson);
                    var location = ContainerFileRestoreValidation.SourceLocation(new(row.ContainerId, row.InstanceId, row.RelativePath), source);
                    receipt = await TransferWithLease(row, token => gateway.StageContainerFileAsync(ContainerFileUploadIntentValidation.Stage(row),
                        history.ReadContainerHistorySourceAsync(location, source, token), token), cancellationToken);
                    Identity(receipt);
                }
                // Only a positive full Stage receipt can advance a restored
                // source, including after a Worker crash just after commit.
                // Missing/partial source is fenced; it is never read a second
                // time into the same durable operation.
                if (row.State != "Receiving" || receipt?.Upload.State != "Staged")
                { await Prevent("Failed", "CONTAINER_UPLOAD_INTERRUPTED"); return true; }
                if (receipt.Upload.SizeBytes != row.SizeBytes || receipt.Upload.Sha256 != row.Sha256 || receipt.Upload.HddBackup is not null ||
                    receipt.Upload.PreviousRetained || receipt.Upload.BindingCurrent || receipt.Upload.IsInProgress || receipt.Upload.Version.Length != 0)
                { await Unknown("CONTAINER_UPLOAD_OUTPUT_INVALID"); return true; }
                await Authority(row, cancellationToken);
                if (!await repository.ReadyAsync(row, cancellationToken))
                { await repository.DeferAsync(row, cancellationToken); return true; }
                row.State = "Ready";
            }
            if (receipt is null && row.State == "Ready")
            { await Unknown("CONTAINER_UPLOAD_STAGING_LOST"); return true; }
            if (receipt?.Upload.State != "Staged" || row.State != "Ready")
            { await repository.DeferAsync(row, cancellationToken); return true; }
            if (receipt.Upload.SizeBytes != row.SizeBytes || receipt.Upload.Sha256 != row.Sha256)
            { await Unknown("CONTAINER_UPLOAD_OUTPUT_INVALID"); return true; }
            await gateway.RequireContainerUploadWorkflowAsync(cancellationToken);
            var started = await repository.StartedAsync(row, clock.GetUtcNow(), cancellationToken);
            if (started != ContainerFileUploadStartResult.Started)
            { await repository.DeferAsync(row, cancellationToken); return true; }
            var published = await PublishWithLease(row, reference, cancellationToken); Identity(published);
            if (published.Upload.State == "Published") await Complete(published);
            else if (published.Upload.State is "Prevented" or "Discarded" or "PublishDiscarded")
                await repository.FinishAsync(row, "Failed", "CONTAINER_UPLOAD_INTERRUPTED", published, cancellationToken);
            else await Unknown("CONTAINER_UPLOAD_RESULT_UNKNOWN");
            return true;

            async Task Unknown(string code) => _ = await repository.FinishAsync(row, "Unknown", code, null, cancellationToken);
            void Identity(ContainerFileUploadReceiptDto value)
            {
                if (value.ContainerId != row.ContainerId || value.InstanceId != row.InstanceId || value.OperationId != row.Id || value.TargetProof != row.TargetProof)
                    throw new InvalidOperationException("CONTAINER_UPLOAD_OUTPUT_INVALID");
            }
            async Task Complete(ContainerFileUploadReceiptDto value)
            {
                try
                {
                    await repository.FinishAsync(row, value.Upload.BindingCurrent ? "Succeeded" : "Superseded",
                        value.Upload.BindingCurrent ? "CONTAINER_UPLOAD_PUBLISHED" : "CONTAINER_UPLOAD_TARGET_CHANGED", value, cancellationToken);
                }
                catch (ArgumentException) { await Unknown("CONTAINER_UPLOAD_OUTPUT_INVALID"); }
            }
            async Task Prevent(string state, string code)
            {
                // This requires the installed pre-receive fence capability.
                // A lost or busy response never means "successfully canceled".
                ContainerFileUploadReceiptDto? discarded;
                try { discarded = await gateway.PreventContainerUploadAsync(reference, cancellationToken); }
                catch (InvalidOperationException error) when (error.Message == "CONTAINER_UPLOAD_OUTPUT_INVALID")
                {
                    // The fence transport only accepts positive discard
                    // states. A publication can win the race; observe the
                    // durable facts once rather than calling it canceled.
                    discarded = await gateway.InspectContainerUploadAsync(reference, cancellationToken);
                }
                if (discarded is null) { await Unknown("CONTAINER_UPLOAD_RESULT_UNKNOWN"); return; }
                Identity(discarded);
                if (discarded.Upload.State == "Published") await Complete(discarded);
                else if (discarded.Upload.State is "Prevented" or "Discarded" or "PublishDiscarded")
                    await repository.FinishAsync(row, state, code, discarded, cancellationToken);
                else await Unknown("CONTAINER_UPLOAD_RESULT_UNKNOWN");
            }
        }
        catch (ContainerFileUploadTargetUnavailableException) when (!cancellationToken.IsCancellationRequested)
        { await repository.FinishAsync(row, "Unknown", "CONTAINER_UPLOAD_TARGET_UNAVAILABLE", null, cancellationToken); return true; }
        catch (InvalidOperationException error) when (error.Message == "CONTAINER_UPLOAD_OUTPUT_INVALID")
        { await repository.FinishAsync(row, "Unknown", "CONTAINER_UPLOAD_OUTPUT_INVALID", null, cancellationToken); return true; }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            if (row.ExpiresAtUtc <= clock.GetUtcNow())
                await repository.FinishAsync(row, "Unknown", "CONTAINER_UPLOAD_RESULT_UNKNOWN", null, cancellationToken);
            else await repository.DeferAsync(row, cancellationToken);
            return true;
        }
    }

    private Task Authority(ContainerFileUploadOperation row, CancellationToken cancellationToken) =>
        authorization.AuthorizeAsync(new(row.ActorSubject, null, [], row.LoginBinding, row.TraceId, null), cancellationToken);

    private Task<ContainerFileUploadReceiptDto> PublishWithLease(ContainerFileUploadOperation row, ContainerFileUploadReferenceDto reference, CancellationToken cancellationToken) =>
        TransferWithLease(row, token => gateway.PublishContainerFileAsync(reference, token), cancellationToken);

    private async Task<ContainerFileUploadReceiptDto> TransferWithLease(ContainerFileUploadOperation row,
        Func<CancellationToken, Task<ContainerFileUploadReceiptDto>> transfer, CancellationToken cancellationToken)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var remaining = row.ExpiresAtUtc - clock.GetUtcNow();
        if (remaining <= TimeSpan.Zero) throw new FileTransferRejectedException();
        lifetime.CancelAfter(remaining < TimeSpan.FromMinutes(10) ? remaining : TimeSpan.FromMinutes(10));
        await Guard();
        var publish = transfer(lifetime.Token);
        try
        {
            while (!publish.IsCompleted)
            {
                using var waiting = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                var interval = Task.Delay(TimeSpan.FromSeconds(6), clock, waiting.Token);
                if (await Task.WhenAny(publish, interval) == publish)
                { await waiting.CancelAsync(); break; }
                await interval; await Guard();
            }
            return await publish;
        }
        finally
        {
            await lifetime.CancelAsync();
            try { await publish; } catch (Exception) { /* Observe the sole invocation; retain the original failure. */ }
        }
        async Task Guard()
        {
            using var checking = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); checking.CancelAfter(TimeSpan.FromSeconds(10));
            await Authority(row, checking.Token);
            if (row.ExpiresAtUtc <= clock.GetUtcNow() || !ContainerFileUploadService.HasValidBinding(row, plans) ||
                await repository.CancellationRequestedAsync(row.Id, checking.Token) || !await repository.RenewAsync(row, clock.GetUtcNow(), checking.Token))
                throw new FileTransferRejectedException();
        }
    }
}
