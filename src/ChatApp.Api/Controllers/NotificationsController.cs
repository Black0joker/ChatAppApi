using ChatApp.Api.Extensions;
using ChatApp.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChatApp.Api.Controllers;

[ApiController]
[Route("api/notifications")]
[Authorize]
public sealed class NotificationsController(INotificationService notifications) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<NotificationInboxDto>> GetInbox([FromQuery] int? limit, CancellationToken ct)
        => Ok(await notifications.GetInboxAsync(User.GetUserId(), limit, ct));

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkAsRead(Guid id, CancellationToken ct)
    {
        await notifications.MarkAsReadAsync(User.GetUserId(), id, ct);
        return NoContent();
    }
}
