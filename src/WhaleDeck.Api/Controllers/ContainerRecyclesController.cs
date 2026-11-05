using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhaleDeck.Api.Security;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Api.Controllers;

[ApiController, Authorize(Policy = "Administrator"), ContainerRecycleInput]
[Route("api/v1/containers/{containerId:regex((?-i:\\A[[a-f0-9]]+\\z)):length(64)}/files/recycles")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None), RequestSizeLimit(65536)]
public sealed class ContainerRecyclesController(ContainerRecycleWorkflowService recycling, TerminalCookieIdentity identity) : ControllerBase
{
    private const string Intent = "{id:regex((?-i:\\A[[a-f0-9-]]+\\z)):length(36):guid}";
    /// <summary>Queue bounded, asynchronous source preflight. No file recycling occurs until the original login confirms its completed plan.</summary>
    [HttpPost, Consumes("application/json"), ProducesResponseType<ContainerRecycleIntentDto>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<ContainerRecycleIntentDto>> Queue(string containerId, ContainerRecyclePlanBody command, CancellationToken cancellationToken)
    {
        if (!Key(out var key)) return ContainerRecycleHttp.BadKey(HttpContext);
        if (command is null) return ContainerRecycleHttp.BadCommand(HttpContext);
        var result = await recycling.QueueAsync((await identity.ReadAsync(HttpContext)).Actor,
            new(new(containerId, command.InstanceId, command.Path), command.ExpectedVersion), key, cancellationToken);
        return Accepted(Location(containerId, result.Id), result);
    }
    [HttpGet(Intent), ContainerRecycleQuery]
    public async Task<ActionResult<ContainerRecycleIntentDto>> Status(string containerId, Guid id, CancellationToken cancellationToken) =>
        Ok(await recycling.GetAsync((await identity.ReadAsync(HttpContext)).Actor, containerId, id, cancellationToken));
    /// <summary>Read the original registration after a lost HTTP reply. A missing result never authorizes generating another key.</summary>
    [HttpGet("by-key/{key:guid}"), ContainerRecycleQuery]
    public async Task<ActionResult<ContainerRecycleIntentDto>> ByKey(string containerId, string key, CancellationToken cancellationToken) =>
        Ok(await recycling.GetByKeyAsync((await identity.ReadAsync(HttpContext)).Actor, containerId, key, cancellationToken));
    /// <summary>Confirm the five-minute immutable plan. Retries resolve the same execution job; no file recycling runs within this HTTP request.</summary>
    [HttpPost(Intent + "/confirm"), ProducesResponseType<ContainerRecycleIntentDto>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<ContainerRecycleIntentDto>> Confirm(string containerId, Guid id, ConfirmContainerRecycleBody command, CancellationToken cancellationToken)
    {
        if (!Matches(id)) return ContainerRecycleHttp.BadKey(HttpContext);
        var result = await recycling.ConfirmAsync((await identity.ReadAsync(HttpContext)).Actor, containerId, id, new(command.PlanHash, command.Confirmed, command.Version), cancellationToken);
        return Accepted(Location(containerId, id), result);
    }
    [HttpPost(Intent + "/cancel"), ProducesResponseType<ContainerRecycleIntentDto>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<ContainerRecycleIntentDto>> Cancel(string containerId, Guid id, CancellationToken cancellationToken)
    {
        if (!Matches(id)) return ContainerRecycleHttp.BadKey(HttpContext);
        return Accepted(Location(containerId, id), await recycling.CancelAsync((await identity.ReadAsync(HttpContext)).Actor, containerId, id, cancellationToken));
    }
    private bool Key(out string key)
    {
        key = Request.Headers["Idempotency-Key"].ToString();
        return Request.Headers["Idempotency-Key"].Count == 1 && Guid.TryParseExact(key, "D", out var value) && value != Guid.Empty && value.ToString("D") == key;
    }
    private bool Matches(Guid id) => Key(out var key) && key == id.ToString("D");
    private static string Location(string containerId, Guid id) => $"/api/v1/containers/{containerId}/files/recycles/{id:D}";
}
[System.Text.Json.Serialization.JsonConverter(typeof(ContainerRecyclePlanBodyConverter))]
public sealed record ContainerRecyclePlanBody(string InstanceId, string Path, string ExpectedVersion);

[ApiController, Authorize(Policy = "Administrator"), ContainerRecycleInput, Route("api/v1/files/container-recycles/recovery")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None), RequestSizeLimit(65536)]
public sealed class ContainerRecycleRecoveryController(ContainerRecycleWorkflowService recycling, TerminalCookieIdentity identity) : ControllerBase
{
    /// <summary>Bounded metadata for Unknown recycle operations, accessible to current administrators without impersonating the original user.</summary>
    [HttpGet, ContainerRecycleQuery(AllowTake = true)]
    public async Task<ActionResult<IReadOnlyList<ContainerRecycleIntentDto>>> List([FromQuery] int take = 50, CancellationToken cancellationToken = default) =>
        Ok(await recycling.UnresolvedAsync((await identity.ReadAsync(HttpContext)).Actor, take, cancellationToken));
    [HttpGet("{id:guid}"), ContainerRecycleQuery]
    public async Task<ActionResult<ContainerRecycleIntentDto>> Status(Guid id, CancellationToken cancellationToken) =>
        Ok(await recycling.RecoveryGetAsync((await identity.ReadAsync(HttpContext)).Actor, id, cancellationToken));
    /// <summary>Accept historical uncertainty only after the exact original Root operation is permanently fenced. Never deletes HDD metadata or reports them as successful.</summary>
    [HttpPost("{id:guid}/acknowledge"), ProducesResponseType<FileCancelDto>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Acknowledge(Guid id, AcknowledgeContainerRecycleBody command, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty || Request.Headers["Idempotency-Key"].Count != 1 || Request.Headers["Idempotency-Key"].ToString() != id.ToString("D")) return ContainerRecycleHttp.BadKey(HttpContext);
        await recycling.AcknowledgeAsync((await identity.ReadAsync(HttpContext)).Actor, id, new(command.Version, command.Confirmed), cancellationToken);
        return Accepted(new FileCancelDto(id, "Acknowledged"));
    }
}

internal static class ContainerRecycleHttp
{
    internal static BadRequestObjectResult BadCommand(HttpContext context) => new(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = "必须明确提供容器实例、文件路径和当前文件版本。",
        Extensions = { ["code"] = "CONTAINER_RECYCLE_COMMAND_REQUIRED", ["traceId"] = context.TraceIdentifier }
    });
    internal static BadRequestObjectResult BadKey(HttpContext context) => new(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = "必须提供与当前操作匹配的单个规范幂等键。",
        Extensions = { ["code"] = "CONTAINER_RECYCLE_IDEMPOTENCY_KEY_REQUIRED", ["traceId"] = context.TraceIdentifier }
    });
}
