using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhaleDeck.Api.Security;
using WhaleDeck.Application.Development;

namespace WhaleDeck.Api.Controllers;

[ApiController, Authorize, Route("api/v1/development/workspaces/{workspaceId:guid}/source-imports/{importId:guid}")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class WorkspaceSourcesController(WorkspaceSources sources, TerminalCookieIdentity identity) : ControllerBase
{
    [HttpPut, Consumes("application/json"), RequestSizeLimit(4096)]
    public async Task<ActionResult<WorkspaceSourceView>> Begin(Guid workspaceId, Guid importId, WorkspaceSourceInput input, CancellationToken token)
    {
        var result = await sources.BeginAsync((await identity.ReadAsync(HttpContext)).Actor, workspaceId, importId, input, token);
        return AcceptedAtAction(nameof(Get), new { workspaceId, importId }, result);
    }

    [HttpGet]
    public async Task<WorkspaceSourceView> Get(Guid workspaceId, Guid importId, CancellationToken token) =>
        await sources.GetAsync((await identity.ReadAsync(HttpContext)).Actor, workspaceId, importId, token);

    [HttpPut("chunks/{offset:int}"), Consumes("application/octet-stream"), RequestSizeLimit(WorkspaceSources.ChunkBytes)]
    public async Task<ActionResult<WorkspaceSourceView>> Chunk(Guid workspaceId, Guid importId, int offset, CancellationToken token)
    {
        if (Request.ContentLength is not (> 0 and <= WorkspaceSources.ChunkBytes) || Request.Headers.ContainsKey("Content-Encoding"))
            return BadRequest();
        return await sources.AppendAsync((await identity.ReadAsync(HttpContext)).Actor, workspaceId, importId, offset,
            (int)Request.ContentLength.Value, Request.Body, token);
    }

    [HttpPost("publish"), Consumes("application/json"), RequestSizeLimit(4096)]
    public async Task<ActionResult<WorkspaceSourceView>> Publish(Guid workspaceId, Guid importId, WorkspaceSourceConfirmation input, CancellationToken token)
    {
        var result = await sources.ConfirmAsync((await identity.ReadAsync(HttpContext)).Actor, workspaceId, importId, input.PlanSha256, token);
        return AcceptedAtAction(nameof(Get), new { workspaceId, importId }, result);
    }

    [HttpPost("cancel"), RequestSizeLimit(1024)]
    public async Task<ActionResult<WorkspaceSourceView>> Cancel(Guid workspaceId, Guid importId, CancellationToken token)
    {
        var result = await sources.CancelAsync((await identity.ReadAsync(HttpContext)).Actor, workspaceId, importId, token);
        return AcceptedAtAction(nameof(Get), new { workspaceId, importId }, result);
    }
}
