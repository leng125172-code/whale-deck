using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

public sealed class ContainerDirectoryDispatcher(IContainerDirectoryRepository repository, IContainerDirectoryGateway gateway,
    ContainerDirectoryWorkflowService authorization, IContainerDirectoryProtector protector, TimeProvider clock)
{
    public async Task<bool> TickAsync(CancellationToken cancellationToken)
    {
        var row = await repository.ClaimAsync(clock.GetUtcNow(), cancellationToken); if (row is null) return false;
        try
        {
            if (!ContainerDirectoryWorkflowService.HasValidBinding(row, protector)) { await Unknown("CONTAINER_DIRECTORY_BINDING_CHANGED"); return true; }
            var operation = ContainerDirectoryWorkflowValidation.Reference(row);
            var observed = await gateway.InspectContainerDirectoryAsync(operation, cancellationToken);
            if (observed is not null) { await Finish(observed, await repository.CancellationRequestedAsync(row.Id, cancellationToken)); return true; }
            var canceled = await repository.CancellationRequestedAsync(row.Id, cancellationToken);
            var allowed = true;
            try { await Authority(row, cancellationToken); } catch (UnauthorizedAccessException) { allowed = false; }
            if (canceled || !allowed || row.ExpiresAtUtc <= clock.GetUtcNow() || row.RootStartedAtUtc is not null)
            {
                var fenced = await gateway.DiscardContainerDirectoryAsync(operation, cancellationToken);
                await Finish(fenced, canceled || !allowed); return true;
            }
            await gateway.RequireContainerDirectoryWorkflowAsync(cancellationToken);
            var start = await repository.StartedAsync(row, clock.GetUtcNow(), cancellationToken);
            if (start != ContainerFileUploadStartResult.Started) { await repository.DeferAsync(row, cancellationToken); return true; }
            var receipt = await ApplyWithLease(row, operation, cancellationToken);
            await Finish(receipt, false); return true;
            async Task Finish(ContainerDirectoryReceiptDto receipt, bool cancellation)
            {
                ContainerDirectoryValidation.Receipt(operation, receipt);
                var state = receipt.State == "Completed" ? receipt.BindingCurrent ? "Succeeded" : "Superseded" : receipt.State == "Unknown" ? "Unknown" : cancellation ? "Canceled" : "Failed";
                var code = state switch { "Succeeded" => "CONTAINER_DIRECTORY_CREATED", "Superseded" => "CONTAINER_DIRECTORY_TARGET_CHANGED",
                    "Unknown" => "CONTAINER_DIRECTORY_RESULT_UNKNOWN", "Canceled" => "CONTAINER_DIRECTORY_CANCELED", _ => "CONTAINER_DIRECTORY_INTERRUPTED" };
                await repository.FinishAsync(row, state, code, receipt, cancellationToken);
            }
            async Task Unknown(string code) => _ = await repository.FinishAsync(row, "Unknown", code, null, cancellationToken);
        }
        catch (InvalidOperationException error) when (error.Message == "CONTAINER_DIRECTORY_OUTPUT_INVALID")
        { await repository.FinishAsync(row, "Unknown", "CONTAINER_DIRECTORY_OUTPUT_INVALID", null, cancellationToken); return true; }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            if (row.ExpiresAtUtc <= clock.GetUtcNow()) await repository.FinishAsync(row, "Unknown", "CONTAINER_DIRECTORY_RESULT_UNKNOWN", null, cancellationToken);
            else await repository.DeferAsync(row, cancellationToken);
            return true;
        }
    }
    private Task Authority(ContainerDirectoryOperation row, CancellationToken token) => authorization.AuthorizeAsync(new(row.ActorSubject, null, [], row.LoginBinding, row.TraceId, null), token);
    private async Task<ContainerDirectoryReceiptDto> ApplyWithLease(ContainerDirectoryOperation row, ContainerDirectoryOperationDto operation, CancellationToken cancellationToken)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); lifetime.CancelAfter(TimeSpan.FromSeconds(35));
        await Guard(); var apply = gateway.ApplyContainerDirectoryAsync(operation, lifetime.Token);
        try
        {
            while (!apply.IsCompleted)
            {
                using var wait = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                var delay = Task.Delay(TimeSpan.FromSeconds(6), clock, wait.Token);
                if (await Task.WhenAny(apply, delay) == apply) { await wait.CancelAsync(); break; }
                await delay; await Guard();
            }
            return await apply;
        }
        finally { await lifetime.CancelAsync(); try { await apply; } catch (Exception) { } }
        async Task Guard()
        {
            await Authority(row, lifetime.Token);
            if (row.ExpiresAtUtc <= clock.GetUtcNow() || await repository.CancellationRequestedAsync(row.Id, lifetime.Token) ||
                !await repository.RenewAsync(row, clock.GetUtcNow(), lifetime.Token)) throw new FileTransferRejectedException();
        }
    }
}
