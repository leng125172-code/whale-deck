using System.Text.Json;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Services;

/// <summary>Retries a terminal operation as a new, independently authorized job.</summary>
public sealed class JobRetryService(IJobRepository jobs, IAgentGateway agent, OperationService operations)
{
    public async Task<JobRetryContextDto> GetContextAsync(Guid id, CancellationToken cancellationToken) =>
        (await ReadAsync(id, cancellationToken)).Context;

    public async Task<JobRetryPlanDto> PlanAsync(Guid id, string? inputTicket, CancellationToken cancellationToken)
    {
        var retry = await ReadAsync(id, cancellationToken);
        var parameters = PrepareParameters(retry.Context, retry.Parameters, inputTicket);
        var command = new OperationCommand(retry.Area, retry.Action, retry.Context.ResourceId, "preview", "{}", null, true);
        var plan = OperationService.RequiresPlan(command)
            ? await agent.PlanAsync(retry.Area, retry.Action, retry.Context.ResourceId, parameters, cancellationToken)
            : null;
        return new JobRetryPlanDto(retry.Context, plan);
    }

    public async Task<JobDto> RetryAsync(Guid id, string actorSubject, string traceId, string idempotencyKey,
        string? planHash, string? inputTicket, bool confirmed, CancellationToken cancellationToken)
    {
        if (!confirmed) throw new InvalidOperationException("Retrying an operation requires explicit confirmation.");
        var retry = await ReadAsync(id, cancellationToken);
        if (idempotencyKey == retry.OriginalKey)
            throw new ArgumentException("A retry must use a new idempotency key.");
        var parameters = PrepareParameters(retry.Context, retry.Parameters, inputTicket);
        // The failed task and its events stay intact. A new job/outbox/audit record
        // receives a fresh Agent operation ID and passes the ordinary safety checks.
        var request = JsonSerializer.Serialize(new { resourceId = retry.Context.ResourceId, parameters, planHash, retriedFromJobId = id });
        return await operations.EnqueueAsync(actorSubject, traceId,
            new OperationCommand(retry.Area, retry.Action, retry.Context.ResourceId, idempotencyKey, request, planHash, confirmed, id),
            cancellationToken);
    }

    private async Task<(JobRetryContextDto Context, string Area, string Action, Dictionary<string, string> Parameters, string OriginalKey)> ReadAsync(
        Guid id, CancellationToken cancellationToken)
    {
        var source = await jobs.FindAsync(id, cancellationToken) ?? throw new KeyNotFoundException("Job was not found.");
        if (source.State is not ("Failed" or "Canceled" or "RolledBack"))
            throw new InvalidOperationException("Only failed, canceled or rolled-back jobs can be retried.");
        var kind = source.JobType.Split('.', 2);
        if (kind.Length != 2) throw new InvalidOperationException("The task has no registered operation to retry.");
        using var request = JsonDocument.Parse(source.RequestJson);
        var resourceId = request.RootElement.TryGetProperty("resourceId", out var resource) ? resource.GetString() : null;
        var parameters = request.RootElement.TryGetProperty("parameters", out var values) && values.ValueKind == JsonValueKind.Object
            ? values.EnumerateObject().ToDictionary(item => item.Name, item => item.Value.GetString() ?? string.Empty, StringComparer.Ordinal)
            : new Dictionary<string, string>();
        var requiresInput = parameters.Remove("inputTicket");
        var inputKind = requiresInput ? parameters.GetValueOrDefault("inputKind", "password") : null;
        return (new JobRetryContextDto(id, source.JobType, resourceId, requiresInput, inputKind), kind[0], kind[1], parameters, source.IdempotencyKey);
    }

    private static Dictionary<string, string> PrepareParameters(JobRetryContextDto context, Dictionary<string, string> parameters,
        string? inputTicket)
    {
        if (context.RequiresInput)
        {
            if (string.IsNullOrWhiteSpace(inputTicket) || inputTicket.Length > 256)
                throw new InvalidOperationException("Restage the required input before retrying this task.");
            parameters["inputTicket"] = inputTicket;
        }
        else if (!string.IsNullOrEmpty(inputTicket))
            throw new ArgumentException("This task does not accept replacement input.");
        return parameters;
    }
}
