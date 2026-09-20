using ChatApp.Api.Extensions;
using ChatApp.Application.Abstractions;
using ChatApp.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChatApp.Api.Controllers;

[ApiController]
[Route("api/conversations")]
[Authorize]
public sealed class ConversationsController(
    IConversationService conversations,
    IMessageService messages) : ControllerBase
{
    public sealed record CreateDirectBody(Guid OtherUserId);
    public sealed record CreateGroupBody(string Name, List<Guid> MemberIds);
    public sealed record AddMemberBody(Guid UserId, MemberRole? Role);
    public sealed record SendMessageBody(string Content, MessageType? MessageType, Guid? ReplyToMessageId, List<Guid>? AttachmentIds);

    [HttpPost("direct")]
    public async Task<ActionResult<ConversationDto>> CreateDirect([FromBody] CreateDirectBody body, CancellationToken ct)
    {
        var result = await conversations.CreateDirectAsync(User.GetUserId(), new CreateDirectRequest(body.OtherUserId), ct);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPost("groups")]
    public async Task<ActionResult<ConversationDto>> CreateGroup([FromBody] CreateGroupBody body, CancellationToken ct)
    {
        var result = await conversations.CreateGroupAsync(
            User.GetUserId(), new CreateGroupRequest(body.Name, body.MemberIds ?? []), ct);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ConversationDto>>> GetMine(CancellationToken ct)
        => Ok(await conversations.GetMineAsync(User.GetUserId(), ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ConversationDto>> GetById(Guid id, CancellationToken ct)
        => Ok(await conversations.GetByIdAsync(User.GetUserId(), id, ct));

    [HttpPost("{id:guid}/members")]
    public async Task<ActionResult<ConversationDto>> AddMember(Guid id, [FromBody] AddMemberBody body, CancellationToken ct)
        => Ok(await conversations.AddMemberAsync(User.GetUserId(), id, new AddMemberRequest(body.UserId, body.Role), ct));

    [HttpDelete("{id:guid}/members/{userId:guid}")]
    public async Task<IActionResult> RemoveMember(Guid id, Guid userId, CancellationToken ct)
    {
        await conversations.RemoveMemberAsync(User.GetUserId(), id, userId, ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/messages")]
    public async Task<ActionResult<MessageHistoryDto>> GetHistory(
        Guid id, [FromQuery] Guid? before, [FromQuery] Guid? after, [FromQuery] int? limit, CancellationToken ct)
        => Ok(await messages.GetHistoryAsync(User.GetUserId(), id, before, after, limit, ct));

    [HttpPost("{id:guid}/messages")]
    public async Task<ActionResult<MessageDto>> SendMessage(Guid id, [FromBody] SendMessageBody body, CancellationToken ct)
    {
        var result = await messages.SendAsync(
            User.GetUserId(), id, new SendMessageRequest(body.Content, body.MessageType, body.ReplyToMessageId, body.AttachmentIds), ct);
        return CreatedAtAction(nameof(GetHistory), new { id }, result);
    }

    [HttpPost("{id:guid}/messages/{messageId:guid}/read")]
    public async Task<ActionResult<ReadReceiptDto>> MarkAsRead(Guid id, Guid messageId, CancellationToken ct)
    {
        var result = await messages.MarkAsReadAsync(User.GetUserId(), id, messageId, ct);
        return Ok(result.Receipt);
    }
}
