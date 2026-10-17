using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhaleDeck.Api.Security;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Api.Controllers;

[ApiController, Authorize(Policy = "Administrator"), Route("api/v1/files/archives")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FileArchivesController(FileArchiveService archives, TerminalCookieIdentity identity) : ControllerBase
{
    /// <summary>Bounded HDD history snapshot. Present is metadata/stat availability, not a fresh full-content checksum. Legacy capture actors remain unknown.</summary>
    [HttpGet]
    public async Task<ActionResult<FileHistoryPage>> History([FromQuery] string rootId, [FromQuery] string path = "", [FromQuery] string after = "", [FromQuery] int take = 50,
        [FromQuery] string expectedVersion = "", CancellationToken cancellationToken = default) =>
        Ok(await archives.HistoryAsync((await identity.ReadAsync(HttpContext)).Actor, new(rootId, path, after, take, expectedVersion), cancellationToken));
    /// <summary>Five-minute exact-version plan. Trash/restore/purge support regular files; full HDD backup is mandatory before trash and restore-overwrite. Purge addresses one HDD capture, not local journal retention.</summary>
    [HttpPost("plan")]
    public async Task<ActionResult<FileArchivePlanDto>> Plan(FileArchiveCommand command, CancellationToken cancellationToken)
    {
        var key = Request.Headers["Idempotency-Key"].ToString();
        if (!KeyMatches(key) || !Guid.TryParseExact(key, "D", out var parsed) || parsed == Guid.Empty || parsed.ToString("D") != key) return BadRequest();
        return Ok(await archives.PlanAsync((await identity.ReadAsync(HttpContext)).Actor, command, cancellationToken, key));
    }
    /// <summary>Recover the exact session-bound plan or registered job after reload. Issued is not confirmation; Expired is sampled together with job absence under the enqueue gate.</summary>
    [HttpGet("registrations/{id:guid}")]
    public async Task<ActionResult<FileArchiveRegistrationDto>> Registration(Guid id, CancellationToken cancellationToken) =>
        Ok(await archives.RegistrationAsync((await identity.ReadAsync(HttpContext)).Actor, id, cancellationToken));
    /// <summary>Fence an unconfirmed plan against late submission. An already registered task is returned, never silently canceled or undone.</summary>
    [HttpPost("registrations/{id:guid}/cancel")]
    public async Task<ActionResult<FileArchiveRegistrationDto>> CancelRegistration(Guid id, CancellationToken cancellationToken)
    {
        if (!KeyMatches(id.ToString("D"))) return BadRequest();
        return Ok(await archives.CancelRegistrationAsync((await identity.ReadAsync(HttpContext)).Actor, id, cancellationToken));
    }
    /// <summary>Confirm a permanent login-bound intent with shared locks, Job, Outbox and audit. Never synchronously claim success.</summary>
    [HttpPost, ProducesResponseType<FileArchiveIntentDto>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<FileArchiveIntentDto>> Create(SubmitFileArchiveCommand command, CancellationToken cancellationToken)
    {
        if (!KeyMatches(command.IdempotencyKey)) return BadRequest();
        var result = await archives.SubmitAsync((await identity.ReadAsync(HttpContext)).Actor, command, cancellationToken);
        return Accepted($"/api/v1/files/archives/{result.Id:D}", result);
    }
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<FileArchiveIntentDto>> Status(Guid id, CancellationToken cancellationToken) =>
        Ok(await archives.GetAsync((await identity.ReadAsync(HttpContext)).Actor, id, cancellationToken));
    /// <summary>Resolve the original registration without creating a new intent. Null requires expiry plus a transaction fence against late registration.</summary>
    [HttpPost("resolve")]
    public async Task<ActionResult<FileArchiveIntentDto?>> Resolve(SubmitFileArchiveCommand command, CancellationToken cancellationToken)
    {
        if (!KeyMatches(command.IdempotencyKey)) return BadRequest();
        return Ok(await archives.ResolveAsync((await identity.ReadAsync(HttpContext)).Actor, command, cancellationToken));
    }
    [HttpPost("{id:guid}/cancel"), ProducesResponseType<FileCancelDto>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
    {
        if (!KeyMatches(id.ToString("D"))) return BadRequest();
        await archives.CancelAsync((await identity.ReadAsync(HttpContext)).Actor, id, cancellationToken); return Accepted(new FileCancelDto(id, "CancelRequested"));
    }
    /// <summary>Original-login, version-bound human acceptance of an uncertain result. Root is fenced first; no success, rollback or evidence deletion is inferred.</summary>
    [HttpPost("{id:guid}/acknowledge"), ProducesResponseType<FileCancelDto>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Acknowledge(Guid id, AcknowledgeFileArchiveCommand command, CancellationToken cancellationToken)
    {
        if (!KeyMatches(id.ToString("D"))) return BadRequest();
        await archives.AcknowledgeAsync((await identity.ReadAsync(HttpContext)).Actor, id, command, cancellationToken); return Accepted(new FileCancelDto(id, "Acknowledged"));
    }
    private bool KeyMatches(string key) => Request.Headers["Idempotency-Key"].Count == 1 && Request.Headers["Idempotency-Key"].ToString() == key;
}
