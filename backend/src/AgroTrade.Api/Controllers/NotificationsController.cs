using AgroTrade.Application.Common;
using AgroTrade.Application.Communication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgroTrade.Api.Controllers;

/// <summary>
/// In-app notifications for the current user (plan §03 — Notifications).
/// </summary>
[ApiController]
[Route("api/v1/me/notifications")]
[Authorize]
public class NotificationsController(ICommunicationService communicationService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<NotificationDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<NotificationDto>>> Get(
        [FromQuery] bool? unreadOnly, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        return Ok(await communicationService.GetNotificationsAsync(User.GetUserId(), unreadOnly, page, pageSize, ct));
    }

    [HttpGet("unread-count")]
    [ProducesResponseType(typeof(int), StatusCodes.Status200OK)]
    public async Task<ActionResult<int>> UnreadCount(CancellationToken ct)
    {
        return Ok(await communicationService.GetUnreadNotificationCountAsync(User.GetUserId(), ct));
    }

    [HttpPatch("{id:guid}/read")]
    [ProducesResponseType(typeof(NotificationDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<NotificationDto>> MarkRead(Guid id, CancellationToken ct)
    {
        return Ok(await communicationService.MarkNotificationReadAsync(User.GetUserId(), id, ct));
    }

    [HttpPost("read-all")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
    {
        await communicationService.MarkAllNotificationsReadAsync(User.GetUserId(), ct);
        return NoContent();
    }
}