using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhaleDeck.Api.Security;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Api.Controllers;

[ApiController]
[Authorize(Policy = "Administrator")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/v1/notifications/manage/channels/{id:guid}/private")]
public sealed class NotificationPrivateController(NotificationPrivateWorkflow workflow, IIdentityDirectory identity) : ControllerBase
{
    /// <summary>Redacted host state only: no endpoint, mailbox, password or token.</summary>
    [HttpGet]
    public async Task<ActionResult<NotificationPrivateMetadata>> Inspect(Guid id, CancellationToken cancellationToken) => Ok(await workflow.InspectAsync(await Context(cancellationToken), id, cancellationToken));
    /// <summary>Prepare an actor/channel/input-bound five-minute plan using an owner-bound staged secret ticket. Private values are never returned.</summary>
    [HttpPost("plan")]
    public async Task<ActionResult<NotificationPrivatePlanDto>> Plan(Guid id, NotificationPrivatePlanCommand command, CancellationToken cancellationToken) => Ok(await workflow.PlanAsync(await Context(cancellationToken), id, command, cancellationToken));
    /// <summary>Confirmed, idempotent asynchronous private update. Pauses the channel; completion does not enable external sending.</summary>
    [HttpPost]
    public async Task<ActionResult<JobDto>> Submit(Guid id, NotificationPrivateSubmitCommand command, CancellationToken cancellationToken) => Accepted(await workflow.SubmitAsync(await Context(cancellationToken), id, command, cancellationToken));
    [HttpGet("status")]
    public async Task<ActionResult<NotificationPrivateStatusDto?>> Status(Guid id, CancellationToken cancellationToken) => Ok(await workflow.StatusAsync(await Context(cancellationToken), id, cancellationToken));
    /// <summary>Accept an unknown outcome without enabling sending. New acknowledgments require an expired lease and a fresh root receipt check; exact accepted requests replay from durable audit without Agent access. Reconfiguration remains required.</summary>
    [HttpPost("acknowledge-unknown")]
    public async Task<IActionResult> Acknowledge(Guid id, AcknowledgeNotificationPrivateCommand command, CancellationToken cancellationToken)
    { await workflow.AcknowledgeAsync(await Context(cancellationToken), id, command, cancellationToken); return NoContent(); }
    private async Task<NotificationContext> Context(CancellationToken cancellationToken)
    {
        var user = await identity.ResolveCurrentAsync(User.RequireSubject(), User.Identity?.Name, User.FindAll("groups").Select(item => item.Value).ToArray(), cancellationToken);
        return new(user.Subject, user.IsAdministrator, HttpContext.TraceIdentifier, HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers["Idempotency-Key"].Count == 1 ? Request.Headers["Idempotency-Key"].ToString() : null);
    }
}
