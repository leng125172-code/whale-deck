using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhaleDeck.Api.Security;
using WhaleDeck.Application.Development;

namespace WhaleDeck.Api.Controllers;

[ApiController, Authorize, Route("api/v1/development/workspaces/{id:guid}/retirements")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None), RequestSizeLimit(4096)]
public sealed class WorkspaceRetirementsController(WorkspaceRetirements retirements, TerminalCookieIdentity identity) : ControllerBase
{
    [HttpGet]
    public async Task<WorkspaceRetirementView?> Current(Guid id, CancellationToken token) =>
        await retirements.CurrentAsync((await identity.ReadAsync(HttpContext)).Actor, id, token);
    [HttpGet("{retirementId:guid}")]
    public async Task<WorkspaceRetirementView> Get(Guid id, Guid retirementId, CancellationToken token) =>
        await retirements.GetAsync((await identity.ReadAsync(HttpContext)).Actor, id, retirementId, token);
    [HttpGet("{retirementId:guid}/attempts")]
    public async Task<IReadOnlyList<WorkspaceRetirementAttemptView>> Attempts(Guid id, Guid retirementId, CancellationToken token) =>
        await retirements.AttemptsAsync((await identity.ReadAsync(HttpContext)).Actor, id, retirementId, token);
    [HttpPost("{retirementId:guid}/recover"), Consumes("application/json")]
    public async Task<ActionResult<WorkspaceRetirementView>> Recover(Guid id, Guid retirementId, WorkspaceRetirementRecoveryConfirmation input, CancellationToken token)
    {
        var header = Request.Headers["Idempotency-Key"];
        if (header.Count != 1 || !Guid.TryParseExact(header[0], "D", out var key) || key == Guid.Empty || key.ToString("D") != header[0])
            return BadRequest();
        var view = await retirements.RecoverAsync((await identity.ReadAsync(HttpContext)).Actor, id, retirementId, key, input, token);
        return AcceptedAtAction(nameof(Get), new { id, retirementId }, view);
    }
    [HttpPost, Consumes("application/json")]
    public async Task<ActionResult<WorkspaceRetirementView>> Begin(Guid id, WorkspaceRetirementBegin input, CancellationToken token)
    {
        var header = Request.Headers["Idempotency-Key"];
        if (header.Count != 1 || !Guid.TryParseExact(header[0], "D", out var key) || key == Guid.Empty || key.ToString("D") != header[0])
            return BadRequest();
        var view = await retirements.BeginAsync((await identity.ReadAsync(HttpContext)).Actor, id, key, input, token);
        return AcceptedAtAction(nameof(Get), new { id, retirementId = view.RetirementId }, view);
    }
    [HttpPost("{retirementId:guid}/confirm"), Consumes("application/json")]
    public async Task<ActionResult<WorkspaceRetirementView>> Confirm(Guid id, Guid retirementId, WorkspaceRetirementConfirmation input, CancellationToken token)
    {
        var view = await retirements.ConfirmAsync((await identity.ReadAsync(HttpContext)).Actor, id, retirementId, input, token);
        return AcceptedAtAction(nameof(Get), new { id, retirementId }, view);
    }
    [HttpPost("{retirementId:guid}/cancel")]
    public async Task<ActionResult<WorkspaceRetirementView>> Cancel(Guid id, Guid retirementId, CancellationToken token)
    {
        var view = await retirements.CancelAsync((await identity.ReadAsync(HttpContext)).Actor, id, retirementId, token);
        return AcceptedAtAction(nameof(Get), new { id, retirementId }, view);
    }
    [HttpPost("{retirementId:guid}/authorize")]
    public async Task<ActionResult<WorkspaceRetirementView>> Reauthorize(Guid id, Guid retirementId, CancellationToken token)
    {
        var view = await retirements.ReauthorizeAsync((await identity.ReadAsync(HttpContext)).Actor, id, retirementId, token);
        return AcceptedAtAction(nameof(Get), new { id, retirementId }, view);
    }
}
