using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Development;

public sealed record WorkspaceBudget(int CpuMillis, int MemoryMiB, int DiskMiB);
public sealed record WorkspaceRuntimePin(string Version, string Image);
public sealed record WorkspaceRuntimeSpec(string Key, string Version, string Image);
public sealed record WorkspaceEnvironment(int Version, WorkspaceRuntimeSpec[] Runtimes);
public sealed record WorkspaceRegistrationCommand(string Slug, string StorageRootId, string[] Runtimes, WorkspaceBudget Budget);
public sealed record WorkspaceDefinition(Guid Id, string Slug, string OwnerSubject, string StorageRootId, string State,
    long Version, WorkspaceBudget Budget, WorkspaceEnvironment Environment, string EnvironmentSha256, DateTimeOffset CreatedAtUtc);
public sealed record WorkspaceRegistrationReceipt(WorkspaceDefinition Workspace, bool Replayed);
public sealed record WorkspaceCatalog(WorkspaceRuntimeSpec[] Runtimes, string[] StorageRootIds, WorkspaceBudget Capacity);
public sealed record WorkspacePage(WorkspaceDefinition[] Items, string? NextAfter);

public interface IWorkspaceStorageGateway
{
    Task AllocateAsync(TerminalActor actor, WorkspaceDefinition workspace, CancellationToken token);
    Task AttestAsync(TerminalActor actor, WorkspaceDefinition workspace, CancellationToken token);
}

public sealed class WorkspaceRegistrationPolicy
{
    public const int MaximumWorkspaceDiskMiB = 1024 * 1024;
    public string[] StorageRootIds { get; init; } = [];
    public Guid[] CreateGroupIds { get; init; } = [];
    public Dictionary<string, WorkspaceRuntimePin> Runtimes { get; init; } = new(StringComparer.Ordinal);
    public WorkspaceBudget Capacity { get; init; } = new(8000, 16384, 102400);
}

public interface IDevelopmentWorkspaceRegistry
{
    Task<WorkspaceRegistrationReceipt?> FindReplayAsync(string subject, Guid key, string requestHash, CancellationToken token);
    Task<WorkspaceRegistrationReceipt> RegisterAsync(TerminalActor actor, Guid key, string requestHash, WorkspaceDefinition workspace,
        WorkspaceBudget capacity, CancellationToken token);
    Task<WorkspaceDefinition?> FindAsync(Guid id, string? owner, CancellationToken token);
    Task<WorkspacePage> ListAsync(string? owner, string? after, int take, CancellationToken token);
    Task<WorkspaceDefinition> BeginStorageAllocationAsync(TerminalActor actor, WorkspaceStoragePlan plan,
        DateTimeOffset now, CancellationToken token);
    Task<WorkspaceDefinition> CompleteStorageAllocationAsync(TerminalActor actor, WorkspaceStoragePlan plan,
        DateTimeOffset now, CancellationToken token);
}

