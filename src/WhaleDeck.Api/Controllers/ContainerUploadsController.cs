using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhaleDeck.Api.Security;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Api.Controllers;

[ApiController, Authorize(Policy = "Administrator")]
[Route("api/v1/containers/{containerId:regex((?-i:\\A[[a-f0-9]]+\\z)):length(64)}/files/uploads")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ContainerUploadsController(ContainerFileUploadService uploads, TerminalCookieIdentity identity) : ControllerBase
{
    private const string Intent = "{id:regex((?-i:\\A[[a-f0-9-]]+\\z)):length(36):guid}";

    /// <summary>Five-minute plan bound to the original container instance, relative path, exact target version, complete size and SHA256. Requires the full minor41 recovery workflow.</summary>
    [HttpPost("plan")]
    public async Task<ActionResult<FileUploadPlanDto>> Plan(string containerId, ContainerUploadPlanBody command, CancellationToken cancellationToken) =>
        Ok(await uploads.PlanAsync((await identity.ReadAsync(HttpContext)).Actor,
            new(new(containerId, command.InstanceId, command.Path), command.SizeBytes, command.Sha256, command.ExpectedVersion), cancellationToken));

    /// <summary>Confirmed immutable intent, Job, Outbox and audit. 202 reserves this upload; it does not receive or publish any file bytes.</summary>
    [HttpPost, ProducesResponseType<ContainerFileUploadIntentDto>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<ContainerFileUploadIntentDto>> Create(string containerId, SubmitFileUploadCommand command, CancellationToken cancellationToken)
    {
        if (!KeyMatches(command.IdempotencyKey)) return BadRequest();
        var result = await uploads.SubmitAsync((await identity.ReadAsync(HttpContext)).Actor, containerId, command, cancellationToken);
        return Accepted(IntentLocation(containerId, result.Id), result);
    }

    [HttpGet(Intent)]
    public async Task<ActionResult<ContainerFileUploadIntentDto>> Status(string containerId, Guid id, CancellationToken cancellationToken) =>
        Ok(await uploads.GetAsync((await identity.ReadAsync(HttpContext)).Actor, containerId, id, cancellationToken));

    /// <summary>Single raw binary receive, at most 1 GiB. Exact Content-Length and original intent key are required; compression, ranges and resumption are rejected. Cookie, role and login are rechecked during reception. 202 means staged only; publication runs in the dedicated Worker.</summary>
    [HttpPut(Intent + "/content"), Consumes("application/octet-stream"), RequestSizeLimit(FileManagementService.MaximumBytes)]
    [ProducesResponseType<ContainerFileUploadIntentDto>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<ContainerFileUploadIntentDto>> Content(string containerId, Guid id, CancellationToken cancellationToken)
    {
        if (!KeyMatches(id.ToString("D")) || Request.ContentLength is null or < 0 or > FileManagementService.MaximumBytes ||
            Request.Headers.ContainsKey("Content-Encoding") || Request.Headers.ContainsKey("Content-Range") || Request.Headers.ContainsKey("Range")) return BadRequest();
        var login = await identity.ReadAsync(HttpContext);
        var result = await uploads.ReceiveAsync(login.Actor, containerId, id, Request.Body, Request.ContentLength,
            async token => { _ = await identity.ReadAsync(HttpContext, login.Key).WaitAsync(token); }, cancellationToken);
        return Accepted(IntentLocation(containerId, result.Id), result);
    }

    /// <summary>Queue durable cancellation of the original intent. A completed publication is never undone or mislabeled as canceled.</summary>
    [HttpPost(Intent + "/cancel"), ProducesResponseType<FileCancelDto>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Cancel(string containerId, Guid id, CancellationToken cancellationToken)
    {
        if (!KeyMatches(id.ToString("D"))) return BadRequest();
        await uploads.CancelAsync((await identity.ReadAsync(HttpContext)).Actor, containerId, id, cancellationToken);
        return Accepted(new FileCancelDto(id, "CancelRequested"));
    }
    private bool KeyMatches(string key) => Request.Headers["Idempotency-Key"].Count == 1 && Request.Headers["Idempotency-Key"].ToString() == key;
    private static string IntentLocation(string containerId, Guid id) => $"/api/v1/containers/{containerId}/files/uploads/{id:D}";
}

public sealed record ContainerUploadPlanBody(string InstanceId, string Path, long SizeBytes, string Sha256, string ExpectedVersion);
