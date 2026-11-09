using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhaleDeck.Api.Security;
using WhaleDeck.Application.Development;

namespace WhaleDeck.Api.Controllers;

[ApiController, Authorize, Route("api/v1/development/workspaces")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None), RequestSizeLimit(4096)]
public sealed class DevelopmentWorkspacesController(DevelopmentWorkspaces workspaces, TerminalCookieIdentity identity) : ControllerBase
{
    [HttpGet("catalog")]
    public async Task<WorkspaceCatalog> Catalog(CancellationToken token) =>
        await workspaces.CatalogAsync((await identity.ReadAsync(HttpContext)).Actor, token);

    [HttpGet]
    public async Task<WorkspacePage> List([FromQuery] string? after = null, [FromQuery] int take = 30, CancellationToken token = default) =>
        await workspaces.ListAsync((await identity.ReadAsync(HttpContext)).Actor, after, take, token);

    [HttpGet("{id:guid}")]
    public async Task<WorkspaceDefinition> Get(Guid id, CancellationToken token) =>
        await workspaces.GetAsync((await identity.ReadAsync(HttpContext)).Actor, id, token);

    [HttpPost, Consumes("application/json")]
    public async Task<ActionResult<WorkspaceRegistrationReceipt>> Register(WorkspaceRegistrationCommand command, CancellationToken token)
    {
        var header = Request.Headers["Idempotency-Key"];
        if (header.Count != 1 || !Guid.TryParseExact(header[0], "D", out var key) ||
            key == Guid.Empty || key.ToString("D") != header[0])
            return BadRequest();
        var receipt = await workspaces.RegisterAsync((await identity.ReadAsync(HttpContext)).Actor, key, command, token);
        if (receipt.Replayed) return Ok(receipt);
        return CreatedAtAction(nameof(Get), new { id = receipt.Workspace.Id }, receipt);
    }
}
