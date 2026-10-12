using System.Text.Json;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

public sealed class OperationService(IJobRepository jobs)
{
    private static readonly Dictionary<string, HashSet<string>> AllowedActions =
        new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["containers"] = new(StringComparer.OrdinalIgnoreCase)
                { "create", "start", "stop", "restart", "delete", "pull", "prune", "update", "rebuild", "batch-start", "batch-stop", "batch-restart", "batch-delete" },
            ["docker"] = new(StringComparer.OrdinalIgnoreCase) { "validate-settings", "apply-settings", "network-create", "network-delete", "volume-delete" },
            ["databases"] = new(StringComparer.OrdinalIgnoreCase) { "create", "delete", "create-principal", "delete-principal", "disable-principal", "grant", "rotate", "terminate-connection" },
            // Active backups are canceled through the job cancellation API so
            // cancellation targets a concrete persisted Agent operation.
            ["backups"] = new(StringComparer.OrdinalIgnoreCase) { "run", "verify", "save-policy" },
            ["applications"] = new(StringComparer.OrdinalIgnoreCase) { "install", "start", "stop", "restart", "update", "reinstall", "uninstall" },
            ["host"] = new(StringComparer.OrdinalIgnoreCase) { "update-check", "update-install", "reboot", "systemd-start", "systemd-stop", "systemd-restart" },
            ["config-repository"] = new(StringComparer.OrdinalIgnoreCase) { "snapshot", "commit-push" },
            ["platform"] = new(StringComparer.OrdinalIgnoreCase) { "diagnose", "plan-update", "apply-update", "rollback", "diagnostic-bundle" },
            ["identity"] = new(StringComparer.OrdinalIgnoreCase) { "create-user", "update-user", "disable-user", "create-sso", "update-sso" }
        };

    public async Task<JobDto> EnqueueAsync(string actorSubject, string traceId, OperationCommand command, CancellationToken cancellationToken)
    {
        command = command with { Area = command.Area.ToLowerInvariant(), Action = command.Action.ToLowerInvariant() };
        if (!AllowedActions.TryGetValue(command.Area, out var actions) || !actions.Contains(command.Action))
        {
            throw new ArgumentException("The requested management action is not registered.");
        }
        if (RequiresConfirmation(command) && !command.Confirmed)
        {
            throw new InvalidOperationException("This action requires explicit confirmation.");
        }
        if (RequiresPlan(command) && string.IsNullOrWhiteSpace(command.PlanHash))
        {
            throw new InvalidOperationException("This action requires a current plan hash.");
        }
        if (string.IsNullOrWhiteSpace(command.IdempotencyKey) || command.IdempotencyKey.Length > 128)
        {
            throw new ArgumentException("A valid idempotency key is required.");
        }
        RejectSensitiveParameters(command.RequestJson);

        var job = new OperationJob
        {
            JobType = $"{command.Area}.{command.Action}",
            ActorSubject = actorSubject,
            ResourceId = Guid.TryParse(command.ResourceId, out var resourceId) ? resourceId : null,
            IdempotencyKey = command.IdempotencyKey,
            RequestJson = command.RequestJson
        };
        var outbox = new OutboxMessage { MessageType = "OperationRequested", PayloadJson = $"{{\"jobId\":\"{job.Id:D}\"}}" };
        var audit = new AuditEvent
        {
            ActorSubject = actorSubject,
            Action = job.JobType,
            TargetType = command.Area,
            TargetId = command.ResourceId ?? "new",
            Result = "Accepted",
            JobId = job.Id,
            TraceId = traceId,
            DetailJson = command.RetriedFromJobId is { } sourceJobId
                ? JsonSerializer.Serialize(new { retriedFromJobId = sourceJobId }) : "{}"
        };
        return Map(await jobs.EnqueueAsync(job, outbox, audit, cancellationToken));
    }

    public async Task<JobDto?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        var job = await jobs.FindAsync(id, cancellationToken);
        return job is null ? null : Map(job);
    }

    public async Task<IReadOnlyCollection<JobDto>> ListAsync(int take, CancellationToken cancellationToken) =>
        (await jobs.ListAsync(Math.Clamp(take, 1, 200), cancellationToken)).Select(Map).ToArray();

    public async Task<IReadOnlyCollection<JobEventDto>> EventsAsync(Guid id, long afterSequence, CancellationToken cancellationToken) =>
        (await jobs.ListEventsAsync(id, afterSequence, cancellationToken)).Select(item => new JobEventDto(item.Sequence, item.State, item.Phase, item.ProgressPercent, item.MessageCode, item.OccurredAtUtc)).ToArray();

    public Task CancelAsync(Guid id, string actorSubject, CancellationToken cancellationToken) =>
        jobs.RequestCancellationAsync(id, actorSubject, cancellationToken);

    private static bool RequiresConfirmation(OperationCommand command) => command.Action is
        "stop" or "restart" or "delete" or "delete-principal" or "prune" or "apply-settings" or "uninstall" or
        "update" or "rebuild" or "reinstall" or "reboot" or "update-install" or "commit-push" or "apply-update" or "rollback" or
        "network-delete" or "volume-delete" or
        "batch-start" or "batch-stop" or "batch-restart" or "batch-delete";

    internal static bool RequiresPlan(OperationCommand command) => command.Action is
        "create" or "delete" or "delete-principal" or "prune" or "apply-settings" or "install" or "update" or "rebuild" or "reinstall" or "uninstall" or
        "update-install" or "reboot" or "commit-push" or "apply-update" or "rollback" or
        "network-create" or "network-delete" or "volume-delete" or
        "batch-start" or "batch-stop" or "batch-restart" or "batch-delete";

    private static JobDto Map(OperationJob job) => new(job.Id, job.JobType, job.State, job.Phase, job.ProgressPercent, job.ErrorCode,
        job.CreatedAtUtc, job.CompletedAtUtc, job.State == "Succeeded" ? job.ResultJson : null);

    private static void RejectSensitiveParameters(string requestJson)
    {
        using var document = JsonDocument.Parse(requestJson);
        if (!document.RootElement.TryGetProperty("parameters", out var parameters) || parameters.ValueKind != JsonValueKind.Object) return;
        foreach (var property in parameters.EnumerateObject())
        {
            if (property.Name.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("token", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("credential", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Sensitive values must use the one-time secret workflow and cannot be queued in job parameters.");
            }
        }
    }
}
