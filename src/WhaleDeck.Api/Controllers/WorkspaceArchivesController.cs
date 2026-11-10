using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhaleDeck.Api.Security;
using WhaleDeck.Application.Development;

namespace WhaleDeck.Api.Controllers;

[ApiController, Authorize, Route("api/v1/development/workspaces/{id:guid}/archives")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None), RequestSizeLimit(4096)]
public sealed class WorkspaceArchivesController(WorkspaceArchives archives, TerminalCookieIdentity identity) : ControllerBase
{
    [HttpGet]
    public async Task<WorkspaceArchiveView?> Current(Guid id, CancellationToken token) =>
        await archives.CurrentAsync((await identity.ReadAsync(HttpContext)).Actor, id, token);
    [HttpGet("{archiveId:guid}")]
    public async Task<WorkspaceArchiveView> Get(Guid id, Guid archiveId, CancellationToken token) =>
        await archives.GetAsync((await identity.ReadAsync(HttpContext)).Actor, id, archiveId, token);
    [HttpPost, Consumes("application/json")]
    public async Task<ActionResult<WorkspaceArchiveView>> Begin(Guid id, WorkspaceArchiveBegin input, CancellationToken token)
    {
        var header = Request.Headers["Idempotency-Key"];
        if (header.Count != 1 || !Guid.TryParseExact(header[0], "D", out var key) || key == Guid.Empty || key.ToString("D") != header[0])
            return BadRequest();
        var view = await archives.BeginAsync((await identity.ReadAsync(HttpContext)).Actor, id, key, input, token);
        return AcceptedAtAction(nameof(Get), new { id, archiveId = view.ArchiveId }, view);
    }
    [HttpPost("{archiveId:guid}/confirm"), Consumes("application/json")]
    public async Task<ActionResult<WorkspaceArchiveView>> Confirm(Guid id, Guid archiveId, WorkspaceArchiveConfirmation input, CancellationToken token)
    {
        var view = await archives.ConfirmAsync((await identity.ReadAsync(HttpContext)).Actor, id, archiveId, input, token);
        return AcceptedAtAction(nameof(Get), new { id, archiveId }, view);
    }
    [HttpPost("{archiveId:guid}/cancel")]
    public async Task<ActionResult<WorkspaceArchiveView>> Cancel(Guid id, Guid archiveId, CancellationToken token)
    {
        var view = await archives.CancelAsync((await identity.ReadAsync(HttpContext)).Actor, id, archiveId, token);
        return AcceptedAtAction(nameof(Get), new { id, archiveId }, view);
    }
    [HttpPost("{archiveId:guid}/authorize")]
    public async Task<ActionResult<WorkspaceArchiveView>> Reauthorize(Guid id, Guid archiveId, CancellationToken token)
    {
        var view = await archives.ReauthorizeAsync((await identity.ReadAsync(HttpContext)).Actor, id, archiveId, token);
        return AcceptedAtAction(nameof(Get), new { id, archiveId }, view);
    }
}
