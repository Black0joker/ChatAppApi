using ChatApp.Domain.Enums;

namespace ChatApp.Application.Abstractions;

public sealed record SendMessageRequest(string Content, MessageType? MessageType = null, Guid? ReplyToMessageId = null);
public sealed record EditMessageRequest(string Content);

public sealed record MessageDto(
    Guid Id,
    Guid ConversationId,
    Guid SenderId,
    string? Content, // null when soft-deleted
    MessageType MessageType,
    DateTimeOffset CreatedAt,
    DateTimeOffset? EditedAt,
    DateTimeOffset? DeletedAt,
    Guid? ReplyToMessageId,
    // Read state is populated on history reads only (single batched query);
    // send/edit/delete responses carry an empty list.
    IReadOnlyList<ReadReceiptDto> ReadBy);

public sealed record ReadReceiptDto(Guid MessageId, Guid UserId, DateTimeOffset ReadAt);

public sealed record MarkAsReadResult(ReadReceiptDto Receipt, bool IsNewActivity);

public sealed record MessageHistoryDto(IReadOnlyList<MessageDto> Items, bool HasMore, Guid? NextCursor);
