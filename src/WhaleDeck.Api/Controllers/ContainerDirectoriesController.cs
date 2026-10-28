using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhaleDeck.Api.Security;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Api.Controllers;

[ApiController, Authorize(Policy = "Administrator")]
[Route("api/v1/containers/{containerId:regex((?-i:\\A[[a-f0-9]]+\\z)):length(64)}/files/directories")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ContainerDirectoriesController(ContainerDirectoryWorkflowService directories, TerminalCookieIdentity identity) : ControllerBase
{
    private const string Intent = "{id:regex((?-i:\\A[[a-f0-9-]]+\\z)):length(36):guid}";
    /// <summary>Five-minute human/login-bound exact container instance and parent plan. Single absent leaf only; kernel-derived owner, ordinary permission bits.</summary>
    [HttpPost("plan")]
    public async Task<ActionResult<ContainerDirectoryPlanResponse>> Plan(string containerId, ContainerDirectoryPlanBody command, CancellationToken token) =>
        Ok(await directories.PlanAsync((await identity.ReadAsync(HttpContext)).Actor, new(new(containerId, command.InstanceId, command.Path), command.Mode), token));
    /// <summary>Confirmed durable intent with Job/Outbox/audit. 202 is acceptance, not successful mkdir. Worker acquires the shared publication protection before signing Apply.</summary>
    [HttpPost, ProducesResponseType<ContainerDirectoryIntentDto>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<ContainerDirectoryIntentDto>> Create(string containerId, SubmitContainerDirectoryCommand command, CancellationToken token)
    {
        if (!KeyMatches(command.IdempotencyKey)) return BadRequest();
        var result = await directories.SubmitAsync((await identity.ReadAsync(HttpContext)).Actor, containerId, command, token);
        return Accepted(Location(containerId, result.Id), result);
    }
    [HttpGet(Intent)]
    public async Task<ActionResult<ContainerDirectoryIntentDto>> Status(string containerId, Guid id, CancellationToken token) =>
        Ok(await directories.GetAsync((await identity.ReadAsync(HttpContext)).Actor, containerId, id, token));
    /// <summary>Read-only resolution of the original registration. Null requires expiry and a transaction fence; never submits another task.</summary>
    [HttpPost("resolve")]
    public async Task<ActionResult<ContainerDirectoryIntentDto?>> Resolve(string containerId, SubmitContainerDirectoryCommand command, CancellationToken token)
    {
        if (!KeyMatches(command.IdempotencyKey)) return BadRequest();
        return Ok(await directories.ResolveAsync((await identity.ReadAsync(HttpContext)).Actor, containerId, command, token));
    }
    [HttpPost(Intent + "/cancel"), ProducesResponseType<FileCancelDto>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Cancel(string containerId, Guid id, CancellationToken token)
    {
        if (!KeyMatches(id.ToString("D"))) return BadRequest();
        await directories.CancelAsync((await identity.ReadAsync(HttpContext)).Actor, containerId, id, token); return Accepted(new FileCancelDto(id, "CancelRequested"));
    }
    private bool KeyMatches(string key) => Request.Headers["Idempotency-Key"].Count == 1 && Request.Headers["Idempotency-Key"].ToString() == key;
    private static string Location(string container, Guid id) => $"/api/v1/containers/{container}/files/directories/{id:D}";
}
public sealed record ContainerDirectoryPlanBody(string InstanceId, string Path, string Mode);

[ApiController, Authorize(Policy = "Administrator"), Route("api/v1/files/container-directories/recovery")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ContainerDirectoryRecoveryController(ContainerDirectoryWorkflowService directories, TerminalCookieIdentity identity) : ControllerBase
{
    /// <summary>Current administrators can investigate Unknown tasks even if the original login/user no longer exists. Metadata only, bounded oldest-first.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ContainerDirectoryIntentDto>>> List([FromQuery] int take = 50, CancellationToken token = default) =>
        Ok(await directories.UnknownAsync((await identity.ReadAsync(HttpContext)).Actor, take, token));
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ContainerDirectoryIntentDto>> Status(Guid id, CancellationToken token) =>
        Ok(await directories.RecoveryGetAsync((await identity.ReadAsync(HttpContext)).Actor, id, token));
    /// <summary>Version-bound explicit acceptance of historical uncertainty. Root terminal fence is proved before releasing SQL protection. Keeps Root evidence, fails the old task, never recreates/rolls back a directory.</summary>
    [HttpPost("{id:guid}/acknowledge"), ProducesResponseType<FileCancelDto>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Acknowledge(Guid id, AcknowledgeContainerDirectoryCommand command, CancellationToken token)
    {
        if (Request.Headers["Idempotency-Key"].Count != 1 || Request.Headers["Idempotency-Key"].ToString() != id.ToString("D")) return BadRequest();
        await directories.AcknowledgeAsync((await identity.ReadAsync(HttpContext)).Actor, id, command, token); return Accepted(new FileCancelDto(id, "Acknowledged"));
    }
}
