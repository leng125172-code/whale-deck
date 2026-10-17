using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhaleDeck.Api.Security;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Api.Controllers;

[ApiController, Authorize(Policy = "Administrator"), Route("api/v1/files/recovery")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FileRecoveryController(FileRecoveryInventoryService inventory, FileRecoveryService recovery, TerminalCookieIdentity identity) : ControllerBase
{
    /// <summary>Current-admin inventory across five Unknown workflows; never releases a lock or returns old login/owner credentials.</summary>
    [HttpGet, ProducesResponseType<FileRecoveryPageDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<FileRecoveryPageDto>> List(CancellationToken cancellationToken, [FromQuery] string? after = null, [FromQuery] int take = 25) =>
        Ok(await inventory.ListAsync((await identity.ReadAsync(HttpContext)).Actor, after, take, cancellationToken));
    /// <summary>Read the current metadata even after the original login is gone. This does not authorize disposal, replay or success.</summary>
    [HttpGet("{kind}/{id:guid}"), ProducesResponseType<FileRecoveryCandidateDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<FileRecoveryCandidateDto>> Get(string kind, Guid id, CancellationToken cancellationToken) =>
        Ok(await inventory.GetAsync((await identity.ReadAsync(HttpContext)).Actor, kind, id, cancellationToken));
    /// <summary>Create a separate current-login-bound recovery plan. Does not release original locks or replay any file operation.</summary>
    [HttpPost("operations"), ProducesResponseType<FileRecoveryIntentDto>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<FileRecoveryIntentDto>> Queue(QueueFileRecoveryCommand command, CancellationToken cancellationToken)
    {
        var key = Request.Headers["Idempotency-Key"].ToString(); if (!Key(key)) return BadRequest();
        var row = await recovery.QueueAsync((await identity.ReadAsync(HttpContext)).Actor, command, key, cancellationToken);
        return Accepted($"/api/v1/files/recovery/operations/{row.Id:D}", row);
    }
    /// <summary>Read this current login's own recovery plan or disposition progress, without private original material.</summary>
    [HttpGet("operations/{id:guid}")]
    public async Task<ActionResult<FileRecoveryIntentDto>> Status(Guid id, CancellationToken cancellationToken) =>
        Ok(await recovery.GetAsync((await identity.ReadAsync(HttpContext)).Actor, id, cancellationToken));
    /// <summary>Recover a lost registration response. A read never creates or repeats an operation.</summary>
    [HttpGet("operations/by-key/{key}")]
    public async Task<ActionResult<FileRecoveryIntentDto>> ByKey(string key, CancellationToken cancellationToken) =>
        Ok(await recovery.GetByKeyAsync((await identity.ReadAsync(HttpContext)).Actor, key, cancellationToken));
    /// <summary>Confirm the sealed observation and current administrator identity, then queue only fencing and atomic result reconciliation.</summary>
    [HttpPost("operations/{id:guid}/confirm"), ProducesResponseType<FileRecoveryIntentDto>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<FileRecoveryIntentDto>> Confirm(Guid id, ConfirmFileRecoveryCommand command, CancellationToken cancellationToken)
    {
        if (!Key(id.ToString("D"))) return BadRequest();
        var row = await recovery.ConfirmAsync((await identity.ReadAsync(HttpContext)).Actor, id, command, cancellationToken);
        return Accepted($"/api/v1/files/recovery/operations/{row.Id:D}", row);
    }
    /// <summary>Cancel this recovery before fencing begins. Original evidence and locks remain unchanged.</summary>
    [HttpPost("operations/{id:guid}/cancel"), ProducesResponseType<FileRecoveryIntentDto>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<FileRecoveryIntentDto>> Cancel(Guid id, CancellationToken cancellationToken)
    {
        if (!Key(id.ToString("D"))) return BadRequest();
        return Accepted(await recovery.CancelAsync((await identity.ReadAsync(HttpContext)).Actor, id, cancellationToken));
    }
    private bool Key(string value) => Request.Headers["Idempotency-Key"].Count == 1 && Guid.TryParseExact(value, "D", out var key) && key != Guid.Empty &&
        key.ToString("D") == value && Request.Headers["Idempotency-Key"].ToString() == value;
}
