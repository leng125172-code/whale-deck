using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhaleDeck.Api.Security;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Api.Controllers;

[ApiController, Authorize(Policy = "Administrator")]
[Route("api/v1/containers/{containerId:regex((?-i:\\A[[a-f0-9]]+\\z)):length(64)}/files/copies")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ContainerCopiesController(ContainerCopyWorkflowService copies, TerminalCookieIdentity identity) : ControllerBase
{
    private const string Intent = "{id:regex((?-i:\\A[[a-f0-9-]]+\\z)):length(36):guid}";
    /// <summary>Queue bounded, asynchronous source preflight. No copy occurs until the original login confirms its completed plan.</summary>
    [HttpPost, ProducesResponseType<ContainerCopyIntentDto>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<ContainerCopyIntentDto>> Queue(string containerId, ContainerCopyPlanBody command, CancellationToken cancellationToken)
    {
        if (!Key(out var key)) return BadRequest();
        var result = await copies.QueueAsync((await identity.ReadAsync(HttpContext)).Actor,
            new(new(containerId, command.InstanceId, command.SourcePath), command.TargetPath, command.ExpectedVersion), key, cancellationToken);
        return Accepted(Location(containerId, result.Id), result);
    }
    [HttpGet(Intent)]
    public async Task<ActionResult<ContainerCopyIntentDto>> Status(string containerId, Guid id, CancellationToken cancellationToken) =>
        Ok(await copies.GetAsync((await identity.ReadAsync(HttpContext)).Actor, containerId, id, cancellationToken));
    /// <summary>Read the original registration after a lost HTTP reply. A missing result never authorizes generating another key.</summary>
    [HttpGet("by-key/{key:guid}")]
    public async Task<ActionResult<ContainerCopyIntentDto>> ByKey(string containerId, string key, CancellationToken cancellationToken) =>
        Ok(await copies.GetByKeyAsync((await identity.ReadAsync(HttpContext)).Actor, containerId, key, cancellationToken));
    /// <summary>Confirm the five-minute immutable plan. Retries resolve the same execution job; no scan or copy runs within this HTTP request.</summary>
    [HttpPost(Intent + "/confirm"), ProducesResponseType<ContainerCopyIntentDto>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<ContainerCopyIntentDto>> Confirm(string containerId, Guid id, ConfirmContainerCopyCommand command, CancellationToken cancellationToken)
    {
        if (!Matches(id)) return BadRequest();
        var result = await copies.ConfirmAsync((await identity.ReadAsync(HttpContext)).Actor, containerId, id, command, cancellationToken);
        return Accepted(Location(containerId, id), result);
    }
    [HttpPost(Intent + "/cancel"), ProducesResponseType<ContainerCopyIntentDto>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<ContainerCopyIntentDto>> Cancel(string containerId, Guid id, CancellationToken cancellationToken)
    {
        if (!Matches(id)) return BadRequest();
        return Accepted(Location(containerId, id), await copies.CancelAsync((await identity.ReadAsync(HttpContext)).Actor, containerId, id, cancellationToken));
    }
    private bool Key(out string key)
    {
        key = Request.Headers["Idempotency-Key"].ToString();
        return Request.Headers["Idempotency-Key"].Count == 1 && Guid.TryParseExact(key, "D", out var value) && value != Guid.Empty && value.ToString("D") == key;
    }
    private bool Matches(Guid id) => Key(out var key) && key == id.ToString("D");
    private static string Location(string containerId, Guid id) => $"/api/v1/containers/{containerId}/files/copies/{id:D}";
}
public sealed record ContainerCopyPlanBody(string InstanceId, string SourcePath, string TargetPath, string ExpectedVersion);

[ApiController, Authorize(Policy = "Administrator"), Route("api/v1/files/container-copies/recovery")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ContainerCopyRecoveryController(ContainerCopyWorkflowService copies, TerminalCookieIdentity identity) : ControllerBase
{
    /// <summary>Bounded metadata for Unknown copies, accessible to current administrators without impersonating the original user.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ContainerCopyIntentDto>>> List([FromQuery] int take = 50, CancellationToken cancellationToken = default) =>
        Ok(await copies.UnknownAsync((await identity.ReadAsync(HttpContext)).Actor, take, cancellationToken));
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ContainerCopyIntentDto>> Status(Guid id, CancellationToken cancellationToken) =>
        Ok(await copies.RecoveryGetAsync((await identity.ReadAsync(HttpContext)).Actor, id, cancellationToken));
    /// <summary>Accept historical uncertainty only after the exact original Root operation is permanently fenced. Never deletes private copies or reports them as successful.</summary>
    [HttpPost("{id:guid}/acknowledge"), ProducesResponseType<FileCancelDto>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Acknowledge(Guid id, AcknowledgeContainerCopyCommand command, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty || Request.Headers["Idempotency-Key"].Count != 1 || Request.Headers["Idempotency-Key"].ToString() != id.ToString("D")) return BadRequest();
        await copies.AcknowledgeAsync((await identity.ReadAsync(HttpContext)).Actor, id, command, cancellationToken);
        return Accepted(new FileCancelDto(id, "Acknowledged"));
    }
}
