using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

/// <summary>Only Inspect and idempotent Discard: an original Apply is not part of this contract.</summary>
public sealed class FileRecoveryDispatcher(IFileRecoveryRepository repository, IFileRecoveryGateway gateway,
    IIdentityDirectory identity, IFileRecoveryProtector protector, TimeProvider clock, ILoginSessionRegistry? logins = null)
{
    public async Task<bool> TickAsync(CancellationToken cancellationToken)
    {
        var row = await repository.ClaimAsync(clock.GetUtcNow(), cancellationToken); if (row is null) return false;
        try
        {
            if (!FileRecoveryValidation.Bound(row, protector)) throw new InvalidOperationException("FILES_RECOVERY_BINDING_CHANGED");
            if (!await Administrator(row, cancellationToken)) { await Fail("FILES_RECOVERY_AUTHORITY_UNAVAILABLE"); return true; }
            var deadline = row.ExecutionJobId is null ? row.PlanningExpiresAtUtc : row.ExecutionExpiresAtUtc;
            if (deadline <= clock.GetUtcNow() || row.State == "Ready") { await Fail("FILES_RECOVERY_PLAN_EXPIRED"); return true; }
            var actor = new TerminalActor(row.ActorSubject, null, [], row.LoginBinding, row.Id.ToString("D"), null);
            var snapshot = FileRecoveryValidation.Snapshot(row);
            var receipt = await Guarded(row, token => gateway.InspectForRecoveryAsync(snapshot, actor, token), cancellationToken);
            if (row.ExecutionJobId is null)
            { await repository.FinishPlanningAsync(row, receipt, "FILES_RECOVERY_READY", cancellationToken); return true; }
            // A changed observation needs a new human-approved plan. Never
            // fence newly discovered facts under an older confirmation.
            var containerSealed = row.Kind == FileRecoveryKinds.ContainerUpload && row.FenceStartedAtUtc is not null &&
                ContainerUploadRecoveryValidation.MatchesApprovedOrSealed(snapshot, receipt);
            if (receipt.ReceiptHash != row.ObservationHash && !(row.FenceStartedAtUtc is not null && row.RootState == "Missing" && receipt.State == "Aborted") && !containerSealed)
            { await Fail("FILES_RECOVERY_RECEIPT_CHANGED"); return true; }
            if (row.FenceStartedAtUtc is not null && receipt.State != "Missing")
            { await repository.FinishAsync(row, receipt, "FILES_RECOVERY_DISPOSED", cancellationToken); return true; }
            if (!await repository.BeginFenceAsync(row, cancellationToken))
            { await Fail("FILES_RECOVERY_CANCELED_OR_EXPIRED"); return true; }
            var fenced = await Guarded(row, token => gateway.FenceForRecoveryAsync(snapshot, actor, token), cancellationToken);
            await repository.FinishAsync(row, fenced, "FILES_RECOVERY_DISPOSED", cancellationToken);
        }
        catch (RecoveryGuardRejectedException) when (!cancellationToken.IsCancellationRequested)
        { await Fail("FILES_RECOVERY_AUTHORITY_OR_LEASE_LOST"); }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested && OriginalChanged(error))
        { await Fail("FILES_RECOVERY_ORIGINAL_CHANGED"); }
        catch (InvalidOperationException error) when (!cancellationToken.IsCancellationRequested && error.Message == "FILES_RECOVERY_DEADLINE_EXPIRED")
        { await Fail("FILES_RECOVERY_PLAN_EXPIRED"); }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        { await repository.DeferAsync(row, cancellationToken); }
        return true;

        Task<bool> Fail(string code) => row.ExecutionJobId is null
            ? repository.FinishPlanningAsync(row, null, code, cancellationToken)
            : repository.FinishAsync(row, null, code, cancellationToken);
    }
    private static bool OriginalChanged(Exception error) => error is KeyNotFoundException || error is InvalidOperationException &&
        error.Message is "FILES_RECOVERY_ORIGINAL_CHANGED" or "FILES_RECOVERY_ORIGINAL_NOT_UNKNOWN" or "FILES_INTENT_BINDING_CHANGED";
    private async Task<bool> Administrator(FileRecoveryOperation row, CancellationToken cancellationToken)
    {
        try
        {
            if (row.Kind == FileRecoveryKinds.ContainerUpload && (logins is null ||
                !await logins.IsActiveAsync(row.ActorSubject, row.LoginBinding, cancellationToken))) return false;
            var user = await identity.ResolveCurrentAsync(row.ActorSubject, null, [], cancellationToken);
            return user.Subject == row.ActorSubject && user.IsAdministrator && user.PermissionExpiresAtUtc > clock.GetUtcNow();
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested) { return false; }
    }
    private async Task<T> Guarded<T>(FileRecoveryOperation row, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
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
            var result = await call; await Guard(); return result;
        }
        finally
        {
            await lifetime.CancelAsync();
            try { await call; } catch (Exception) { /* Observe only. A lost reply is not a successful fence. */ }
        }
        async Task Guard()
        {
            using var checking = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); checking.CancelAfter(TimeSpan.FromSeconds(10));
            var deadline = row.ExecutionJobId is null ? row.PlanningExpiresAtUtc : row.ExecutionExpiresAtUtc;
            if (deadline <= clock.GetUtcNow() || !await Administrator(row, checking.Token) ||
                !await repository.RenewAsync(row, clock.GetUtcNow(), checking.Token)) throw new RecoveryGuardRejectedException();
        }
    }
    private sealed class RecoveryGuardRejectedException : InvalidOperationException;
}
