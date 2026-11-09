using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;
using Retirement = WhaleDeck.Application.Services.ContainerRecycleArchiveRetirementWorkflowValidation;
using StartResult = WhaleDeck.Application.Models.ContainerFileUploadStartResult;

namespace WhaleDeck.Application.Services;

/// <summary>Only a fresh confirmed lease may enter Reserve and Apply. An entered operation is recovered by its original ID.</summary>
public sealed class ContainerRecycleArchiveRetirementDispatcher(IContainerRecycleArchiveRetirementRepository repository,
    IContainerRecycleArchiveRetirementGateway gateway, ContainerRecycleArchiveRetirementWorkflowService workflow,
    IContainerRecycleArchiveRetirementProtector protector, TimeProvider clock)
{
    public async Task<bool> TickAsync(CancellationToken token)
    {
        var row = await repository.ClaimAsync(token);
        if (row is null) return false;
        Retirement.Verify(row, protector);
        if (row.ExecutionJobId is null) await PlanAsync(row, token);
        else await ExecuteAsync(row, token);
        return true;
    }

    private async Task PlanAsync(ContainerRecycleArchiveRetirementIntent row, CancellationToken token)
    {
        try
        {
            if (row.State == "Ready" || row.PlanningExpiresAtUtc <= clock.GetUtcNow() || await Canceled(row, token))
            {
                await repository.FinishPlanningAsync(row, null, Retirement.Prefix + "PLAN_EXPIRED", token);
                return;
            }
            var source = await workflow.RequireCurrentSourceAsync(row, token);
            await gateway.RequireContainerRecycleArchiveRetirementAsync(token);
            if (!await repository.MarkPlanningAsync(row, token)) { await repository.DeferAsync(row, token); return; }
            var plan = await Guarded(row, active: true, callToken => gateway.PlanContainerRecycleArchiveRetirementAsync(
                ContainerRecycleArchiveRetirementWorkflowService.Actor(row), source.Binding.Command, callToken), token);
            await repository.FinishPlanningAsync(row, plan, Retirement.Prefix + "READY", token);
        }
        catch (Exception error) when (AuthorityRejected(error) && !token.IsCancellationRequested)
        {
            await repository.FinishPlanningAsync(row, null, AuthorityCode(error), token);
        }
        catch (Exception) when (!token.IsCancellationRequested)
        {
            if (row.PlanningExpiresAtUtc <= clock.GetUtcNow() || await Canceled(row, token))
                await repository.FinishPlanningAsync(row, null, Retirement.Prefix + "PLAN_EXPIRED", token);
            else await repository.DeferAsync(row, token);
        }
    }

    private async Task ExecuteAsync(ContainerRecycleArchiveRetirementIntent row, CancellationToken token)
    {
        var operation = Retirement.Operation(row);
        if (row.RootStartedAtUtc is not null)
        {
            await RecoverAsync(row, operation, token);
            return;
        }

        var entered = false;
        try
        {
            if (row.ExecutionExpiresAtUtc <= clock.GetUtcNow() || await Canceled(row, token))
            {
                await repository.FinishAsync(row, Retirement.Prefix + "CANCELED", null, token);
                return;
            }
            await workflow.RequireCurrentSourceAsync(row, token);
            await gateway.RequireContainerRecycleArchiveRetirementAsync(token);
            var start = await repository.StartAsync(row, token);
            if (start != StartResult.Started)
            {
                if (start is StartResult.Canceled or StartResult.Expired)
                    await repository.FinishAsync(row, Retirement.Prefix + "NOT_DUE_OR_EXPIRED", null, token);
                else await repository.DeferAsync(row, token);
                return;
            }
            entered = true;
            var reserved = await Guarded(row, active: true, ct => gateway.ReserveContainerRecycleArchiveRetirementAsync(operation, ct), token);
            ContainerRecycleArchiveRetirementValidation.Receipt(operation, reserved);
            if (reserved.State != "Reserved")
            {
                // A Completed reply with no durable Apply entry cannot prove
                // that this confirmed business execution started deletion.
                await repository.FinishAsync(row, Retirement.Prefix + "RESERVATION_REFUSED", reserved.State == "Completed" ? null : reserved, token);
                return;
            }
            if (!await repository.ReserveAsync(row, reserved, token))
            {
                await repository.DeferAsync(row, token);
                return;
            }

            await RequireActive(row, token);
            if (await repository.EnterApplyAsync(row, token) != StartResult.Started)
            {
                await repository.DeferAsync(row, token);
                return;
            }
            var receipt = await Guarded(row, active: true, ct => gateway.ApplyContainerRecycleArchiveRetirementAsync(operation, ct), token);
            ContainerRecycleArchiveRetirementValidation.Receipt(operation, receipt);
            await repository.FinishAsync(row, Retirement.Prefix + "INTERRUPTED", receipt.State == "Reserved" ? null : receipt, token);
        }
        catch (Exception error) when (!entered && AuthorityRejected(error) && !token.IsCancellationRequested)
        {
            await repository.FinishAsync(row, AuthorityCode(error), null, token);
        }
        catch (Exception) when (!token.IsCancellationRequested)
        {
            // An ambiguous Start/Reserve/Apply reply goes through the durable
            // markers on the next claim. In particular, never retry Apply.
            if (!entered && (row.ExecutionExpiresAtUtc <= clock.GetUtcNow() || await Canceled(row, token)))
                await repository.FinishAsync(row, Retirement.Prefix + "INTERRUPTED", null, token);
            else await repository.DeferAsync(row, token);
        }
    }

    private async Task RecoverAsync(ContainerRecycleArchiveRetirementIntent row, ContainerRecycleArchiveRetirementOperationDto operation, CancellationToken token)
    {
        try
        {
            // Existing committed scope permits bounded observation and
            // releasing this reservation after the original session is gone.
            // Neither native action can remove the retained data.
            var receipt = await Guarded(row, active: false, ct => gateway.InspectContainerRecycleArchiveRetirementAsync(operation, ct), token);
            if (receipt is null or { State: "Reserved" })
                receipt = await Guarded(row, active: false, ct => gateway.DiscardContainerRecycleArchiveRetirementAsync(operation, ct), token);
            ContainerRecycleArchiveRetirementValidation.Receipt(operation, receipt);
            var established = receipt.State != "Reserved" && (receipt.State != "Completed" || row.ApplyStartedAtUtc is not null);
            await repository.FinishAsync(row, Retirement.Prefix + "INTERRUPTED", established ? receipt : null, token);
        }
        catch (Exception) when (!token.IsCancellationRequested)
        {
            if (row.ExecutionExpiresAtUtc <= clock.GetUtcNow())
                await repository.FinishAsync(row, Retirement.Prefix + "RESULT_UNKNOWN", null, token);
            else await repository.DeferAsync(row, token);
        }
    }

    private Task<bool> Canceled(ContainerRecycleArchiveRetirementIntent row, CancellationToken token) => repository.CancellationRequestedAsync(row.Id, token);

    private async Task RequireActive(ContainerRecycleArchiveRetirementIntent row, CancellationToken token)
    {
        var expiry = row.ExecutionJobId is null ? row.PlanningExpiresAtUtc : row.ExecutionExpiresAtUtc;
        if (expiry <= clock.GetUtcNow() || await Canceled(row, token)) throw new FileTransferRejectedException();
        await workflow.RequireCurrentSourceAsync(row, token);
        if (expiry <= clock.GetUtcNow() || await Canceled(row, token)) throw new FileTransferRejectedException();
    }

    private async Task<T> Guarded<T>(ContainerRecycleArchiveRetirementIntent row, bool active, Func<CancellationToken, Task<T>> invoke, CancellationToken token)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        lifetime.CancelAfter(TimeSpan.FromSeconds(130));
        await CheckAsync();
        var call = invoke(lifetime.Token);
        try
        {
            while (!call.IsCompleted)
            {
                using var wait = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                var interval = Task.Delay(TimeSpan.FromSeconds(5), clock, wait.Token);
                if (await Task.WhenAny(call, interval) == call)
                {
                    await wait.CancelAsync();
                    break;
                }
                await interval;
                await CheckAsync();
            }
            var result = await call;
            await CheckAsync();
            return result;
        }
        finally
        {
            await lifetime.CancelAsync();
            try { await call; } catch (Exception) { /* The original result or guard failure is authoritative. */ }
        }

        async Task CheckAsync()
        {
            using var check = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            check.CancelAfter(TimeSpan.FromSeconds(10));
            if (active) await RequireActive(row, check.Token);
            if (!await repository.RenewAsync(row, check.Token)) throw new FileTransferRejectedException();
        }
    }

    private static bool AuthorityRejected(Exception error) => error is UnauthorizedAccessException or KeyNotFoundException ||
        error is InvalidOperationException && error.Message == Retirement.Prefix + "SOURCE_CHANGED";
    private static string AuthorityCode(Exception error) => error is UnauthorizedAccessException
        ? Retirement.Prefix + "AUTHORITY_REVOKED" : Retirement.Prefix + "SOURCE_CHANGED";
}
