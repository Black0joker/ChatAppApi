using ChatApp.Api.Extensions;
using ChatApp.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChatApp.Api.Controllers;

[ApiController]
[Authorize]
public sealed class MessagesController(IMessageService messages) : ControllerBase
{
    public sealed record EditMessageBody(string Content);

    [HttpPut("api/messages/{id:guid}")]
    public async Task<ActionResult<MessageDto>> Edit(Guid id, [FromBody] EditMessageBody body, CancellationToken ct)
        => Ok(await messages.EditAsync(User.GetUserId(), id, new EditMessageRequest(body.Content), ct));

    [HttpDelete("api/messages/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await messages.DeleteAsync(User.GetUserId(), id, ct);
        return NoContent();
    }
}
