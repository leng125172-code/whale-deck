using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

public sealed class ContainerDirectoryWorkflowService(ContainerFileUploadService authorization, IContainerDirectoryGateway gateway,
    IContainerDirectoryRepository repository, IContainerDirectoryProtector protector, TimeProvider clock)
{
    public Task AuthorizeAsync(TerminalActor actor, CancellationToken cancellationToken) => authorization.AuthorizeAsync(actor, cancellationToken);
    public async Task<ContainerDirectoryPlanResponse> PlanAsync(TerminalActor actor, ContainerDirectoryCommandDto command, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken); ContainerDirectoryValidation.Command(command);
        await gateway.RequireContainerDirectoryWorkflowAsync(cancellationToken);
        var plan = await gateway.PlanContainerDirectoryAsync(actor, command, cancellationToken); ContainerDirectoryValidation.Plan(plan);
        if (plan.Command != command) throw ContainerDirectoryValidation.Invalid();
        await AuthorizeAsync(actor, cancellationToken);
        var created = ContainerDirectoryWorkflowValidation.Time(clock.GetUtcNow()); var expiry = created.AddMinutes(5);
        return new(protector.SignDirectory(new(actor.Subject, actor.LoginBinding, plan, created, expiry)), expiry, command,
            "仅在当前容器实例创建不存在的目录，不覆盖目标。目录所有者来自容器实际进程。结果未知时保留保护，不自动重复执行。");
    }
    public async Task<ContainerDirectoryIntentDto> SubmitAsync(TerminalActor actor, string containerId, SubmitContainerDirectoryCommand command, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken); ContainerFileValidation.Id(containerId);
        var key = ContainerDirectoryWorkflowValidation.Key(command); var claims = Claims(actor, containerId, command.PlanHash);
        var id = Id(actor, key); var tokenHash = FileManagementService.Hash(command.PlanHash);
        var prior = await repository.OwnedAsync(actor, id, cancellationToken);
        if (prior is not null) { Match(prior, claims, tokenHash); return Map(prior); }
        var now = clock.GetUtcNow();
        if (claims.ExpiresAtUtc <= now || claims.CreatedAtUtc > now) throw new InvalidOperationException("CONTAINER_DIRECTORY_PLAN_EXPIRED");
        await gateway.RequireContainerDirectoryWorkflowAsync(cancellationToken);
        var current = await gateway.PlanContainerDirectoryAsync(actor, claims.Plan.Command, cancellationToken);
        if (current != claims.Plan) throw new InvalidOperationException("CONTAINER_DIRECTORY_VERSION_CONFLICT");
        await AuthorizeAsync(actor, cancellationToken);
        var created = ContainerDirectoryWorkflowValidation.Time(clock.GetUtcNow());
        if (claims.ExpiresAtUtc <= created) throw new InvalidOperationException("CONTAINER_DIRECTORY_PLAN_EXPIRED");
        var location = current.Command.Location;
        var binding = new ContainerDirectoryBindingDto(id, key, actor.Subject, actor.LoginBinding, actor.TraceId,
            ContainerDirectoryWorkflowValidation.Owner(actor.Subject, actor.LoginBinding), current, tokenHash, created, created.AddMinutes(15));
        var row = new ContainerDirectoryOperation { Id = id, IdempotencyKey = key, ActorSubject = actor.Subject, LoginBinding = actor.LoginBinding,
            TraceId = actor.TraceId, OwnerHash = binding.Owner, ContainerId = location.ContainerId, InstanceId = location.InstanceId, RelativePath = location.Path,
            PlanHash = tokenHash, PlanJson = JsonSerializer.Serialize(current, ContainerDirectoryWorkflowValidation.Json), RequestHash = protector.FingerprintDirectory(binding),
            CreatedAtUtc = created, ExpiresAtUtc = binding.ExpiresAtUtc, NextAttemptAtUtc = created.AddSeconds(6) };
        return Map(await repository.EnqueueAsync(row, claims.ExpiresAtUtc, cancellationToken));
    }
    public async Task<ContainerDirectoryIntentDto?> ResolveAsync(TerminalActor actor, string containerId, SubmitContainerDirectoryCommand command, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken); ContainerFileValidation.Id(containerId);
        var key = ContainerDirectoryWorkflowValidation.Key(command); var claims = Claims(actor, containerId, command.PlanHash);
        var row = await repository.ResolveAsync(actor, Id(actor, key), claims.ExpiresAtUtc, cancellationToken);
        if (row is null) return null;
        Match(row, claims, FileManagementService.Hash(command.PlanHash)); return Map(row);
    }
    public async Task<ContainerDirectoryIntentDto> GetAsync(TerminalActor actor, string containerId, Guid id, CancellationToken cancellationToken)
    { await AuthorizeAsync(actor, cancellationToken); return Map(await Owned(actor, containerId, id, cancellationToken)); }
    public async Task CancelAsync(TerminalActor actor, string containerId, Guid id, CancellationToken cancellationToken)
    { await AuthorizeAsync(actor, cancellationToken); _ = await Owned(actor, containerId, id, cancellationToken); await repository.CancelAsync(actor, id, cancellationToken); }
    public async Task<IReadOnlyList<ContainerDirectoryIntentDto>> UnknownAsync(TerminalActor actor, int take, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        if (take is < 1 or > 50) throw new ArgumentException("CONTAINER_DIRECTORY_PAGE_INVALID");
        var rows = await repository.UnknownAsync(take, cancellationToken);
        foreach (var row in rows) RequireBinding(row);
        return rows.Select(Map).ToArray();
    }
    public async Task<ContainerDirectoryIntentDto> RecoveryGetAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken); if (id == Guid.Empty) throw new ArgumentException("CONTAINER_DIRECTORY_ID_REQUIRED");
        var row = await repository.FindAsync(id, cancellationToken) ?? throw new KeyNotFoundException("CONTAINER_DIRECTORY_NOT_FOUND");
        RequireBinding(row); return Map(row);
    }
    public async Task AcknowledgeAsync(TerminalActor actor, Guid id, AcknowledgeContainerDirectoryCommand command, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        if (id == Guid.Empty || command is null || !command.Confirmed || command.Version < 0) throw new ArgumentException("CONTAINER_DIRECTORY_CONFIRMATION_REQUIRED");
        var row = await repository.FindAsync(id, cancellationToken) ?? throw new KeyNotFoundException("CONTAINER_DIRECTORY_NOT_FOUND");
        RequireBinding(row);
        if (row.State != "Unknown" || row.CompletedAtUtc is not null || row.Version != command.Version) throw new InvalidOperationException("CONTAINER_DIRECTORY_ACKNOWLEDGEMENT_CONFLICT");
        // Root's three terminal states permanently fence a delayed Apply for
        // this exact intent. A busy/lost reply cannot release SQL protection.
        var reference = ContainerDirectoryWorkflowValidation.Reference(row);
        var receipt = await gateway.DiscardContainerDirectoryAsync(reference, cancellationToken);
        ContainerDirectoryValidation.Receipt(reference, receipt);
        await AuthorizeAsync(actor, cancellationToken);
        await repository.AcknowledgeAsync(actor, id, command.Version, receipt, cancellationToken);
    }
    private ContainerDirectoryPlanClaims Claims(TerminalActor actor, string containerId, string token)
    {
        var claims = protector.VerifyDirectory(token); ContainerDirectoryWorkflowValidation.Claims(claims);
        if (claims.Actor != actor.Subject || claims.LoginBinding != actor.LoginBinding || claims.Plan.Command.Location.ContainerId != containerId)
            throw new UnauthorizedAccessException("CONTAINER_DIRECTORY_PLAN_OWNER_REQUIRED");
        return claims;
    }
    private static Guid Id(TerminalActor actor, Guid key) => new(SHA256.HashData(Encoding.UTF8.GetBytes("whaledeck-container-directory-id-v1:" +
        BffSessionValidation.SubjectHash(actor.Subject) + ":" + actor.LoginBinding + ":" + key.ToString("N"))).AsSpan(0, 16));
    private void Match(ContainerDirectoryOperation row, ContainerDirectoryPlanClaims claims, string hash)
    {
        RequireBinding(row);
        if (row.PlanHash != hash || ContainerDirectoryWorkflowValidation.ReadPlan(row) != claims.Plan) throw new InvalidOperationException("CONTAINER_DIRECTORY_IDEMPOTENCY_CONFLICT");
    }
    private async Task<ContainerDirectoryOperation> Owned(TerminalActor actor, string containerId, Guid id, CancellationToken cancellationToken)
    {
        ContainerFileValidation.Id(containerId); if (id == Guid.Empty) throw new ArgumentException("CONTAINER_DIRECTORY_ID_REQUIRED");
        var row = await repository.OwnedAsync(actor, id, cancellationToken) ?? throw new KeyNotFoundException("CONTAINER_DIRECTORY_NOT_FOUND");
        if (row.ContainerId != containerId) throw new KeyNotFoundException("CONTAINER_DIRECTORY_NOT_FOUND");
        RequireBinding(row); return row;
    }
    private void RequireBinding(ContainerDirectoryOperation row)
    { if (!HasValidBinding(row, protector)) throw new InvalidOperationException("CONTAINER_DIRECTORY_BINDING_CHANGED"); }
    public static bool HasValidBinding(ContainerDirectoryOperation row, IContainerDirectoryProtector protector)
    {
        try { return FileManagementService.Hex(row.RequestHash) && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(row.RequestHash),
            Encoding.ASCII.GetBytes(protector.FingerprintDirectory(ContainerDirectoryWorkflowValidation.ReadBinding(row)))); }
        catch (Exception error) when (error is JsonException or ArgumentException) { return false; }
    }
    public static ContainerDirectoryIntentDto Map(ContainerDirectoryOperation row) => new(row.Id, row.Id, ContainerDirectoryWorkflowValidation.ReadPlan(row).Command,
        row.State, row.ExpiresAtUtc, row.ResultCode, row.Version, row.PublishedVersion);
}
