using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhaleDeck.Api.Security;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Api.Controllers;

[ApiController, Authorize(Policy = "Administrator")]
[Route("api/v1/containers/{containerId:regex((?-i:\\A[[a-f0-9]]+\\z)):length(64)}/files/moves")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ContainerMovesController(ContainerMoveWorkflowService moves, TerminalCookieIdentity identity) : ControllerBase
{
    private const string Intent = "{id:regex((?-i:\\A[[a-f0-9-]]+\\z)):length(36):guid}";
    /// <summary>Queue bounded, asynchronous source preflight. No move occurs until the original login confirms its completed plan.</summary>
    [HttpPost, ProducesResponseType<ContainerMoveIntentDto>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<ContainerMoveIntentDto>> Queue(string containerId, ContainerMovePlanBody command, CancellationToken cancellationToken)
    {
        if (!Key(out var key)) return ContainerMoveHttp.BadKey(HttpContext);
        var result = await moves.QueueAsync((await identity.ReadAsync(HttpContext)).Actor,
            new(new(containerId, command.InstanceId, command.SourcePath), command.TargetPath, command.ExpectedVersion), key, cancellationToken);
        return Accepted(Location(containerId, result.Id), result);
    }
    [HttpGet(Intent)]
    public async Task<ActionResult<ContainerMoveIntentDto>> Status(string containerId, Guid id, CancellationToken cancellationToken) =>
        Ok(await moves.GetAsync((await identity.ReadAsync(HttpContext)).Actor, containerId, id, cancellationToken));
    /// <summary>Read the original registration after a lost HTTP reply. A missing result never authorizes generating another key.</summary>
    [HttpGet("by-key/{key:guid}")]
    public async Task<ActionResult<ContainerMoveIntentDto>> ByKey(string containerId, string key, CancellationToken cancellationToken) =>
        Ok(await moves.GetByKeyAsync((await identity.ReadAsync(HttpContext)).Actor, containerId, key, cancellationToken));
    /// <summary>Confirm the five-minute immutable plan. Retries resolve the same execution job; no scan or move runs within this HTTP request.</summary>
    [HttpPost(Intent + "/confirm"), ProducesResponseType<ContainerMoveIntentDto>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<ContainerMoveIntentDto>> Confirm(string containerId, Guid id, ConfirmContainerMoveCommand command, CancellationToken cancellationToken)
    {
        if (!Matches(id)) return ContainerMoveHttp.BadKey(HttpContext);
        var result = await moves.ConfirmAsync((await identity.ReadAsync(HttpContext)).Actor, containerId, id, command, cancellationToken);
        return Accepted(Location(containerId, id), result);
    }
    [HttpPost(Intent + "/cancel"), ProducesResponseType<ContainerMoveIntentDto>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<ContainerMoveIntentDto>> Cancel(string containerId, Guid id, CancellationToken cancellationToken)
    {
        if (!Matches(id)) return ContainerMoveHttp.BadKey(HttpContext);
        return Accepted(Location(containerId, id), await moves.CancelAsync((await identity.ReadAsync(HttpContext)).Actor, containerId, id, cancellationToken));
    }
    private bool Key(out string key)
    {
        key = Request.Headers["Idempotency-Key"].ToString();
        return Request.Headers["Idempotency-Key"].Count == 1 && Guid.TryParseExact(key, "D", out var value) && value != Guid.Empty && value.ToString("D") == key;
    }
    private bool Matches(Guid id) => Key(out var key) && key == id.ToString("D");
    private static string Location(string containerId, Guid id) => $"/api/v1/containers/{containerId}/files/moves/{id:D}";
}
public sealed record ContainerMovePlanBody(string InstanceId, string SourcePath, string TargetPath, string ExpectedVersion);

[ApiController, Authorize(Policy = "Administrator"), Route("api/v1/files/container-moves/recovery")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ContainerMoveRecoveryController(ContainerMoveWorkflowService moves, TerminalCookieIdentity identity) : ControllerBase
{
    /// <summary>Bounded metadata for Unknown moves, accessible to current administrators without impersonating the original user.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ContainerMoveIntentDto>>> List([FromQuery] int take = 50, CancellationToken cancellationToken = default) =>
        Ok(await moves.UnknownAsync((await identity.ReadAsync(HttpContext)).Actor, take, cancellationToken));
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ContainerMoveIntentDto>> Status(Guid id, CancellationToken cancellationToken) =>
        Ok(await moves.RecoveryGetAsync((await identity.ReadAsync(HttpContext)).Actor, id, cancellationToken));
    /// <summary>Accept historical uncertainty only after the exact original Root operation is permanently fenced. Never deletes private moves or reports them as successful.</summary>
    [HttpPost("{id:guid}/acknowledge"), ProducesResponseType<FileCancelDto>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Acknowledge(Guid id, AcknowledgeContainerMoveCommand command, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty || Request.Headers["Idempotency-Key"].Count != 1 || Request.Headers["Idempotency-Key"].ToString() != id.ToString("D")) return ContainerMoveHttp.BadKey(HttpContext);
        await moves.AcknowledgeAsync((await identity.ReadAsync(HttpContext)).Actor, id, command, cancellationToken);
        return Accepted(new FileCancelDto(id, "Acknowledged"));
    }
}

internal static class ContainerMoveHttp
{
    internal static BadRequestObjectResult BadKey(HttpContext context) => new(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = "A single canonical idempotency key matching this operation is required.",
        Extensions = { ["code"] = "CONTAINER_MOVE_IDEMPOTENCY_KEY_REQUIRED", ["traceId"] = context.TraceIdentifier }
    });
}
