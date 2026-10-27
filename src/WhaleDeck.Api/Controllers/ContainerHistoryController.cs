using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhaleDeck.Api.Security;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Api.Controllers;

[ApiController, Authorize(Policy = "Administrator")]
[Route("api/v1/containers/{containerId:regex((?-i:\\A[[a-f0-9]]+\\z)):length(64)}/files")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ContainerHistoryController(ContainerFileHistoryService history, ContainerFileRestoreService restores, TerminalCookieIdentity identity) : ControllerBase
{
    private const string Intent = "restores/{id:regex((?-i:\\A[[a-f0-9-]]+\\z)):length(36):guid}";
    /// <summary>Live instance-bound HDD history and permanent metadata observation; Present is not a full byte verification. Pagination freezes its inventory version.</summary>
    [HttpGet("history")]
    public async Task<ActionResult<ContainerHistoryPageDto>> List(string containerId, [FromQuery] string instanceId, [FromQuery] string path = "",
        [FromQuery] string after = "", [FromQuery] int take = 50, [FromQuery] string expectedVersion = "", CancellationToken cancellationToken = default) =>
        Ok(await history.ListAsync((await identity.ReadAsync(HttpContext)).Actor, new(containerId, instanceId, path), after, take, expectedVersion, cancellationToken));

    /// <summary>Five-minute restoration plan binding an independently selected same-container destination, original login and exact HDD source metadata. Full source bytes are verified by Worker before staging can commit.</summary>
    [HttpPost("restores/plan")]
    public async Task<ActionResult<ContainerFileRestorePlanDto>> Plan(string containerId, ContainerRestorePlanBody command, CancellationToken cancellationToken) =>
        Ok(await restores.PlanAsync((await identity.ReadAsync(HttpContext)).Actor,
            new(new(containerId, command.InstanceId, command.Path), command.CaptureId, command.ExpectedCaptureHash, command.ExpectedVersion, command.RestoreAttributes), cancellationToken));

    /// <summary>Confirmed permanent Job/Outbox and immutable source. No browser body upload; Worker pulls only the approved HDD capture through signed Agent RPC.</summary>
    [HttpPost("restores"), ProducesResponseType<ContainerFileUploadIntentDto>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<ContainerFileUploadIntentDto>> Create(string containerId, SubmitFileUploadCommand command, CancellationToken cancellationToken)
    {
        if (!KeyMatches(command.IdempotencyKey)) return BadRequest();
        var result = await restores.SubmitAsync((await identity.ReadAsync(HttpContext)).Actor, containerId, command, cancellationToken);
        return Accepted($"/api/v1/containers/{containerId}/files/restores/{result.Id:D}", result);
    }
    [HttpGet(Intent)]
    public async Task<ActionResult<ContainerFileUploadIntentDto>> Status(string containerId, Guid id, CancellationToken cancellationToken) =>
        Ok(await restores.GetAsync((await identity.ReadAsync(HttpContext)).Actor, containerId, id, cancellationToken));

    [HttpPost(Intent + "/cancel"), ProducesResponseType<FileCancelDto>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Cancel(string containerId, Guid id, CancellationToken cancellationToken)
    {
        if (!KeyMatches(id.ToString("D"))) return BadRequest();
        await restores.CancelAsync((await identity.ReadAsync(HttpContext)).Actor, containerId, id, cancellationToken);
        return Accepted(new FileCancelDto(id, "CancelRequested"));
    }
    private bool KeyMatches(string key) => Request.Headers["Idempotency-Key"].Count == 1 && Request.Headers["Idempotency-Key"].ToString() == key;
}

public sealed record ContainerRestorePlanBody(string InstanceId, string Path, string CaptureId, string ExpectedCaptureHash, string ExpectedVersion, bool RestoreAttributes = false);
