using ChatApp.Api.Extensions;
using ChatApp.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChatApp.Api.Controllers;

[ApiController]
[Route("api/users")]
[Authorize]
public sealed class UsersController(IUserRepository users, IPresenceService presence) : ControllerBase
{
    [HttpGet("me")]
    public async Task<ActionResult<UserProfileDto>> Me(CancellationToken ct)
    {
        var id = User.GetUserId();
        var user = await users.GetByIdAsync(id, ct);
        if (user is null)
            return NotFound();
        return Ok(new UserProfileDto(user.Id, user.Username, user.DisplayName, user.Email,
            user.AvatarUrl, user.CreatedAt, user.LastSeenAt,
            await presence.IsOnlineAsync(id, ct)));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<UserProfileDto>> GetById(Guid id, CancellationToken ct)
    {
        var user = await users.GetByIdAsync(id, ct);
        if (user is null)
            return NotFound();
        // Don't leak email of other users in this minimal phase-2 shape.
        var email = user.Id == User.GetUserId() ? user.Email : string.Empty;
        return Ok(new UserProfileDto(user.Id, user.Username, user.DisplayName, email,
            user.AvatarUrl, user.CreatedAt, user.LastSeenAt,
            await presence.IsOnlineAsync(id, ct)));
    }
}
