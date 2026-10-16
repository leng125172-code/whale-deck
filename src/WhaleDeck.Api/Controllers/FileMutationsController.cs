using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhaleDeck.Api.Security;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Api.Controllers;

[ApiController, Authorize(Policy = "Administrator"), Route("api/v1/files/operations")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FileMutationsController(FileMutationService mutations, TerminalCookieIdentity identity) : ControllerBase
{
    /// <summary>Five-minute login-bound exact-version plan. mkdir creates one leaf; copy/move/chmod accept only regular single-link files. No overwrite, chown or recursive directory operations.</summary>
    [HttpPost("plan")]
    public async Task<ActionResult<FileMutationPlanDto>> Plan(FileMutationCommand command, CancellationToken cancellationToken) =>
        Ok(await mutations.PlanAsync((await identity.ReadAsync(HttpContext)).Actor, command, cancellationToken));
    /// <summary>Confirmed permanent intent, shared source/target locks, Job, Outbox and human audit. Execution is asynchronous; never return success before the root receipt.</summary>
    [HttpPost, ProducesResponseType<FileMutationIntentDto>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<FileMutationIntentDto>> Create(SubmitFileMutationCommand command, CancellationToken cancellationToken)
    {
        if (!KeyMatches(command.IdempotencyKey)) return BadRequest();
        var result = await mutations.SubmitAsync((await identity.ReadAsync(HttpContext)).Actor, command, cancellationToken);
        return Accepted($"/api/v1/files/operations/{result.Id:D}", result);
    }
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<FileMutationIntentDto>> Status(Guid id, CancellationToken cancellationToken) =>
        Ok(await mutations.GetAsync((await identity.ReadAsync(HttpContext)).Actor, id, cancellationToken));
    /// <summary>Resolve the original frozen registration without creating a task. Null is returned only after plan expiry and a transaction fence proves no delayed registration can still appear.</summary>
    [HttpPost("resolve")]
    public async Task<ActionResult<FileMutationIntentDto?>> Resolve(SubmitFileMutationCommand command, CancellationToken cancellationToken)
    {
        if (!KeyMatches(command.IdempotencyKey)) return BadRequest();
        return Ok(await mutations.ResolveAsync((await identity.ReadAsync(HttpContext)).Actor, command, cancellationToken));
    }
    /// <summary>Request cancellation. A durable root tombstone fences delayed execution. Already completed effects are never undone.</summary>
    [HttpPost("{id:guid}/cancel"), ProducesResponseType<FileCancelDto>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
    {
        if (!KeyMatches(id.ToString("D"))) return BadRequest();
        await mutations.CancelAsync((await identity.ReadAsync(HttpContext)).Actor, id, cancellationToken); return Accepted(new FileCancelDto(id, "CancelRequested"));
    }
    /// <summary>Explicitly accept an uncertain outcome after manual investigation. Root is fenced first; this releases SQL locks, never reports success/rollback or deletes evidence. Version-bound and original login only.</summary>
    [HttpPost("{id:guid}/acknowledge"), ProducesResponseType<FileCancelDto>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Acknowledge(Guid id, AcknowledgeFileMutationCommand command, CancellationToken cancellationToken)
    {
        if (!KeyMatches(id.ToString("D"))) return BadRequest();
        await mutations.AcknowledgeAsync((await identity.ReadAsync(HttpContext)).Actor, id, command, cancellationToken); return Accepted(new FileCancelDto(id, "Acknowledged"));
    }
    private bool KeyMatches(string key) => Request.Headers["Idempotency-Key"].Count == 1 && Request.Headers["Idempotency-Key"].ToString() == key;
}
