using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhaleDeck.Api.Security;
using WhaleDeck.Application.Development;

namespace WhaleDeck.Api.Controllers;

[ApiController, Authorize, Route("api/v1/development/workspaces/{id:guid}/lifecycle")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None), RequestSizeLimit(4096)]
public sealed class WorkspaceLifecycleController(WorkspaceLifecycle lifecycle, TerminalCookieIdentity identity) : ControllerBase
{
    [HttpGet("plan")]
    public async Task<WorkspaceLifecyclePlan> Plan(Guid id, [FromQuery] string action, CancellationToken token) =>
        await lifecycle.PlanAsync((await identity.ReadAsync(HttpContext)).Actor, id, action, token);

    [HttpGet]
    public async Task<WorkspaceLifecycleView?> Current(Guid id, CancellationToken token) =>
        await lifecycle.CurrentAsync((await identity.ReadAsync(HttpContext)).Actor, id, token);

    [HttpGet("{operationId:guid}")]
    public async Task<WorkspaceLifecycleView> Get(Guid id, Guid operationId, CancellationToken token) =>
        await lifecycle.GetAsync((await identity.ReadAsync(HttpContext)).Actor, id, operationId, token);

    [HttpPost, Consumes("application/json")]
    public async Task<ActionResult<WorkspaceLifecycleView>> Apply(Guid id, WorkspaceLifecycleCommand command, CancellationToken token)
    {
        var header = Request.Headers["Idempotency-Key"];
        if (header.Count != 1 || !Guid.TryParseExact(header[0], "D", out var key) || key == Guid.Empty || key.ToString("D") != header[0])
            return BadRequest();
        var result = await lifecycle.ApplyAsync((await identity.ReadAsync(HttpContext)).Actor, id, key, command, token);
        return AcceptedAtAction(nameof(Get), new { id, operationId = result.OperationId }, result);
    }

    [HttpPost("{operationId:guid}/authorize")]
    public async Task<ActionResult<WorkspaceLifecycleView>> Reauthorize(Guid id, Guid operationId, CancellationToken token)
    {
        var result = await lifecycle.ReauthorizeAsync((await identity.ReadAsync(HttpContext)).Actor, id, operationId, token);
        return AcceptedAtAction(nameof(Get), new { id, operationId }, result);
    }
}
