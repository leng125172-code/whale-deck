using System.Text.Json;
using System.Diagnostics;
using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhaleDeck.Api.Security;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Api.Controllers;

[ApiController]
[Authorize(Policy = "Administrator")]
[Route("api/v1/jobs")]
public sealed class JobsController(OperationService operations, JobRetryService retries) : ControllerBase
{
    private static readonly JsonSerializerOptions EventJson = new(JsonSerializerDefaults.Web);
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int take = 50, CancellationToken cancellationToken = default) =>
        Ok(await operations.ListAsync(take, cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var job = await operations.FindAsync(id, cancellationToken);
        return job is null ? NotFound() : Ok(job);
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
    {
        await operations.CancelAsync(id, User.RequireSubject(), cancellationToken);
        return Accepted();
    }

    /// <summary>Streams ordered progress, including all persisted terminal history. Reconnect with Last-Event-ID.</summary>
    /// <remarks>Connections rotate after one minute so authentication and authorization run again on reconnect.</remarks>
    [HttpGet("{id:guid}/events")]
    public async Task<IActionResult> Events(Guid id, CancellationToken cancellationToken)
    {
        var cursor = Request.Headers["Last-Event-ID"];
        if (cursor.Count > 1 || cursor.Count == 1 && (cursor[0]?.Length > 19 ||
            !long.TryParse(cursor[0], NumberStyles.None, CultureInfo.InvariantCulture, out _) || cursor[0]?.StartsWith('-') == true))
            throw new ArgumentException("Last-Event-ID must be a non-negative integer.");
        var after = cursor.Count == 0 ? 0 : long.Parse(cursor[0]!, CultureInfo.InvariantCulture);
        var batch = await operations.EventBatchAsync(id, after, cancellationToken);
        if (batch is null) return NotFound();
        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-store";
        Response.Headers["X-Accel-Buffering"] = "no";
        await Response.WriteAsync("retry: 2000\n\n", cancellationToken);
        var started = Stopwatch.GetTimestamp();
        while (batch is not null && !cancellationToken.IsCancellationRequested)
        {
            foreach (var item in batch.Events)
            {
                await Response.WriteAsync($"id: {item.Sequence}\nevent: progress\ndata: {JsonSerializer.Serialize(item, EventJson)}\n\n", cancellationToken);
                after = item.Sequence;
            }
            if (batch.Events.Count == 0) await Response.WriteAsync(": waiting\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
            if (batch.IsTerminal && batch.Events.Count == 0 || Stopwatch.GetElapsedTime(started) >= TimeSpan.FromMinutes(1)) break;
            if (!batch.IsTerminal) await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            batch = await operations.EventBatchAsync(id, after, cancellationToken);
        }
        return new EmptyResult();
    }

    /// <summary>Describes a retry and whether a new one-time secret is required.</summary>
    [HttpGet("{id:guid}/retry-context")]
    public async Task<IActionResult> RetryContext(Guid id, CancellationToken cancellationToken) =>
        Ok(await retries.GetContextAsync(id, cancellationToken));

    /// <summary>Preflights the failed operation against current resources; old plans are not reused.</summary>
    [HttpPost("{id:guid}/retry/plan")]
    public async Task<IActionResult> RetryPlan(Guid id, [FromBody] RetryPlanRequest request, CancellationToken cancellationToken) =>
        Ok(await retries.PlanAsync(id, request.InputTicket, cancellationToken, User.RequireSubject()));

    /// <summary>Creates a new task linked to the failed task, with explicit confirmation and an idempotency key.</summary>
    [HttpPost("{id:guid}/retry")]
    public async Task<IActionResult> Retry(Guid id, [FromBody] RetryRequest request, CancellationToken cancellationToken)
    {
        var key = Request.Headers["Idempotency-Key"].FirstOrDefault() ?? request.IdempotencyKey;
        var job = await retries.RetryAsync(id, User.RequireSubject(), HttpContext.TraceIdentifier, key,
            request.PlanHash, request.InputTicket, request.Confirmed, cancellationToken);
        return AcceptedAtAction(nameof(Get), new { id = job.Id }, job);
    }

    public sealed record RetryPlanRequest(string? InputTicket);
    public sealed record RetryRequest(string IdempotencyKey, string? PlanHash, string? InputTicket, bool Confirmed);
}
