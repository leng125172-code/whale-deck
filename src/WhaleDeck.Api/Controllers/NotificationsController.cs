using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhaleDeck.Api.Security;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Api.Controllers;

[ApiController]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/v1/notifications")]
public sealed class NotificationsController(NotificationService notifications, IIdentityDirectory identity) : ControllerBase
{
    /// <summary>Read personal messages and, only with current administrator permission, administrator broadcasts.</summary>
    [HttpGet]
    public async Task<ActionResult<NotificationPageDto>> List([FromQuery] int take = 50, [FromQuery] long? before = null,
        [FromQuery] string status = "all", [FromQuery] string kind = "all", CancellationToken cancellationToken = default) =>
        Ok(await notifications.ListAsync(await Context(cancellationToken), new(take, before, status, kind), cancellationToken));

    /// <summary>Personal unread count; reading does not mark messages read.</summary>
    [HttpGet("summary")]
    public async Task<ActionResult<NotificationSummaryDto>> Summary(CancellationToken cancellationToken) =>
        Ok(await notifications.SummaryAsync(await Context(cancellationToken), cancellationToken));

    /// <summary>Idempotently set only the caller's read state. Invisible or expired IDs return 404.</summary>
    [HttpPost("{id:guid}/read-state")]
    public async Task<ActionResult<NotificationReadDto>> SetRead(Guid id, SetNotificationReadCommand command, CancellationToken cancellationToken) =>
        Ok(await notifications.SetReadAsync(await Context(cancellationToken), id, command.IsRead, cancellationToken));

    /// <summary>Mark only visible messages through a previously returned snapshot sequence; newer messages stay unread.</summary>
    [HttpPost("read-all")]
    public async Task<ActionResult<MarkNotificationsReadDto>> MarkAll(MarkNotificationsReadCommand command, CancellationToken cancellationToken) =>
        Ok(await notifications.MarkAllReadAsync(await Context(cancellationToken), command.ThroughSequence, cancellationToken));

    private async Task<NotificationContext> Context(CancellationToken cancellationToken)
    {
        var user = await identity.ResolveCurrentAsync(User.RequireSubject(), User.Identity?.Name,
            User.FindAll("groups").Select(item => item.Value).ToArray(), cancellationToken);
        return new(user.Subject, user.IsAdministrator, HttpContext.TraceIdentifier, HttpContext.Connection.RemoteIpAddress?.ToString());
    }
}
