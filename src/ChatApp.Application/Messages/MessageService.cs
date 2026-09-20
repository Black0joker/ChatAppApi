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
    IReadReceiptRepository receipts,
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

        // Single batched receipt fetch for the whole page (no N+1).
        var readBy = (await receipts.GetForMessagesAsync(page.Select(m => m.Id), ct))
            .GroupBy(r => r.MessageId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<ReadReceiptDto>)g.Select(r => new ReadReceiptDto(r.MessageId, r.UserId, r.ReadAt)).ToList());

        return new MessageHistoryDto(
            page.Select(m => ToDto(m, readBy.GetValueOrDefault(m.Id))).ToList(),
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

    public async Task<MarkAsReadResult> MarkAsReadAsync(Guid userId, Guid conversationId, Guid messageId, CancellationToken ct = default)
    {
        var conversation = await RequireMemberAsync(userId, conversationId, ct);
        var message = await messages.GetByIdAsync(messageId, ct)
            ?? throw new NotFoundAppException("Message not found.");
        if (message.ConversationId != conversationId)
            throw new ValidationAppException("Message belongs to a different conversation.");

        var now = DateTimeOffset.UtcNow;

        if (message.SenderId == userId)
        {
            // Reading your own message is a no-op: no receipt row, no broadcast.
            // LastRead still advances (your own sent message is read by definition).
            await AdvanceLastReadAsync(conversation, userId, message, ct);
            await messages.SaveChangesAsync(ct);
            return new MarkAsReadResult(new ReadReceiptDto(message.Id, userId, now), false);
        }

        var existing = await receipts.GetAsync(message.Id, userId, ct);
        if (existing is null)
            await receipts.AddAsync(new MessageReadReceipt(message.Id, userId, now), ct);
        else
            existing.MarkRead(now);

        await AdvanceLastReadAsync(conversation, userId, message, ct);

        // Shared scoped DbContext: one SaveChanges persists receipt + LastRead atomically.
        await messages.SaveChangesAsync(ct);
        return new MarkAsReadResult(new ReadReceiptDto(message.Id, userId, now), true);
    }

    /// <summary>
    /// Monotonic LastRead: only moves forward in (CreatedAt, Id) order so a stale
    /// mark can never regress the member's read position.
    /// </summary>
    private async Task AdvanceLastReadAsync(Conversation conversation, Guid userId, Message message, CancellationToken ct)
    {
        var self = conversation.Members.First(m => m.UserId == userId && m.IsActive);
        if (self.LastReadMessageId is null || self.LastReadMessageId == message.Id)
        {
            self.SetLastRead(message.Id);
            return;
        }
        var current = await messages.GetByIdAsync(self.LastReadMessageId.Value, ct);
        if (current is null || current.ConversationId != conversation.Id || IsNewerThan(message, current))
            self.SetLastRead(message.Id);
    }

    private static bool IsNewerThan(Message a, Message b)
        => a.CreatedAt > b.CreatedAt || (a.CreatedAt == b.CreatedAt && a.Id.CompareTo(b.Id) > 0);

    private async Task<Conversation> RequireMemberAsync(Guid userId, Guid conversationId, CancellationToken ct)
    {
        var conversation = await conversations.GetByIdAsync(conversationId, ct)
            ?? throw new NotFoundAppException("Conversation not found.");
        if (conversation.Members.All(m => m.UserId != userId || !m.IsActive))
            throw new NotFoundAppException("Conversation not found.");
        return conversation;
    }

    private static MessageDto ToDto(Message m, IReadOnlyList<ReadReceiptDto>? readBy = null) => new(
        m.Id, m.ConversationId, m.SenderId,
        m.IsDeleted ? null : m.Content, // soft-delete hides content (PLAN: durable row, no leak)
        m.MessageType, m.CreatedAt, m.EditedAt, m.DeletedAt, m.ReplyToMessageId,
        readBy ?? []);
}
