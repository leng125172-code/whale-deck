using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhaleDeck.Api.Security;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Api.Controllers;

[ApiController, Authorize(Policy = "Administrator"), Route("api/v1/files/tree-recyclings")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FileTreeRecyclingsController(FileTreeRecyclingService trees, TerminalCookieIdentity identity) : ControllerBase
{
    /// <summary>Administrator-only, stable cursor catalogue: whole registered root or one exact source. Historical verification is not a live disk-integrity claim.</summary>
    [HttpGet("captures")]
    public async Task<ActionResult<FileTreeCapturePage>> Captures([FromQuery] string rootId, [FromQuery] Guid? after, CancellationToken cancellationToken, [FromQuery] string path = "", [FromQuery] int take = 25) =>
        Ok(await trees.CapturesAsync((await identity.ReadAsync(HttpContext)).Actor, new(rootId, path), after, take, cancellationToken));
    /// <summary>Queue a login-bound full-tree planning job; does not recycle. The supplied UUID key recovers the same permanent intent after a lost HTTP response.</summary>
    [HttpPost, ProducesResponseType<FileTreeRecyclingIntentDto>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<FileTreeRecyclingIntentDto>> Queue(FileTreeRecyclingCommand command, CancellationToken cancellationToken)
    {
        var key = Request.Headers["Idempotency-Key"].ToString(); if (!Key(key)) return BadRequest();
        var row = await trees.QueueAsync((await identity.ReadAsync(HttpContext)).Actor, command, key, cancellationToken);
        return Accepted($"/api/v1/files/tree-recyclings/{row.Id:D}", row);
    }
    /// <summary>Recover original planning/execution and five-minute confirmation from this login. Planning completion is not recycling completion.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<FileTreeRecyclingIntentDto>> Status(Guid id, CancellationToken cancellationToken) =>
        Ok(await trees.GetAsync((await identity.ReadAsync(HttpContext)).Actor, id, cancellationToken));
    /// <summary>Read-only lookup of a lost registration response, bound to the current original login. Not found never creates or retries a planning job.</summary>
    [HttpGet("by-key/{key}")]
    public async Task<ActionResult<FileTreeRecyclingIntentDto>> ByKey(string key, CancellationToken cancellationToken) =>
        Ok(await trees.GetByKeyAsync((await identity.ReadAsync(HttpContext)).Actor, key, cancellationToken));
    /// <summary>Confirm the sealed original plan and acquire hierarchical locks, returning a separate execution job. Same confirmation never creates another execution.</summary>
    [HttpPost("{id:guid}/confirm"), ProducesResponseType<FileTreeRecyclingIntentDto>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<FileTreeRecyclingIntentDto>> Confirm(Guid id, ConfirmFileTreeRecyclingCommand command, CancellationToken cancellationToken)
    {
        if (!Key(id.ToString("D"))) return BadRequest();
        var row = await trees.ConfirmAsync((await identity.ReadAsync(HttpContext)).Actor, id, command, cancellationToken);
        return Accepted($"/api/v1/files/tree-recyclings/{row.Id:D}", row);
    }
    /// <summary>Fence an unconfirmed plan immediately, or request execution cancellation until its permanent root receipt is reconciled.</summary>
    [HttpPost("{id:guid}/cancel"), ProducesResponseType<FileTreeRecyclingIntentDto>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<FileTreeRecyclingIntentDto>> Cancel(Guid id, CancellationToken cancellationToken)
    {
        if (!Key(id.ToString("D"))) return BadRequest();
        return Accepted(await trees.CancelAsync((await identity.ReadAsync(HttpContext)).Actor, id, cancellationToken));
    }
    /// <summary>Original-login, version-bound explicit acceptance of an unknown historical result after root fencing; no success or private-object deletion is inferred.</summary>
    [HttpPost("{id:guid}/acknowledge"), ProducesResponseType<FileCancelDto>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Acknowledge(Guid id, AcknowledgeFileMutationCommand command, CancellationToken cancellationToken)
    {
        if (!Key(id.ToString("D"))) return BadRequest();
        await trees.AcknowledgeAsync((await identity.ReadAsync(HttpContext)).Actor, id, command, cancellationToken); return Accepted(new FileCancelDto(id, "Acknowledged"));
    }
    private bool Key(string value) => Request.Headers["Idempotency-Key"].Count == 1 && Guid.TryParseExact(value, "D", out var key) && key != Guid.Empty && key.ToString("D") == value && Request.Headers["Idempotency-Key"].ToString() == value;
}
