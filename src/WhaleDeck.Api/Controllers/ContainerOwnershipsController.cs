using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhaleDeck.Api.Security;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Api.Controllers;

[ApiController, Authorize(Policy = "Administrator"), ContainerOwnershipInput]
[Route("api/v1/containers/{containerId:regex((?-i:\\A[[a-f0-9]]+\\z)):length(64)}/files/ownerships")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None), RequestSizeLimit(65536)]
public sealed class ContainerOwnershipsController(ContainerOwnershipWorkflowService ownerships, TerminalCookieIdentity identity) : ControllerBase
{
    private const string Intent = "{id:regex((?-i:\\A[[a-f0-9-]]+\\z)):length(36):guid}";
    /// <summary>Queue bounded, asynchronous source preflight. No ownership change occurs until the original login confirms its completed plan.</summary>
    [HttpPost, Consumes("application/json"), ProducesResponseType<ContainerOwnershipIntentDto>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<ContainerOwnershipIntentDto>> Queue(string containerId, ContainerOwnershipPlanBody command, CancellationToken cancellationToken)
    {
        if (!Key(out var key)) return ContainerOwnershipHttp.BadKey(HttpContext);
        if (command is null || command.Uid is null || command.Gid is null || command.Uid == uint.MaxValue || command.Gid == uint.MaxValue) return ContainerOwnershipHttp.BadOwner(HttpContext);
        var result = await ownerships.QueueAsync((await identity.ReadAsync(HttpContext)).Actor,
            new(new(containerId, command.InstanceId, command.Path), command.Uid!.Value, command.Gid!.Value, command.ExpectedVersion), key, cancellationToken);
        return Accepted(Location(containerId, result.Id), result);
    }
    [HttpGet(Intent)]
    public async Task<ActionResult<ContainerOwnershipIntentDto>> Status(string containerId, Guid id, CancellationToken cancellationToken) =>
        Ok(await ownerships.GetAsync((await identity.ReadAsync(HttpContext)).Actor, containerId, id, cancellationToken));
    /// <summary>Read the original registration after a lost HTTP reply. A missing result never authorizes generating another key.</summary>
    [HttpGet("by-key/{key:guid}")]
    public async Task<ActionResult<ContainerOwnershipIntentDto>> ByKey(string containerId, string key, CancellationToken cancellationToken) =>
        Ok(await ownerships.GetByKeyAsync((await identity.ReadAsync(HttpContext)).Actor, containerId, key, cancellationToken));
    /// <summary>Confirm the five-minute immutable plan. Retries resolve the same execution job; no ownership mutation runs within this HTTP request.</summary>
    [HttpPost(Intent + "/confirm"), ProducesResponseType<ContainerOwnershipIntentDto>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<ContainerOwnershipIntentDto>> Confirm(string containerId, Guid id, ConfirmContainerOwnershipCommand command, CancellationToken cancellationToken)
    {
        if (!Matches(id)) return ContainerOwnershipHttp.BadKey(HttpContext);
        var result = await ownerships.ConfirmAsync((await identity.ReadAsync(HttpContext)).Actor, containerId, id, command, cancellationToken);
        return Accepted(Location(containerId, id), result);
    }
    [HttpPost(Intent + "/cancel"), ProducesResponseType<ContainerOwnershipIntentDto>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<ContainerOwnershipIntentDto>> Cancel(string containerId, Guid id, CancellationToken cancellationToken)
    {
        if (!Matches(id)) return ContainerOwnershipHttp.BadKey(HttpContext);
        return Accepted(Location(containerId, id), await ownerships.CancelAsync((await identity.ReadAsync(HttpContext)).Actor, containerId, id, cancellationToken));
    }
    private bool Key(out string key)
    {
        key = Request.Headers["Idempotency-Key"].ToString();
        return Request.Headers["Idempotency-Key"].Count == 1 && Guid.TryParseExact(key, "D", out var value) && value != Guid.Empty && value.ToString("D") == key;
    }
    private bool Matches(Guid id) => Key(out var key) && key == id.ToString("D");
    private static string Location(string containerId, Guid id) => $"/api/v1/containers/{containerId}/files/ownerships/{id:D}";
}
[System.Text.Json.Serialization.JsonConverter(typeof(ContainerOwnershipPlanBodyConverter))]
public sealed record ContainerOwnershipPlanBody(string InstanceId, string Path, uint? Uid, uint? Gid, string ExpectedVersion);

[ApiController, Authorize(Policy = "Administrator"), ContainerOwnershipInput, Route("api/v1/files/container-ownerships/recovery")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None), RequestSizeLimit(65536)]
public sealed class ContainerOwnershipRecoveryController(ContainerOwnershipWorkflowService ownerships, TerminalCookieIdentity identity) : ControllerBase
{
    /// <summary>Bounded metadata for Unknown ownership operations, accessible to current administrators without impersonating the original user.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ContainerOwnershipIntentDto>>> List([FromQuery] int take = 50, CancellationToken cancellationToken = default) =>
        Ok(await ownerships.UnknownAsync((await identity.ReadAsync(HttpContext)).Actor, take, cancellationToken));
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ContainerOwnershipIntentDto>> Status(Guid id, CancellationToken cancellationToken) =>
        Ok(await ownerships.RecoveryGetAsync((await identity.ReadAsync(HttpContext)).Actor, id, cancellationToken));
    /// <summary>Accept historical uncertainty only after the exact original Root operation is permanently fenced. Never deletes HDD metadata or reports them as successful.</summary>
    [HttpPost("{id:guid}/acknowledge"), ProducesResponseType<FileCancelDto>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Acknowledge(Guid id, AcknowledgeContainerOwnershipCommand command, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty || Request.Headers["Idempotency-Key"].Count != 1 || Request.Headers["Idempotency-Key"].ToString() != id.ToString("D")) return ContainerOwnershipHttp.BadKey(HttpContext);
        await ownerships.AcknowledgeAsync((await identity.ReadAsync(HttpContext)).Actor, id, command, cancellationToken);
        return Accepted(new FileCancelDto(id, "Acknowledged"));
    }
}

internal static class ContainerOwnershipHttp
{
    internal static BadRequestObjectResult BadOwner(HttpContext context) => new(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = "必须明确提供合法的目标UID和GID（0为合法值）。",
        Extensions = { ["code"] = "CONTAINER_OWNERSHIP_UID_GID_REQUIRED", ["traceId"] = context.TraceIdentifier }
    });
    internal static BadRequestObjectResult BadKey(HttpContext context) => new(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = "A single canonical idempotency key matching this operation is required.",
        Extensions = { ["code"] = "CONTAINER_OWNERSHIP_IDEMPOTENCY_KEY_REQUIRED", ["traceId"] = context.TraceIdentifier }
    });
}
