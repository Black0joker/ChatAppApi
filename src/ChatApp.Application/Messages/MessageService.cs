using ChatApp.Application.Abstractions;
using ChatApp.Application.Common;
using ChatApp.Application.Common.Exceptions;
using ChatApp.Domain.Entities;
using ChatApp.Domain.Enums;
using Microsoft.Extensions.Options;

namespace ChatApp.Application.Messages;

public sealed class MessageService(
    IMessageRepository messages,
    IConversationRepository conversations,
    IOptions<ChatOptions> chatOptions) : IMessageService
{
    private readonly ChatOptions _chat = chatOptions.Value;

    public async Task<MessageDto> SendAsync(Guid userId, Guid conversationId, SendMessageRequest request, CancellationToken ct = default)
    {
        var conversation = await RequireMemberAsync(userId, conversationId, ct);

        var content = request.Content?.Trim() ?? string.Empty;
        if (content.Length == 0)
            throw new ValidationAppException("Message content must not be empty.");
        if (content.Length > _chat.MaxMessageLength)
            throw new ValidationAppException($"Message cannot exceed {_chat.MaxMessageLength} characters.");

        var type = request.MessageType ?? MessageType.Text;
        if (type == MessageType.System)
            throw new ValidationAppException("System messages cannot be sent by clients.");

        Guid? replyTo = null;
        if (request.ReplyToMessageId.HasValue)
        {
            var target = await messages.GetByIdAsync(request.ReplyToMessageId.Value, ct)
                ?? throw new NotFoundAppException("Replied-to message not found.");
            if (target.ConversationId != conversationId || target.IsDeleted)
                throw new ValidationAppException("Can only reply to an existing message in the same conversation.");
            replyTo = target.Id;
        }

        var message = new Message(conversationId, userId, content, type, replyTo);

        // Transaction boundary (PLAN §38): message + LastMessageId persist atomically.
        // Both repositories share the scoped DbContext, so one SaveChanges = one transaction.
        await messages.AddAsync(message, ct);
        conversation.SetLastMessage(message.Id);
        await messages.SaveChangesAsync(ct);

        return ToDto(message);
    }

    public async Task<MessageHistoryDto> GetHistoryAsync(Guid userId, Guid conversationId, Guid? before, Guid? after, int? limit, CancellationToken ct = default)
    {
        await RequireMemberAsync(userId, conversationId, ct);

        if (before.HasValue && after.HasValue)
            throw new ValidationAppException("Only one of 'before' or 'after' may be specified.");

        var take = limit is null or <= 0 ? _chat.DefaultPageSize : Math.Min(limit.Value, _chat.MaxPageSize);

        DateTimeOffset? beforeAt = null; Guid? beforeId = null;
        DateTimeOffset? afterAt = null; Guid? afterId = null;

        if (before.HasValue)
        {
            var cursor = await messages.GetByIdAsync(before.Value, ct)
                ?? throw new NotFoundAppException("Cursor message not found.");
            if (cursor.ConversationId != conversationId)
                throw new ValidationAppException("Cursor belongs to a different conversation.");
            beforeAt = cursor.CreatedAt; beforeId = cursor.Id;
        }
        if (after.HasValue)
        {
            var cursor = await messages.GetByIdAsync(after.Value, ct)
                ?? throw new NotFoundAppException("Cursor message not found.");
            if (cursor.ConversationId != conversationId)
                throw new ValidationAppException("Cursor belongs to a different conversation.");
            afterAt = cursor.CreatedAt; afterId = cursor.Id;
        }

        var rows = await messages.GetHistoryAsync(conversationId, beforeAt, beforeId, afterAt, afterId, take, ct);
        var hasMore = rows.Count > take;
        var page = rows.Take(take).ToList();
        return new MessageHistoryDto(
            page.Select(ToDto).ToList(),
            hasMore,
            page.Count == 0 ? null : page[^1].Id);
    }

    public async Task<MessageDto> EditAsync(Guid userId, Guid messageId, EditMessageRequest request, CancellationToken ct = default)
    {
        var message = await messages.GetByIdAsync(messageId, ct)
            ?? throw new NotFoundAppException("Message not found.");
        await RequireMemberAsync(userId, message.ConversationId, ct);

        if (message.SenderId != userId)
            throw new ForbiddenAppException("Only the sender can edit a message.");
        if (message.IsDeleted)
            throw new ValidationAppException("A deleted message cannot be edited.");

        var content = request.Content?.Trim() ?? string.Empty;
        if (content.Length == 0)
            throw new ValidationAppException("Message content must not be empty.");
        if (content.Length > _chat.MaxMessageLength)
            throw new ValidationAppException($"Message cannot exceed {_chat.MaxMessageLength} characters.");

        message.Edit(content);
        await messages.SaveChangesAsync(ct);
        return ToDto(message);
    }

    public async Task<MessageDto> DeleteAsync(Guid userId, Guid messageId, CancellationToken ct = default)
    {
        var message = await messages.GetByIdAsync(messageId, ct)
            ?? throw new NotFoundAppException("Message not found.");
        var conversation = await RequireMemberAsync(userId, message.ConversationId, ct);

        var self = conversation.Members.First(m => m.UserId == userId && m.IsActive);
        var canDelete = message.SenderId == userId
            || (conversation.Type == ConversationType.Group
                && self.Role is MemberRole.Owner or MemberRole.Admin);

        if (!canDelete)
            throw new ForbiddenAppException("Only the sender or a group owner/admin can delete a message.");

        message.Delete();
        await messages.SaveChangesAsync(ct);
        return ToDto(message);
    }

    private async Task<Conversation> RequireMemberAsync(Guid userId, Guid conversationId, CancellationToken ct)
    {
        var conversation = await conversations.GetByIdAsync(conversationId, ct)
            ?? throw new NotFoundAppException("Conversation not found.");
        if (conversation.Members.All(m => m.UserId != userId || !m.IsActive))
            throw new NotFoundAppException("Conversation not found.");
        return conversation;
    }

    private static MessageDto ToDto(Message m) => new(
        m.Id, m.ConversationId, m.SenderId,
        m.IsDeleted ? null : m.Content, // soft-delete hides content (PLAN: durable row, no leak)
        m.MessageType, m.CreatedAt, m.EditedAt, m.DeletedAt, m.ReplyToMessageId);
}
