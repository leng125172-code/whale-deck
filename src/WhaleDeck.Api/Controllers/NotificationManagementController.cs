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
[Route("api/v1/notifications/manage")]
public sealed class NotificationManagementController(NotificationManagementService notifications, IIdentityDirectory identity) : ControllerBase
{
    /// <summary>Administrator channel metadata; credentials and their reference names are never returned.</summary>
    [HttpGet("channels")]
    public async Task<ActionResult<NotificationManagementPage<NotificationChannelDto>>> Channels([FromQuery] int page = 1, [FromQuery] int take = 25, CancellationToken cancellationToken = default) =>
        Ok(await notifications.ListChannelsAsync(await Context(cancellationToken), new(page, take), cancellationToken));
    [HttpPost("channels")]
    public async Task<ActionResult<NotificationChannelDto>> CreateChannel(SaveNotificationChannelCommand command, CancellationToken cancellationToken) =>
        Ok(await notifications.SaveChannelAsync(await Context(cancellationToken), null, command, cancellationToken));
    [HttpPut("channels/{id:guid}")]
    public async Task<ActionResult<NotificationChannelDto>> UpdateChannel(Guid id, SaveNotificationChannelCommand command, CancellationToken cancellationToken) =>
        Ok(await notifications.SaveChannelAsync(await Context(cancellationToken), id, command, cancellationToken));
    [HttpDelete("channels/{id:guid}")]
    public async Task<IActionResult> DeleteChannel(Guid id, DeleteNotificationConfigurationCommand command, CancellationToken cancellationToken)
    { await notifications.DeleteChannelAsync(await Context(cancellationToken), id, command, cancellationToken); return NoContent(); }
    [HttpGet("routes")]
    public async Task<ActionResult<NotificationManagementPage<NotificationRouteDto>>> Routes([FromQuery] int page = 1, [FromQuery] int take = 25, [FromQuery] Guid? channelId = null, CancellationToken cancellationToken = default) =>
        Ok(await notifications.ListRoutesAsync(await Context(cancellationToken), new(page, take), channelId, cancellationToken));
    /// <summary>Confirmed routing changes invalidate queued pre-send intents for this channel; Sending is not canceled or resent.</summary>
    [HttpPost("routes")]
    public async Task<ActionResult<NotificationRouteDto>> CreateRoute(SaveNotificationRouteCommand command, CancellationToken cancellationToken) =>
        Ok(await notifications.SaveRouteAsync(await Context(cancellationToken), null, command, cancellationToken));
    [HttpPut("routes/{id:guid}")]
    public async Task<ActionResult<NotificationRouteDto>> UpdateRoute(Guid id, SaveNotificationRouteCommand command, CancellationToken cancellationToken) =>
        Ok(await notifications.SaveRouteAsync(await Context(cancellationToken), id, command, cancellationToken));
    [HttpDelete("routes/{id:guid}")]
    public async Task<IActionResult> DeleteRoute(Guid id, DeleteNotificationConfigurationCommand command, CancellationToken cancellationToken)
    { await notifications.DeleteRouteAsync(await Context(cancellationToken), id, command, cancellationToken); return NoContent(); }
    /// <summary>Bounded stable delivery history projection. No lease tokens, credentials, references or rendered messages.</summary>
    [HttpGet("deliveries")]
    public async Task<ActionResult<NotificationManagementPage<NotificationDeliveryDto>>> Deliveries([FromQuery] int page = 1, [FromQuery] int take = 25, [FromQuery] Guid? channelId = null,
        [FromQuery] string? state = null, CancellationToken cancellationToken = default) =>
        Ok(await notifications.ListDeliveriesAsync(await Context(cancellationToken), new(page, take), channelId, state, cancellationToken));

    private async Task<NotificationContext> Context(CancellationToken cancellationToken)
    {
        var user = await identity.ResolveCurrentAsync(User.RequireSubject(), User.Identity?.Name, User.FindAll("groups").Select(item => item.Value).ToArray(), cancellationToken);
        return new(user.Subject, user.IsAdministrator, HttpContext.TraceIdentifier, HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers["Idempotency-Key"].Count == 1 ? Request.Headers["Idempotency-Key"].ToString() : null);
    }

    /// <summary>At most five fixed result/timestamp records. No private transport payload or configuration revision.</summary>
    [HttpGet("deliveries/{id:guid}/attempts")]
    public async Task<ActionResult<IReadOnlyCollection<NotificationAttemptDto>>> Attempts(Guid id, CancellationToken cancellationToken) =>
        Ok(await notifications.AttemptsAsync(await Context(cancellationToken), id, cancellationToken));
}
