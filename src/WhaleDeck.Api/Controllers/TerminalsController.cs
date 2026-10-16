using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhaleDeck.Api.Middleware;
using WhaleDeck.Api.Security;
using WhaleDeck.Api.Terminals;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Api.Controllers;

[ApiController, Authorize(Policy = "Administrator"), Route("api/v1/terminals")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TerminalsController(TerminalSessionService sessions, TerminalCookieIdentity identity, HostTerminalWebSocket bridge) : ControllerBase
{
    /// <summary>Confirmed, idempotent, login-bound 30-second one-time host terminal grant. This is a restricted non-root shell.</summary>
    [HttpPost("host/grants")]
    public async Task<ActionResult<TerminalGrantDto>> Issue(CreateHostTerminalCommand command, CancellationToken cancellationToken)
    {
        if (Request.Headers["Idempotency-Key"].Count != 1 || Request.Headers["Idempotency-Key"].ToString() != command.IdempotencyKey) return BadRequest();
        var login = await identity.ReadAsync(HttpContext);
        return Ok(await sessions.IssueHostAsync(login.Actor, command, cancellationToken));
    }
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TerminalSessionDto>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await sessions.GetAsync((await identity.ReadAsync(HttpContext)).Actor, id, cancellationToken));
    [HttpPost("{id:guid}/close")]
    public async Task<IActionResult> Close(Guid id, CancellationToken cancellationToken)
    {
        // Get requires current administrator permission and exact login owner.
        var actor = (await identity.ReadAsync(HttpContext)).Actor;
        await sessions.GetAsync(actor, id, cancellationToken);
        await sessions.CloseAsync(actor, id, "TERMINAL_CLOSED", null, cancellationToken); return NoContent();
    }
    /// <summary>Cookie-authenticated same-origin WebSocket. Token must be sent in the first JSON message, never in a URL. No reconnect to an already consumed grant.</summary>
    [HttpGet("{id:guid}/connect")]
    public async Task Connect(Guid id)
    {
        if (!SameOriginWriteProtection.IsSameOrigin(Request) || Request.QueryString.HasValue)
        { Response.StatusCode = StatusCodes.Status403Forbidden; return; }
        if (!HttpContext.WebSockets.IsWebSocketRequest || HttpContext.WebSockets.WebSocketRequestedProtocols.Count != 1 ||
            HttpContext.WebSockets.WebSocketRequestedProtocols[0] != "whaledeck.terminal.v1")
        { Response.StatusCode = StatusCodes.Status400BadRequest; return; }
        var login = await identity.ReadAsync(HttpContext);
        await bridge.RunAsync(HttpContext, id, login);
    }
}
