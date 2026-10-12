using System.Text.Json;
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

    [HttpGet("{id:guid}/events")]
    public async Task Events(Guid id, CancellationToken cancellationToken)
    {
        Response.Headers.ContentType = "text/event-stream";
        var after = long.TryParse(Request.Headers["Last-Event-ID"], out var sequence) ? sequence : 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            var events = await operations.EventsAsync(id, after, cancellationToken);
            foreach (var item in events)
            {
                await Response.WriteAsync($"id: {item.Sequence}\nevent: progress\ndata: {JsonSerializer.Serialize(item)}\n\n", cancellationToken);
                after = item.Sequence;
            }
            await Response.Body.FlushAsync(cancellationToken);
            var job = await operations.FindAsync(id, cancellationToken);
            if (job is null || job.State is "Succeeded" or "Failed" or "Canceled" or "RolledBack") return;
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }
    }

    /// <summary>Describes a retry and whether a new one-time secret is required.</summary>
    [HttpGet("{id:guid}/retry-context")]
    public async Task<IActionResult> RetryContext(Guid id, CancellationToken cancellationToken) =>
        Ok(await retries.GetContextAsync(id, cancellationToken));

    /// <summary>Preflights the failed operation against current resources; old plans are not reused.</summary>
    [HttpPost("{id:guid}/retry/plan")]
    public async Task<IActionResult> RetryPlan(Guid id, [FromBody] RetryPlanRequest request, CancellationToken cancellationToken) =>
        Ok(await retries.PlanAsync(id, request.InputTicket, cancellationToken));

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
