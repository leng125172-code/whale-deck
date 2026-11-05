using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhaleDeck.Api.Security;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Api.Controllers;

[ApiController, Authorize(Policy = "Administrator"), ContainerRecycleRestoreInput]
[Route("api/v1/containers/{containerId:regex((?-i:\\A[[a-f0-9]]+\\z)):length(64)}/files/recycle-restores")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None), RequestSizeLimit(65536)]
public sealed class ContainerRecycleRestoresController(ContainerRecycleRestoreWorkflowService recycling, TerminalCookieIdentity identity, ContainerRecycleRestoreSourcesService sources) : ControllerBase
{
    private const string Intent = "{id:regex((?-i:\\A[[a-f0-9-]]+\\z)):length(36):guid}";
    /// <summary>Queue independent source-bound restoration; no file operation occurs until the original login explicitly confirms its completed plan.</summary>
    [HttpPost, Consumes("application/json"), ProducesResponseType<ContainerRecycleRestoreIntentDto>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<ContainerRecycleRestoreIntentDto>> Queue(string containerId, ContainerRecycleRestorePlanBody command, CancellationToken cancellationToken)
    {
        if (!Key(out var key)) return ContainerRecycleRestoreHttp.BadKey(HttpContext);
        if (command is null) return ContainerRecycleRestoreHttp.BadCommand(HttpContext);
        var result = await recycling.QueueAsync((await identity.ReadAsync(HttpContext)).Actor,
            containerId, command.SourceIntentId, key, cancellationToken);
        return Accepted(Location(containerId, result.Id), result);
    }
    [HttpGet("sources"), ContainerRecycleRestoreQuery(AllowTake = true, AllowAfter = true)]
    public async Task<ActionResult<ContainerRecycleRestoreSourcePage>> Sources(string containerId, [FromQuery] int take = 25, [FromQuery] Guid? after = null, CancellationToken cancellationToken = default) =>
        Ok(await sources.ListAsync((await identity.ReadAsync(HttpContext)).Actor, containerId, take, after, cancellationToken));
    [HttpGet(Intent), ContainerRecycleRestoreQuery]
    public async Task<ActionResult<ContainerRecycleRestoreIntentDto>> Status(string containerId, Guid id, CancellationToken cancellationToken) =>
        Ok(await recycling.GetAsync((await identity.ReadAsync(HttpContext)).Actor, containerId, id, cancellationToken));
    /// <summary>Read the original registration after a lost HTTP reply. A missing result never authorizes generating another key.</summary>
    [HttpGet("by-key/{key:guid}"), ContainerRecycleRestoreQuery]
    public async Task<ActionResult<ContainerRecycleRestoreIntentDto>> ByKey(string containerId, string key, CancellationToken cancellationToken) =>
        Ok(await recycling.GetByKeyAsync((await identity.ReadAsync(HttpContext)).Actor, containerId, key, cancellationToken));
    /// <summary>Confirm the five-minute immutable plan. Retries resolve the same execution job; no restoration runs within this HTTP request.</summary>
    [HttpPost(Intent + "/confirm"), ProducesResponseType<ContainerRecycleRestoreIntentDto>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<ContainerRecycleRestoreIntentDto>> Confirm(string containerId, Guid id, ConfirmContainerRecycleRestoreBody command, CancellationToken cancellationToken)
    {
        if (!Matches(id)) return ContainerRecycleRestoreHttp.BadKey(HttpContext);
        var result = await recycling.ConfirmAsync((await identity.ReadAsync(HttpContext)).Actor, containerId, id, new(command.PlanHash, command.Confirmed, command.Version), cancellationToken);
        return Accepted(Location(containerId, id), result);
    }
    [HttpPost(Intent + "/cancel"), ProducesResponseType<ContainerRecycleRestoreIntentDto>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<ContainerRecycleRestoreIntentDto>> Cancel(string containerId, Guid id, CancellationToken cancellationToken)
    {
        if (!Matches(id)) return ContainerRecycleRestoreHttp.BadKey(HttpContext);
        return Accepted(Location(containerId, id), await recycling.CancelAsync((await identity.ReadAsync(HttpContext)).Actor, containerId, id, cancellationToken));
    }
    private bool Key(out string key)
    {
        key = Request.Headers["Idempotency-Key"].ToString();
        return Request.Headers["Idempotency-Key"].Count == 1 && Guid.TryParseExact(key, "D", out var value) && value != Guid.Empty && value.ToString("D") == key;
    }
    private bool Matches(Guid id) => Key(out var key) && key == id.ToString("D");
    private static string Location(string containerId, Guid id) => $"/api/v1/containers/{containerId}/files/recycle-restores/{id:D}";
}
[System.Text.Json.Serialization.JsonConverter(typeof(ContainerRecycleRestorePlanBodyConverter))]
public sealed record ContainerRecycleRestorePlanBody(Guid SourceIntentId);

[ApiController, Authorize(Policy = "Administrator"), ContainerRecycleRestoreInput, Route("api/v1/files/container-recycle-restores/recovery")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None), RequestSizeLimit(65536)]
public sealed class ContainerRecycleRestoreRecoveryController(ContainerRecycleRestoreWorkflowService recycling, TerminalCookieIdentity identity) : ControllerBase
{
    /// <summary>Bounded metadata for Unknown recycle operations, accessible to current administrators without impersonating the original user.</summary>
    [HttpGet, ContainerRecycleRestoreQuery(AllowTake = true)]
    public async Task<ActionResult<IReadOnlyList<ContainerRecycleRestoreIntentDto>>> List([FromQuery] int take = 50, CancellationToken cancellationToken = default) =>
        Ok(await recycling.UnresolvedAsync((await identity.ReadAsync(HttpContext)).Actor, take, cancellationToken));
    [HttpGet("{id:guid}"), ContainerRecycleRestoreQuery]
    public async Task<ActionResult<ContainerRecycleRestoreIntentDto>> Status(Guid id, CancellationToken cancellationToken) =>
        Ok(await recycling.RecoveryGetAsync((await identity.ReadAsync(HttpContext)).Actor, id, cancellationToken));
    /// <summary>Accept historical uncertainty only after the exact original Root operation is permanently fenced. Never deletes HDD metadata or reports them as successful.</summary>
    [HttpPost("{id:guid}/acknowledge"), ProducesResponseType<FileCancelDto>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Acknowledge(Guid id, AcknowledgeContainerRecycleRestoreBody command, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty || Request.Headers["Idempotency-Key"].Count != 1 || Request.Headers["Idempotency-Key"].ToString() != id.ToString("D")) return ContainerRecycleRestoreHttp.BadKey(HttpContext);
        await recycling.AcknowledgeAsync((await identity.ReadAsync(HttpContext)).Actor, id, new(command.Version, command.Confirmed), cancellationToken);
        return Accepted(new FileCancelDto(id, "Acknowledged"));
    }
}

internal static class ContainerRecycleRestoreHttp
{
    internal static BadRequestObjectResult BadCommand(HttpContext context) => new(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = "必须明确提供规范的原回收意图引用。",
        Extensions = { ["code"] = "CONTAINER_RECYCLE_RESTORE_COMMAND_REQUIRED", ["traceId"] = context.TraceIdentifier }
    });
    internal static BadRequestObjectResult BadKey(HttpContext context) => new(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = "必须提供与当前操作匹配的单个规范幂等键。",
        Extensions = { ["code"] = "CONTAINER_RECYCLE_RESTORE_IDEMPOTENCY_KEY_REQUIRED", ["traceId"] = context.TraceIdentifier }
    });
}
