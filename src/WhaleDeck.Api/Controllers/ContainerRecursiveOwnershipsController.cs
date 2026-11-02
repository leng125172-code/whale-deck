using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhaleDeck.Api.Security;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Api.Controllers;

[ApiController, Authorize(Policy = "Administrator"), ContainerRecursiveOwnershipInput]
[Route("api/v1/containers/{containerId:regex((?-i:\\A[[a-f0-9]]+\\z)):length(64)}/files/recursive-ownerships")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None), RequestSizeLimit(65536)]
public sealed class ContainerRecursiveOwnershipsController(ContainerRecursiveOwnershipWorkflowService recursiveOwnerships, TerminalCookieIdentity identity) : ControllerBase
{
    private const string Intent = "{id:regex((?-i:\\A[[a-f0-9-]]+\\z)):length(36):guid}";
    /// <summary>Queue bounded, asynchronous source preflight. No recursive ownership change occurs until the original login confirms its completed plan.</summary>
    [HttpPost, Consumes("application/json"), ProducesResponseType<ContainerRecursiveOwnershipIntentDto>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<ContainerRecursiveOwnershipIntentDto>> Queue(string containerId, ContainerRecursiveOwnershipPlanBody command, CancellationToken cancellationToken)
    {
        if (!Key(out var key)) return ContainerRecursiveOwnershipHttp.BadKey(HttpContext);
        if (command is null || command.Uid is null || command.Gid is null || command.Uid == uint.MaxValue || command.Gid == uint.MaxValue) return ContainerRecursiveOwnershipHttp.BadOwner(HttpContext);
        var result = await recursiveOwnerships.QueueAsync((await identity.ReadAsync(HttpContext)).Actor,
            new(new(containerId, command.InstanceId, command.Path), command.Uid!.Value, command.Gid!.Value, command.ExpectedVersion), key, cancellationToken);
        return Accepted(Location(containerId, result.Id), result);
    }
    [HttpGet(Intent)]
    public async Task<ActionResult<ContainerRecursiveOwnershipIntentDto>> Status(string containerId, Guid id, CancellationToken cancellationToken) =>
        Ok(await recursiveOwnerships.GetAsync((await identity.ReadAsync(HttpContext)).Actor, containerId, id, cancellationToken));
    /// <summary>Read the original registration after a lost HTTP reply. A missing result never authorizes generating another key.</summary>
    [HttpGet("by-key/{key:guid}")]
    public async Task<ActionResult<ContainerRecursiveOwnershipIntentDto>> ByKey(string containerId, string key, CancellationToken cancellationToken) =>
        Ok(await recursiveOwnerships.GetByKeyAsync((await identity.ReadAsync(HttpContext)).Actor, containerId, key, cancellationToken));
    /// <summary>Confirm the five-minute immutable plan. Retries resolve the same execution job; no recursive ownership mutation runs within this HTTP request.</summary>
    [HttpPost(Intent + "/confirm"), ProducesResponseType<ContainerRecursiveOwnershipIntentDto>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<ContainerRecursiveOwnershipIntentDto>> Confirm(string containerId, Guid id, ConfirmContainerRecursiveOwnershipBody command, CancellationToken cancellationToken)
    {
        if (!Matches(id)) return ContainerRecursiveOwnershipHttp.BadKey(HttpContext);
        var result = await recursiveOwnerships.ConfirmAsync((await identity.ReadAsync(HttpContext)).Actor, containerId, id, new(command.PlanHash, command.Confirmed, command.Version), cancellationToken);
        return Accepted(Location(containerId, id), result);
    }
    [HttpPost(Intent + "/cancel"), ProducesResponseType<ContainerRecursiveOwnershipIntentDto>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<ContainerRecursiveOwnershipIntentDto>> Cancel(string containerId, Guid id, CancellationToken cancellationToken)
    {
        if (!Matches(id)) return ContainerRecursiveOwnershipHttp.BadKey(HttpContext);
        return Accepted(Location(containerId, id), await recursiveOwnerships.CancelAsync((await identity.ReadAsync(HttpContext)).Actor, containerId, id, cancellationToken));
    }
    private bool Key(out string key)
    {
        key = Request.Headers["Idempotency-Key"].ToString();
        return Request.Headers["Idempotency-Key"].Count == 1 && Guid.TryParseExact(key, "D", out var value) && value != Guid.Empty && value.ToString("D") == key;
    }
    private bool Matches(Guid id) => Key(out var key) && key == id.ToString("D");
    private static string Location(string containerId, Guid id) => $"/api/v1/containers/{containerId}/files/recursive-ownerships/{id:D}";
}
[System.Text.Json.Serialization.JsonConverter(typeof(ContainerRecursiveOwnershipPlanBodyConverter))]
public sealed record ContainerRecursiveOwnershipPlanBody(string InstanceId, string Path, uint? Uid, uint? Gid, string ExpectedVersion);

[ApiController, Authorize(Policy = "Administrator"), ContainerRecursiveOwnershipInput, Route("api/v1/files/container-recursive-ownerships/recovery")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None), RequestSizeLimit(65536)]
public sealed class ContainerRecursiveOwnershipRecoveryController(ContainerRecursiveOwnershipWorkflowService recursiveOwnerships, TerminalCookieIdentity identity) : ControllerBase
{
    /// <summary>Bounded metadata for Partial/Unknown recursive operations, accessible to current administrators without impersonating the original user.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ContainerRecursiveOwnershipIntentDto>>> List([FromQuery] int take = 50, CancellationToken cancellationToken = default) =>
        Ok(await recursiveOwnerships.UnresolvedAsync((await identity.ReadAsync(HttpContext)).Actor, take, cancellationToken));
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ContainerRecursiveOwnershipIntentDto>> Status(Guid id, CancellationToken cancellationToken) =>
        Ok(await recursiveOwnerships.RecoveryGetAsync((await identity.ReadAsync(HttpContext)).Actor, id, cancellationToken));
    /// <summary>Accept historical uncertainty only after the exact original Root operation is permanently fenced. Never deletes HDD metadata or reports them as successful.</summary>
    [HttpPost("{id:guid}/acknowledge"), ProducesResponseType<FileCancelDto>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Acknowledge(Guid id, AcknowledgeContainerRecursiveOwnershipBody command, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty || Request.Headers["Idempotency-Key"].Count != 1 || Request.Headers["Idempotency-Key"].ToString() != id.ToString("D")) return ContainerRecursiveOwnershipHttp.BadKey(HttpContext);
        await recursiveOwnerships.AcknowledgeAsync((await identity.ReadAsync(HttpContext)).Actor, id, new(command.Version, command.Confirmed), cancellationToken);
        return Accepted(new FileCancelDto(id, "Acknowledged"));
    }
}

internal static class ContainerRecursiveOwnershipHttp
{
    internal static BadRequestObjectResult BadOwner(HttpContext context) => new(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = "必须明确提供合法的目标UID和GID（0表示root）。",
        Extensions = { ["code"] = "CONTAINER_RECURSIVE_OWNERSHIP_UID_GID_REQUIRED", ["traceId"] = context.TraceIdentifier }
    });
    internal static BadRequestObjectResult BadKey(HttpContext context) => new(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = "必须提供与当前操作匹配的单个规范幂等键。",
        Extensions = { ["code"] = "CONTAINER_RECURSIVE_OWNERSHIP_IDEMPOTENCY_KEY_REQUIRED", ["traceId"] = context.TraceIdentifier }
    });
}
