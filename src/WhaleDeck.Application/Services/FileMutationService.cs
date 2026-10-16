using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

public sealed class FileMutationService(FileManagementService authorization, IFileMutationGateway gateway,
    IFileMutationRepository repository, IFileMutationProtector protector, TimeProvider clock)
{
    public static readonly JsonSerializerOptions JsonOptions = new() { MaxDepth = 12, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    public static FileMutationPlan ReadPlan(FileMutationOperation operation) =>
        JsonSerializer.Deserialize<FileMutationPlan>(operation.PlanJson, JsonOptions) ?? throw new InvalidOperationException("FILES_PLAN_INVALID");
    public async Task<FileMutationPlanDto> PlanAsync(TerminalActor actor, FileMutationCommand command, CancellationToken cancellationToken)
    {
        await authorization.AuthorizeAsync(actor, cancellationToken); Validate(command);
        var plan = await gateway.PlanMutationAsync(actor, command, cancellationToken); Validate(plan);
        if (plan.Command != command) throw new InvalidOperationException("FILES_PLAN_TARGET_MISMATCH");
        var expiry = clock.GetUtcNow().AddMinutes(5);
        return new(protector.Sign(new(actor.Subject, actor.LoginBinding, plan, expiry)), expiry, command, plan.SizeBytes,
            "仅操作计划中的版本，不覆盖已有目标。移动会私有保留原文件；结果未知时不会自动重做，需要人工核实。权限仅修改普通模式位，不修改所有者。此私有保留不是HDD备份或回收站。");
    }
    public async Task<FileMutationIntentDto> SubmitAsync(TerminalActor actor, SubmitFileMutationCommand command, CancellationToken cancellationToken)
    {
        await authorization.AuthorizeAsync(actor, cancellationToken);
        if (!command.Confirmed || !Guid.TryParseExact(command.IdempotencyKey, "D", out var key) || key == Guid.Empty || key.ToString("D") != command.IdempotencyKey)
            throw new ArgumentException("FILES_CONFIRMATION_AND_KEY_REQUIRED");
        var claims = protector.VerifyMutation(command.PlanHash); Validate(claims.Plan);
        if (claims.Actor != actor.Subject || claims.LoginBinding != actor.LoginBinding) throw new UnauthorizedAccessException("FILES_PLAN_OWNER_REQUIRED");
        var id = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes("files-mutation:" + actor.Subject + ":" + actor.LoginBinding + ":" + key)).AsSpan(0, 16));
        var prior = await repository.OwnedAsync(actor, id, cancellationToken);
        if (prior is not null)
        {
            if (!HasValidBinding(prior, protector) || prior.RequestHash != protector.Fingerprint(new(id, actor.Subject, actor.LoginBinding, claims.Plan, prior.ExpiresAtUtc)))
                throw new InvalidOperationException("FILES_IDEMPOTENCY_CONFLICT");
            return Map(prior);
        }
        if (claims.ExpiresAtUtc <= clock.GetUtcNow()) throw new InvalidOperationException("FILES_PLAN_EXPIRED");
        var current = await gateway.PlanMutationAsync(actor, claims.Plan.Command, cancellationToken);
        if (current != claims.Plan) throw new InvalidOperationException("FILES_VERSION_CONFLICT");
        await authorization.AuthorizeAsync(actor, cancellationToken);
        var expiry = clock.GetUtcNow().AddMinutes(15); expiry = expiry.AddTicks(-(expiry.Ticks % 10));
        return Map(await repository.EnqueueAsync(actor, id, command.IdempotencyKey,
            protector.Fingerprint(new(id, actor.Subject, actor.LoginBinding, claims.Plan, expiry)), claims, expiry, cancellationToken));
    }
    public async Task<FileMutationIntentDto> GetAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken)
    { await authorization.AuthorizeAsync(actor, cancellationToken); return Map(await Owned(actor, id, cancellationToken)); }
    public async Task<FileMutationIntentDto?> ResolveAsync(TerminalActor actor, SubmitFileMutationCommand command, CancellationToken cancellationToken)
    {
        await authorization.AuthorizeAsync(actor, cancellationToken);
        if (!command.Confirmed || !Guid.TryParseExact(command.IdempotencyKey, "D", out var key) || key == Guid.Empty || key.ToString("D") != command.IdempotencyKey)
            throw new ArgumentException("FILES_CONFIRMATION_AND_KEY_REQUIRED");
        var claims = protector.VerifyMutation(command.PlanHash); Validate(claims.Plan);
        if (claims.Actor != actor.Subject || claims.LoginBinding != actor.LoginBinding) throw new UnauthorizedAccessException("FILES_PLAN_OWNER_REQUIRED");
        var id = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes("files-mutation:" + actor.Subject + ":" + actor.LoginBinding + ":" + key)).AsSpan(0, 16));
        var row = await repository.ResolveExpiredRegistrationAsync(actor, id, claims.ExpiresAtUtc, cancellationToken);
        if (row is null) return null;
        if (!HasValidBinding(row, protector) || row.RequestHash != protector.Fingerprint(new(id, actor.Subject, actor.LoginBinding, claims.Plan, row.ExpiresAtUtc)))
            throw new InvalidOperationException("FILES_IDEMPOTENCY_CONFLICT");
        return Map(row);
    }
    public async Task CancelAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken)
    { await authorization.AuthorizeAsync(actor, cancellationToken); await Owned(actor, id, cancellationToken); await repository.CancelAsync(actor, id, cancellationToken); }
    public async Task AcknowledgeAsync(TerminalActor actor, Guid id, AcknowledgeFileMutationCommand command, CancellationToken cancellationToken)
    {
        await authorization.AuthorizeAsync(actor, cancellationToken);
        if (!command.Confirmed) throw new ArgumentException("FILES_CONFIRMATION_REQUIRED");
        var row = await Owned(actor, id, cancellationToken);
        if (row.State != "Unknown" || row.Version != command.Version) throw new InvalidOperationException("FILES_ACKNOWLEDGMENT_CONFLICT");
        // Must first fence any delayed Apply at the root. Never turn a busy or
        // missing observation into a false cancellation/success assertion.
        var receipt = await gateway.DiscardMutationAsync(row, cancellationToken);
        ValidateReceipt(ReadPlan(row), receipt);
        await authorization.AuthorizeAsync(actor, cancellationToken);
        await repository.AcknowledgeAsync(actor, id, command.Version, receipt, cancellationToken);
    }
    private async Task<FileMutationOperation> Owned(TerminalActor actor, Guid id, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty) throw new ArgumentException("FILES_OPERATION_ID_REQUIRED");
        var row = await repository.OwnedAsync(actor, id, cancellationToken) ?? throw new KeyNotFoundException("FILES_OPERATION_NOT_FOUND");
        if (!HasValidBinding(row, protector)) throw new InvalidOperationException("FILES_INTENT_BINDING_CHANGED");
        return row;
    }
    public static bool HasValidBinding(FileMutationOperation row, IFileMutationProtector protector)
    {
        try
        {
            var plan = ReadPlan(row); Validate(plan);
            return row.OwnerHash == FileManagementService.Hash("file-owner-v1:" + row.ActorSubject + ":" + row.LoginBinding) &&
                FileManagementService.Hex(row.RequestHash) && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(row.RequestHash),
                    Encoding.ASCII.GetBytes(protector.Fingerprint(new(row.Id, row.ActorSubject, row.LoginBinding, plan, row.ExpiresAtUtc))));
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException) { return false; }
    }
    public static void Validate(FileMutationCommand command)
    {
        if (command is null || command.Kind is not ("mkdir" or "copy" or "move" or "chmod") || command.Mode is null) throw new ArgumentException("FILES_OPERATION_INVALID");
        if (command.Kind == "mkdir")
        { if (command.Source is not null || command.ExpectedVersion != "ABSENT") throw new ArgumentException("FILES_OPERATION_INVALID"); }
        else { FileManagementService.Location(command.Source!); if (!FileManagementService.Hex(command.ExpectedVersion)) throw new ArgumentException("FILES_VERSION_INVALID"); }
        if (command.Kind == "chmod")
        { if (command.Target is not null) throw new ArgumentException("FILES_OPERATION_INVALID"); }
        else FileManagementService.Location(command.Target!);
        if (command.Kind is "mkdir" or "chmod")
        { if (command.Mode.Length != 4 || command.Mode[0] != '0' || command.Mode.Skip(1).Any(c => c is < '0' or > '7')) throw new ArgumentException("FILES_MODE_INVALID"); }
        else if (command.Mode.Length != 0) throw new ArgumentException("FILES_MODE_INVALID");
        if (command.Source is not null && command.Source == command.Target) throw new ArgumentException("FILES_SAME_TARGET");
    }
    public static void Validate(FileMutationPlan plan)
    {
        if (plan is null) throw new ArgumentException("FILES_PLAN_INVALID");
        Validate(plan.Command);
        if (!FileManagementService.Hex(plan.Fingerprint) || plan.SizeBytes is < 0 or > FileManagementService.MaximumBytes) throw new ArgumentException("FILES_PLAN_INVALID");
        if (plan.Command.Kind == "mkdir")
        { if (plan.SourceIdentity is not null || plan.SourceParent is not null || plan.SizeBytes != 0 || plan.Sha256 != "") throw new ArgumentException("FILES_PLAN_INVALID"); }
        else
        {
            var source = plan.SourceIdentity;
            if (source is null || source.Inode == 0 || source.LinkCount != 1 || source.Mode > ushort.MaxValue || (source.Mode & 0xF000) != 0x8000 ||
                source.SizeBytes != plan.SizeBytes || !FileManagementService.Hex(plan.Sha256) || plan.SourceParent is not { Inode: > 0 }) throw new ArgumentException("FILES_PLAN_INVALID");
        }
        if (plan.Command.Kind == "chmod") { if (plan.TargetParent is not null) throw new ArgumentException("FILES_PLAN_INVALID"); }
        else if (plan.TargetParent is not { Inode: > 0 }) throw new ArgumentException("FILES_PLAN_INVALID");
    }
    public static void ValidateReceipt(FileMutationPlan plan, FileMutationReceipt receipt)
    {
        if (receipt is null || receipt.Kind != plan.Command.Kind || receipt.Fingerprint != plan.Fingerprint || receipt.SizeBytes != plan.SizeBytes || receipt.Sha256 != plan.Sha256 ||
            receipt.State is not ("Completed" or "Aborted" or "Unknown") || receipt.SourceRetained && receipt.Kind != "move" ||
            receipt.State == "Completed" && (!FileManagementService.Hex(receipt.Version) || receipt.Kind == "move" && !receipt.SourceRetained) ||
            receipt.State != "Completed" && (receipt.Version != "" || receipt.SourceRetained || receipt.BindingCurrent)) throw new FileTransferRejectedException();
    }
    public static string[] PathHashes(FileMutationPlan plan) => new[] { plan.Command.Source, plan.Command.Target }.Where(location => location is not null)
        .Select(location => FileManagementService.Hash(location!.RootId + ":" + location.Path)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    public static FileMutationIntentDto Map(FileMutationOperation row)
    { var plan = ReadPlan(row); return new(row.Id, row.Id, plan.Command, plan.SizeBytes, row.State, row.ExpiresAtUtc, row.ResultCode, row.Version, row.PublishedVersion, row.SourceRetained); }
}

