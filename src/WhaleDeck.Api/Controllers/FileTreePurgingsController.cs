using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhaleDeck.Api.Security;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Api.Controllers;

[ApiController, Authorize(Policy = "Administrator"), Route("api/v1/files/tree-purgings")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FileTreePurgingsController(FileTreePurgingService trees, TerminalCookieIdentity identity) : ControllerBase
{
    /// <summary>Queue a permanent cleanup plan from a signed historical capture ID and explicit Archive/Originals/All scope; does not delete. Original owner/source/proof are resolved server-side. The UUID key recovers the same intent after a lost response.</summary>
    [HttpPost, ProducesResponseType<FileTreePurgingIntentDto>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<FileTreePurgingIntentDto>> Queue(CreateFileTreePurgingCommand command, CancellationToken cancellationToken)
    {
        var key = Request.Headers["Idempotency-Key"].ToString(); if (!Key(key)) return BadRequest();
        var row = await trees.QueueAsync((await identity.ReadAsync(HttpContext)).Actor, command, key, cancellationToken);
        return Accepted($"/api/v1/files/tree-purgings/{row.Id:D}", row);
    }
    /// <summary>Recover original planning/execution and five-minute confirmation from this login. Planning completion is not deletion completion.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<FileTreePurgingIntentDto>> Status(Guid id, CancellationToken cancellationToken) =>
        Ok(await trees.GetAsync((await identity.ReadAsync(HttpContext)).Actor, id, cancellationToken));
    /// <summary>Read-only lookup of a lost registration response, bound to the current original login. Not found never creates or retries a planning job.</summary>
    [HttpGet("by-key/{key}")]
    public async Task<ActionResult<FileTreePurgingIntentDto>> ByKey(string key, CancellationToken cancellationToken) =>
        Ok(await trees.GetByKeyAsync((await identity.ReadAsync(HttpContext)).Actor, key, cancellationToken));
    /// <summary>Confirm the sealed original plan and acquire hierarchical locks, returning a separate execution job. Same confirmation never creates another execution.</summary>
    [HttpPost("{id:guid}/confirm"), ProducesResponseType<FileTreePurgingIntentDto>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<FileTreePurgingIntentDto>> Confirm(Guid id, ConfirmFileTreePurgingCommand command, CancellationToken cancellationToken)
    {
        if (!Key(id.ToString("D"))) return BadRequest();
        var row = await trees.ConfirmAsync((await identity.ReadAsync(HttpContext)).Actor, id, command, cancellationToken);
        return Accepted($"/api/v1/files/tree-purgings/{row.Id:D}", row);
    }
    /// <summary>Fence an unconfirmed plan immediately, or request execution cancellation until its permanent root receipt is reconciled.</summary>
    [HttpPost("{id:guid}/cancel"), ProducesResponseType<FileTreePurgingIntentDto>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<FileTreePurgingIntentDto>> Cancel(Guid id, CancellationToken cancellationToken)
    {
        if (!Key(id.ToString("D"))) return BadRequest();
        return Accepted(await trees.CancelAsync((await identity.ReadAsync(HttpContext)).Actor, id, cancellationToken));
    }
    /// <summary>Original-login, version-bound acknowledgment after root fencing; preserves signed component deletion facts and uncertainty. Never resumes deletion.</summary>
    [HttpPost("{id:guid}/acknowledge"), ProducesResponseType<FileCancelDto>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Acknowledge(Guid id, AcknowledgeFileMutationCommand command, CancellationToken cancellationToken)
    {
        if (!Key(id.ToString("D"))) return BadRequest();
        await trees.AcknowledgeAsync((await identity.ReadAsync(HttpContext)).Actor, id, command, cancellationToken); return Accepted(new FileCancelDto(id, "Acknowledged"));
    }
    private bool Key(string value) => Request.Headers["Idempotency-Key"].Count == 1 && Guid.TryParseExact(value, "D", out var key) && key != Guid.Empty && key.ToString("D") == value && Request.Headers["Idempotency-Key"].ToString() == value;
}
