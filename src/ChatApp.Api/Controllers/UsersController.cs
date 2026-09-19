using ChatApp.Api.Extensions;
using ChatApp.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChatApp.Api.Controllers;

[ApiController]
[Route("api/users")]
[Authorize]
public sealed class UsersController(IUserRepository users) : ControllerBase
{
    [HttpGet("me")]
    public async Task<ActionResult<UserDto>> Me(CancellationToken ct)
    {
        var id = User.GetUserId();
        var user = await users.GetByIdAsync(id, ct);
        if (user is null)
            return NotFound();
        return Ok(new UserDto(user.Id, user.Username, user.DisplayName, user.Email, user.AvatarUrl, user.CreatedAt));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<UserDto>> GetById(Guid id, CancellationToken ct)
    {
        var user = await users.GetByIdAsync(id, ct);
        if (user is null)
            return NotFound();
        // Don't leak email of other users in this minimal phase-2 shape.
        var email = user.Id == User.GetUserId() ? user.Email : string.Empty;
        return Ok(new UserDto(user.Id, user.Username, user.DisplayName, email, user.AvatarUrl, user.CreatedAt));
    }
}