public sealed partial class DevelopmentWorkspaces(
    WorkspaceReportAuthorization authorization, IDevelopmentWorkspaceRegistry registry, WorkspaceRegistrationPolicy policy, TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<WorkspaceCatalog> CatalogAsync(TerminalActor actor, CancellationToken token)
    {
        await RequireCreatorAsync(actor, token);
        return new([.. policy.Runtimes.OrderBy(row => row.Key, StringComparer.Ordinal).Select(row => Spec(row.Key, row.Value))],
            policy.StorageRootIds.ToArray(), policy.Capacity);
    }

    public async Task<WorkspaceRegistrationReceipt> RegisterAsync(
        TerminalActor actor, Guid key, WorkspaceRegistrationCommand command, CancellationToken token)
    {
        await RequireCreatorAsync(actor, token);
        if (key == Guid.Empty || !ValidKey(command.Slug) || !ValidKey(command.StorageRootId) ||
            command.Runtimes is not { Length: > 0 and <= 4 } || !command.Runtimes.All(ValidKey) ||
            command.Runtimes.Distinct(StringComparer.Ordinal).Count() != command.Runtimes.Length ||
            command.Budget is not { CpuMillis: >= 100, MemoryMiB: >= 128, DiskMiB: >= 256 and <= WorkspaceRegistrationPolicy.MaximumWorkspaceDiskMiB })
            throw new ArgumentException("WORKSPACE_REGISTRATION_INVALID");

        var canonical = command with { Runtimes = [.. command.Runtimes.Order(StringComparer.Ordinal)] };
        var requestHash = Digest(canonical);
        var previous = await registry.FindReplayAsync(actor.Subject, key, requestHash, token);
        if (previous is not null) return previous;
        if (!policy.StorageRootIds.Contains(command.StorageRootId, StringComparer.Ordinal) ||
            canonical.Runtimes.Any(runtime => !policy.Runtimes.ContainsKey(runtime)) ||
            command.Budget.CpuMillis > policy.Capacity.CpuMillis || command.Budget.MemoryMiB > policy.Capacity.MemoryMiB ||
            command.Budget.DiskMiB > policy.Capacity.DiskMiB)
            throw new ArgumentException("WORKSPACE_REGISTRATION_NOT_APPROVED");

        var environment = new WorkspaceEnvironment(1, [.. canonical.Runtimes.Select(runtime => Spec(runtime, policy.Runtimes[runtime]))]);
        var now = clock.GetUtcNow();
        var createdAt = new DateTimeOffset(now.Ticks - now.Ticks % TimeSpan.TicksPerMicrosecond, TimeSpan.Zero);
        var workspace = new WorkspaceDefinition(Guid.NewGuid(), command.Slug, actor.Subject, command.StorageRootId,
            WorkspaceStorageAllocation.Registered, 1, command.Budget, environment, Digest(environment), createdAt);
        return await registry.RegisterAsync(actor, key, requestHash, workspace, policy.Capacity, token);
    }

    public async Task<WorkspaceDefinition> GetAsync(TerminalActor actor, Guid id, CancellationToken token)
    {
        var current = await authorization.ResolveAsync(actor, token);
        return await registry.FindAsync(id, current.IsAdministrator ? null : actor.Subject, token)
            ?? throw new KeyNotFoundException("WORKSPACE_NOT_FOUND");
    }

    public async Task<WorkspacePage> ListAsync(TerminalActor actor, string? after, int take, CancellationToken token)
    {
        if (take is < 1 or > 100 || (after is not null && !ValidKey(after))) throw new ArgumentException("WORKSPACE_PAGE_INVALID");
        var current = await authorization.ResolveAsync(actor, token);
        return await registry.ListAsync(current.IsAdministrator ? null : actor.Subject, after, take, token);
    }

    public static void ValidatePolicy(WorkspaceRegistrationPolicy policy)
    {
        if (!policy.StorageRootIds.All(ValidKey) || policy.CreateGroupIds.Contains(Guid.Empty) ||
            policy.Capacity is not { CpuMillis: >= 100, MemoryMiB: >= 128, DiskMiB: >= 256 } ||
            policy.Runtimes.Any(row => !ValidKey(row.Key) || row.Value is null || !VersionPattern().IsMatch(row.Value.Version ?? "") ||
                !ImagePattern().IsMatch(row.Value.Image ?? "")))
            throw new InvalidOperationException("WORKSPACE_REGISTRATION_POLICY_INVALID");
    }

    public static string EncodeEnvironment(WorkspaceEnvironment value) => JsonSerializer.Serialize(value, Json);
    public static WorkspaceEnvironment DecodeEnvironment(string value, string digest)
    {
        if (Hash(System.Text.Encoding.UTF8.GetBytes(value)) != digest)
            throw new InvalidDataException("WORKSPACE_ENVIRONMENT_INTEGRITY");
        return JsonSerializer.Deserialize<WorkspaceEnvironment>(value, Json)
            ?? throw new InvalidDataException("WORKSPACE_ENVIRONMENT_INTEGRITY");
    }

    internal async Task RequireCreatorAsync(TerminalActor actor, CancellationToken token)
    {
        var current = await authorization.ResolveAsync(actor, token);
        if (!current.IsAdministrator && !current.Groups.Any(group => Guid.TryParseExact(group, "D", out var id) &&
            policy.CreateGroupIds.Contains(id)))
            throw new UnauthorizedAccessException("WORKSPACE_CREATE_REQUIRED");
    }

    private static WorkspaceRuntimeSpec Spec(string key, WorkspaceRuntimePin pin) => new(key, pin.Version, pin.Image);
    private static bool ValidKey(string? value) => value is { Length: > 0 and <= 48 } && KeyPattern().IsMatch(value);
    private static string Digest<T>(T value) => Hash(JsonSerializer.SerializeToUtf8Bytes(value, Json));
    private static string Hash(byte[] value) => Convert.ToHexStringLower(SHA256.HashData(value));

    [GeneratedRegex("\\A[a-z0-9]+(?:-[a-z0-9]+)*\\z", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();
    [GeneratedRegex("\\A[0-9A-Za-z][0-9A-Za-z.+-]{0,47}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();
    [GeneratedRegex("\\A[a-z0-9][a-z0-9._/-]{2,199}@sha256:[a-f0-9]{64}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex ImagePattern();
}
