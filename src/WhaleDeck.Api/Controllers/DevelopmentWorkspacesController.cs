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

    [HttpGet("{id:guid}/storage-plan")]
    public async Task<WorkspaceStoragePlan> PlanStorage(Guid id, [FromServices] WorkspaceStorageAllocation storage, CancellationToken token) =>
        await storage.PlanAsync((await identity.ReadAsync(HttpContext)).Actor, id, token);

    [HttpPost("{id:guid}/storage-allocation"), Consumes("application/json")]
    public async Task<WorkspaceDefinition> AllocateStorage(Guid id, WorkspaceStorageApplyCommand command,
        [FromServices] WorkspaceStorageAllocation storage, CancellationToken token) =>
        await storage.ApplyAsync((await identity.ReadAsync(HttpContext)).Actor, id, command, token);

    [HttpPost("{id:guid}/runner-preflight")]
    public async Task<WorkspaceRunnerPreflight> PreflightRunner(Guid id, [FromServices] WorkspaceStorageAllocation storage, CancellationToken token) =>
        await storage.PreflightAsync((await identity.ReadAsync(HttpContext)).Actor, id, token);

    [HttpPost("{id:guid}/runs"), Consumes("application/json")]
    public async Task<ActionResult<WorkspaceRunView>> QueueRun(Guid id, WorkspaceRunInput input,
        [FromServices] WorkspaceRuns runs, CancellationToken token)
    {
        var header = Request.Headers["Idempotency-Key"];
        if (header.Count != 1 || !Guid.TryParseExact(header[0], "D", out var key) ||
            key == Guid.Empty || key.ToString("D") != header[0])
            return BadRequest();
        var run = await runs.EnqueueAsync((await identity.ReadAsync(HttpContext)).Actor, id, key, input, token);
        return AcceptedAtAction(nameof(GetRun), new { id, runId = run.RunId }, run);
    }

    [HttpGet("{id:guid}/runs/{runId:guid}")]
    public async Task<WorkspaceRunView> GetRun(Guid id, Guid runId, [FromServices] WorkspaceRuns runs, CancellationToken token) =>
        await runs.GetAsync((await identity.ReadAsync(HttpContext)).Actor, id, runId, token);

    [HttpPost("{id:guid}/runs/{runId:guid}/cancel")]
    public async Task<ActionResult<WorkspaceRunView>> CancelRun(Guid id, Guid runId, [FromServices] WorkspaceRuns runs, CancellationToken token)
    {
        var run = await runs.CancelAsync((await identity.ReadAsync(HttpContext)).Actor, id, runId, token);
        return AcceptedAtAction(nameof(GetRun), new { id, runId }, run);
    }

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