public sealed class FileMutationDispatcher(IFileMutationRepository repository, IFileMutationGateway gateway, IIdentityDirectory identity,
    TimeProvider clock, IFileMutationProtector protector)
{
    public async Task<bool> TickAsync(CancellationToken cancellationToken)
    {
        var row = await repository.ClaimAsync(clock.GetUtcNow(), cancellationToken); if (row is null) return false;
        try
        {
            if (!FileMutationService.HasValidBinding(row, protector))
            { await repository.FinishAsync(row, "Unknown", "FILES_INTENT_BINDING_CHANGED", null, cancellationToken); return true; }
            var receipt = await gateway.InspectMutationAsync(row, cancellationToken);
            if (receipt is not null) { await Finish(receipt, receipt.State == "Aborted" && await repository.CancellationRequestedAsync(row.Id, cancellationToken)); return true; }
            bool admin;
            try { admin = (await identity.ResolveCurrentAsync(row.ActorSubject, null, [], cancellationToken)).IsAdministrator; }
            catch (UnauthorizedAccessException) { admin = false; }
            var canceled = await repository.CancellationRequestedAsync(row.Id, cancellationToken) || !admin;
            // A previously started operation is never blindly re-applied after
            // process/transport loss. Discard writes the durable root fence.
            if (canceled || row.ExpiresAtUtc <= clock.GetUtcNow() || row.RootStartedAtUtc is not null)
            { await Finish(await gateway.DiscardMutationAsync(row, cancellationToken), canceled); return true; }
            var start = await repository.StartedAsync(row, cancellationToken);
            if (start != FileUploadStartResult.Started) { await repository.DeferAsync(row, cancellationToken); return true; }
            // Apply may have committed before a lost response. Any exception
            // defers to Inspect, not a second physical write or new operation ID.
            await Finish(await gateway.ApplyMutationAsync(row, cancellationToken), false);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested) { await repository.DeferAsync(row, cancellationToken); }
        return true;
        async Task Finish(FileMutationReceipt result, bool canceled)
        {
            FileMutationService.ValidateReceipt(FileMutationService.ReadPlan(row), result);
            var state = result.State == "Completed" ? result.BindingCurrent ? "Succeeded" : "Superseded" : result.State == "Unknown" ? "Unknown" : canceled ? "Canceled" : "Failed";
            await repository.FinishAsync(row, state, state switch { "Succeeded" => "FILES_MUTATION_COMPLETED", "Superseded" => "FILES_TARGET_CHANGED",
                "Unknown" => "FILES_RESULT_UNKNOWN", "Canceled" => "FILES_CANCELED", _ => "FILES_OPERATION_ABORTED" }, result, cancellationToken);
        }
    }
}
