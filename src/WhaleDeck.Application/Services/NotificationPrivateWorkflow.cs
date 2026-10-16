using System.Security.Cryptography;
using System.Text;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Services;

public sealed class NotificationPrivateWorkflow(INotificationPrivateRepository repository, INotificationPrivateGateway agent,
    IOneTimeSecretStore staged, INotificationPrivateInputStore inputs, INotificationPrivateProtector protector,
    INotificationPrivateValidator validator, TimeProvider clock)
{
    public async Task<NotificationPrivateMetadata> InspectAsync(NotificationContext context, Guid channelId, CancellationToken cancellationToken)
    {
        Authorize(context); Identity(channelId);
        var channel = await repository.ChannelAsync(channelId, cancellationToken);
        return (await agent.InspectAsync(channelId, cancellationToken)) with { ChannelVersion = channel.Version };
    }

    public async Task<NotificationPrivatePlanDto> PlanAsync(NotificationContext context, Guid channelId, NotificationPrivatePlanCommand command, CancellationToken cancellationToken)
    {
        Authorize(context); Identity(channelId);
        if (command.ExpectedChannelVersion is < 0 or >= long.MaxValue - 2 || command.Action is not ("apply" or "remove")) throw new ArgumentException("Invalid private configuration plan.");
        var channel = await repository.ChannelAsync(channelId, cancellationToken);
        if (channel.Version != command.ExpectedChannelVersion) throw new InvalidOperationException("The notification channel version changed.");
        var metadata = await agent.InspectAsync(channelId, cancellationToken);
        var input = await Input(context.Subject, command.Action, command.InputTicket, cancellationToken);
        validator.Validate(channel.Provider, command.Action, input, metadata.HasCredential);
        var expires = clock.GetUtcNow().AddMinutes(5);
        var claims = new NotificationPrivatePlanClaims(context.Subject, channelId, channel.Version, channel.Provider, command.Action,
            metadata.Revision, protector.Fingerprint(context.Subject, channelId, command.Action, input), expires);
        return new(protector.Sign(claims), expires, metadata.HasTarget, metadata.HasCredential,
            "保存将先停用渠道并取消旧待发送意图；完成后仍保持停用。目标、凭据不回显，请核对后确认；已发送消息不能撤回。");
    }

    public async Task<JobDto> SubmitAsync(NotificationContext context, Guid channelId, NotificationPrivateSubmitCommand command, CancellationToken cancellationToken)
    {
        Authorize(context); Identity(channelId);
        if (!command.Confirmed || !Guid.TryParseExact(context.IdempotencyKey, "D", out var key) || key == Guid.Empty) throw new ArgumentException("Explicit confirmation and a UUID idempotency key are required.");
        var claims = protector.Verify(command.PlanHash);
        if (claims.Actor != context.Subject || claims.ChannelId != channelId) throw new UnauthorizedAccessException("The private plan belongs to another actor or channel.");
        var id = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes("notification-private:" + context.Subject + ":" + key.ToString("D"))).AsSpan(0, 16));
        var requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(command.PlanHash)));
        // A durable exact replay is valid after the input/plan expires; it never
        // touches the root writer or creates another operation.
        var replay = await repository.ReplayAsync(id, context.Subject, requestHash, cancellationToken);
        if (replay is not null) return replay;
        if (claims.ExpiresAtUtc <= clock.GetUtcNow()) throw new InvalidOperationException("The private plan expired. Prepare a new plan.");
        var input = await Input(context.Subject, claims.Action, command.InputTicket, cancellationToken);
        if (protector.Fingerprint(context.Subject, channelId, claims.Action, input) != claims.InputFingerprint) throw new InvalidOperationException("The staged private input differs from the confirmed plan.");
        await inputs.PutAsync(context.Subject, id, input, claims.ExpiresAtUtc, cancellationToken);
        var job = await repository.EnqueueAsync(context, id, claims, requestHash, cancellationToken);
        if (command.InputTicket is not null) await staged.ConsumeAsync(context.Subject, command.InputTicket, cancellationToken);
        return job;
    }

    private Task<string?> Staged(string owner, string? ticket, CancellationToken cancellationToken) => ticket is null ? Task.FromResult<string?>(null) : staged.PeekAsync(owner, ticket, cancellationToken);
    public async Task<NotificationPrivateStatusDto?> StatusAsync(NotificationContext context, Guid channelId, CancellationToken cancellationToken)
    {
        Authorize(context); Identity(channelId);
        var operation = await repository.LatestAsync(channelId, cancellationToken);
        return operation is null ? null : new(operation.Id, operation.State, operation.ResultCode, operation.Version,
            operation.State == "Unknown" && operation.RootStartedAtUtc <= clock.GetUtcNow().AddSeconds(-90) && (operation.LeaseExpiresAtUtc is null || operation.LeaseExpiresAtUtc <= clock.GetUtcNow()));
    }
    public async Task AcknowledgeAsync(NotificationContext context, Guid channelId, AcknowledgeNotificationPrivateCommand command, CancellationToken cancellationToken)
    {
        Authorize(context); Identity(channelId); Identity(command.OperationId);
        if (!command.Confirmed || !command.AcceptUnknownOutcome || command.ExpectedVersion < 0 || !Guid.TryParseExact(context.IdempotencyKey, "D", out var key) || key == Guid.Empty) throw new ArgumentException("Unknown outcomes require explicit acceptance and a UUID request key.");
        var receipt = await agent.InspectReceiptAsync(channelId, command.OperationId, cancellationToken);
        if (receipt.Applied && receipt.BindingCurrent) throw new InvalidOperationException("The root result is available; let reconciliation complete instead.");
        await repository.AcknowledgeAsync(context, channelId, command.OperationId, command.ExpectedVersion, cancellationToken);
        // This only releases the old intent lock. Sending remains blocked until
        // a new confirmed private configuration has been successfully reconciled.
    }
    private async Task<string> Input(string owner, string action, string? ticket, CancellationToken cancellationToken)
    {
        if (action == "remove") { if (ticket is not null) throw new ArgumentException("Removal does not accept private input."); return "{}"; }
        return await Staged(owner, ticket, cancellationToken) ?? throw new InvalidOperationException("The owner-bound private input expired or is unavailable.");
    }
    private static void Authorize(NotificationContext context)
    { if (!context.IsAdministrator || string.IsNullOrWhiteSpace(context.Subject)) throw new UnauthorizedAccessException("Administrator permission is required."); }
    private static void Identity(Guid id) { if (id == Guid.Empty) throw new ArgumentException("A notification channel is required."); }
}

