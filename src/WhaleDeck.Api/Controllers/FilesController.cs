using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhaleDeck.Api.Files;
using WhaleDeck.Api.Security;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Api.Controllers;

[ApiController, Authorize(Policy = "Administrator"), Route("api/v1/files")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FilesController(FileManagementService files, FileTextService text, TerminalCookieIdentity identity, FileHttpTransport transport) : ControllerBase
{
    /// <summary>Registered roots only. Never returns arbitrary host paths or protected configuration roots.</summary>
    [HttpGet("roots")]
    public async Task<ActionResult<IReadOnlyCollection<FileRootDto>>> Roots(CancellationToken cancellationToken) =>
        Ok(await files.RootsAsync((await identity.ReadAsync(HttpContext)).Actor, cancellationToken));
    [HttpGet("entries")]
    public async Task<ActionResult<FilePageDto>> List([FromQuery] string rootId, [FromQuery] string path = "", [FromQuery] string after = "", [FromQuery] int take = 100, CancellationToken cancellationToken = default) =>
        Ok(await files.ListAsync((await identity.ReadAsync(HttpContext)).Actor, new(rootId, path), after, take, cancellationToken));
    [HttpGet("entry")]
    public async Task<ActionResult<FileEntryDto>> Inspect([FromQuery] string rootId, [FromQuery] string path, CancellationToken cancellationToken) =>
        Ok(await files.InspectAsync((await identity.ReadAsync(HttpContext)).Actor, new(rootId, path), cancellationToken));
    /// <summary>Version-bound UTF-8 text up to 1 MiB. Full SHA256 and durable metadata-only audit precede content delivery. Binary/NUL and incomplete streams are rejected; BOM and line endings are reported, never silently converted.</summary>
    [HttpGet("text")]
    public async Task<ActionResult<FileTextDto>> Text([FromQuery] string rootId, [FromQuery] string path, [FromQuery] string expectedVersion, CancellationToken cancellationToken) =>
        Ok(await text.ReadAsync((await identity.ReadAsync(HttpContext)).Actor, new(rootId, path), expectedVersion, cancellationToken));
    /// <summary>Prepare a five-minute plan bound to this login, exact relative path, size, SHA256 and expected target version (ABSENT for creates).</summary>
    [HttpPost("uploads/plan")]
    public async Task<ActionResult<FileUploadPlanDto>> Plan(FileUploadPlanCommand command, CancellationToken cancellationToken) =>
        Ok(await files.PlanAsync((await identity.ReadAsync(HttpContext)).Actor, command, cancellationToken));
    /// <summary>Confirmed permanent intent, Job, Outbox and audit. Returns 202 before any bytes are sent; does not publish a file.</summary>
    [HttpPost("uploads")]
    [ProducesResponseType<FileUploadIntentDto>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<FileUploadIntentDto>> Create(SubmitFileUploadCommand command, CancellationToken cancellationToken)
    {
        if (!KeyMatches(command.IdempotencyKey)) return BadRequest();
        var result = await files.SubmitAsync((await identity.ReadAsync(HttpContext)).Actor, command, cancellationToken);
        return Accepted($"/api/v1/files/uploads/{result.Id:D}", result);
    }
    [HttpGet("uploads/{id:guid}")]
    public async Task<ActionResult<FileUploadIntentDto>> Status(Guid id, CancellationToken cancellationToken) =>
        Ok(await files.GetAsync((await identity.ReadAsync(HttpContext)).Actor, id, cancellationToken));
    /// <summary>Single receive attempt. Raw binary, exact Content-Length, CSRF/Origin and Idempotency-Key equal to upload ID. 202 means staged, not yet published; poll the Job/intent.</summary>
    [HttpPut("uploads/{id:guid}/content"), Consumes("application/octet-stream"), RequestSizeLimit(FileManagementService.MaximumBytes)]
    [ProducesResponseType<FileUploadIntentDto>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<FileUploadIntentDto>> Content(Guid id, CancellationToken cancellationToken)
    {
        if (!KeyMatches(id.ToString("D")) || Request.Headers.ContainsKey("Content-Encoding")) return BadRequest();
        var login = await identity.ReadAsync(HttpContext);
        var result = await files.ReceiveAsync(login.Actor, id, Request.Body, Request.ContentLength,
            async _ => { await identity.ReadAsync(HttpContext, login.Key); }, cancellationToken);
        return Accepted($"/api/v1/files/uploads/{result.Id:D}", result);
    }
    /// <summary>Request cancellation, including a root tombstone before first receive. Completed publication is never undone.</summary>
    [HttpPost("uploads/{id:guid}/cancel")]
    [ProducesResponseType<FileCancelDto>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
    {
        if (!KeyMatches(id.ToString("D"))) return BadRequest();
        await files.CancelAsync((await identity.ReadAsync(HttpContext)).Actor, id, cancellationToken); return Accepted(new FileCancelDto(id, "CancelRequested"));
    }
    /// <summary>Version-bound binary attachment, max 1 GiB / 10 min, no ranges. EOF succeeds only after the complete Agent SHA256 and durable download audit.</summary>
    [HttpGet("download")]
    [Produces("application/octet-stream")]
    public async Task Download([FromQuery] string rootId, [FromQuery] string path, [FromQuery] string expectedVersion) =>
        await transport.DownloadAsync(HttpContext, await identity.ReadAsync(HttpContext), new(rootId, path), expectedVersion);
    private bool KeyMatches(string key) => Request.Headers["Idempotency-Key"].Count == 1 && Request.Headers["Idempotency-Key"].ToString() == key;
}
