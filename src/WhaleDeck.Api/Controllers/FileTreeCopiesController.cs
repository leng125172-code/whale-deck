using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhaleDeck.Api.Security;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Api.Controllers;

[ApiController, Authorize(Policy = "Administrator"), Route("api/v1/files/tree-copies")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FileTreeCopiesController(FileTreeCopyService trees, TerminalCookieIdentity identity) : ControllerBase
{
    /// <summary>Queue a login-bound full-tree planning job; does not copy. The supplied UUID key recovers the same permanent intent after a lost HTTP response.</summary>
    [HttpPost, ProducesResponseType<FileTreeCopyIntentDto>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<FileTreeCopyIntentDto>> Queue(FileTreeCopyCommand command, CancellationToken cancellationToken)
    {
        var key = Request.Headers["Idempotency-Key"].ToString(); if (!Key(key)) return BadRequest();
        var row = await trees.QueueAsync((await identity.ReadAsync(HttpContext)).Actor, command, key, cancellationToken);
        return Accepted($"/api/v1/files/tree-copies/{row.Id:D}", row);
    }
    /// <summary>Recover original planning/execution and five-minute confirmation from this login. Planning completion is not copy completion.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<FileTreeCopyIntentDto>> Status(Guid id, CancellationToken cancellationToken) =>
        Ok(await trees.GetAsync((await identity.ReadAsync(HttpContext)).Actor, id, cancellationToken));
    /// <summary>Confirm the sealed original plan and acquire hierarchical locks, returning a separate execution job. Same confirmation never creates another execution.</summary>
    [HttpPost("{id:guid}/confirm"), ProducesResponseType<FileTreeCopyIntentDto>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<FileTreeCopyIntentDto>> Confirm(Guid id, ConfirmFileTreeCopyCommand command, CancellationToken cancellationToken)
    {
        if (!Key(id.ToString("D"))) return BadRequest();
        var row = await trees.ConfirmAsync((await identity.ReadAsync(HttpContext)).Actor, id, command, cancellationToken);
        return Accepted($"/api/v1/files/tree-copies/{row.Id:D}", row);
    }
    /// <summary>Fence an unconfirmed plan immediately, or request execution cancellation until its permanent root receipt is reconciled.</summary>
    [HttpPost("{id:guid}/cancel"), ProducesResponseType<FileTreeCopyIntentDto>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<FileTreeCopyIntentDto>> Cancel(Guid id, CancellationToken cancellationToken)
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