/// <summary>Root I/O is outside retryable SQL transactions; always reconcile before re-dispatch.</summary>
public sealed class NotificationPrivateDispatcher(INotificationPrivateRepository repository, INotificationPrivateGateway agent,
    INotificationPrivateInputStore inputs, INotificationRequestAuthorizer authorizer, INotificationPrivateProtector protector, TimeProvider clock)
{
    public async Task<bool> TickAsync(CancellationToken cancellationToken)
    {
        var operation = await repository.ClaimAsync(clock.GetUtcNow(), cancellationToken);
        if (operation is null) return false;
        var receipt = await agent.InspectReceiptAsync(operation.ChannelId, operation.Id, cancellationToken);
        if (receipt.Applied)
        {
            await repository.FinishAsync(operation, receipt.BindingCurrent ? "Succeeded" : "Superseded", receipt.BindingCurrent ? "PRIVATE_APPLIED" : "PRIVATE_SUPERSEDED", receipt, cancellationToken);
            await inputs.DeleteAsync(operation.ActorSubject, operation.Id, cancellationToken); return true;
        }
        if (await repository.CancellationRequestedAsync(operation.Id, cancellationToken) || !await authorizer.IsAdministratorAsync(operation.ActorSubject, cancellationToken))
        {
            await repository.FinishAsync(operation, operation.RootStartedAtUtc is null ? "Canceled" : "Unknown", "PRIVATE_PERMISSION_OR_CANCEL", null, cancellationToken); return true;
        }
        var input = operation.Action == "remove" ? "{}" : await inputs.GetAsync(operation.ActorSubject, operation.Id, cancellationToken);
        if (input is null || operation.InputExpiresAtUtc <= clock.GetUtcNow())
        {
            await repository.FinishAsync(operation, operation.RootStartedAtUtc is null ? "Failed" : "Unknown", "PRIVATE_INPUT_EXPIRED", null, cancellationToken); return true;
        }
        if (protector.Fingerprint(operation.ActorSubject, operation.ChannelId, operation.Action, input) != operation.InputFingerprint)
        { await repository.FinishAsync(operation, "Unknown", "PRIVATE_INPUT_CHANGED", null, cancellationToken); return true; }
        if (!await repository.StartedAsync(operation, clock.GetUtcNow(), cancellationToken)) return true;
        try
        {
            var result = await agent.ApplyAsync(new() { ChannelId = operation.ChannelId, OperationId = operation.Id, ExpectedRevision = operation.RegistryRevision,
                Action = operation.Action, ActorSubject = operation.ActorSubject, PrivateJson = input }, cancellationToken);
            await repository.FinishAsync(operation, result.BindingCurrent ? "Succeeded" : "Superseded", result.BindingCurrent ? "PRIVATE_APPLIED" : "PRIVATE_SUPERSEDED", result, cancellationToken);
            await inputs.DeleteAsync(operation.ActorSubject, operation.Id, cancellationToken);
        }
        catch (NotificationPrivateRejectedException)
        { await repository.FinishAsync(operation, "Failed", "PRIVATE_REJECTED", null, cancellationToken); await inputs.DeleteAsync(operation.ActorSubject, operation.Id, cancellationToken); }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        { await repository.DeferAsync(operation, "PRIVATE_AWAITING_RECEIPT", cancellationToken); }
        return true;
    }
}
