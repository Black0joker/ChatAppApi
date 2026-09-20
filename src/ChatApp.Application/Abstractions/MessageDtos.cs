using ChatApp.Domain.Enums;

namespace ChatApp.Application.Abstractions;

public sealed record SendMessageRequest(
    string Content,
    MessageType? MessageType = null,
    Guid? ReplyToMessageId = null,
    IReadOnlyList<Guid>? AttachmentIds = null,
    Guid? ClientMessageId = null);
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
    IReadOnlyList<ReadReceiptDto> ReadBy,
    // Attachments are populated on send + history reads; edit/delete carry empty.
    IReadOnlyList<MessageAttachmentDto> Attachments);

public sealed record ReadReceiptDto(Guid MessageId, Guid UserId, DateTimeOffset ReadAt);

public sealed record MarkAsReadResult(ReadReceiptDto Receipt, bool IsNewActivity);

public sealed record MessageAttachmentDto(Guid Id, string FileName, string ContentType, long Size, DateTimeOffset CreatedAt)
{
    public static MessageAttachmentDto From(Domain.Entities.MessageAttachment a)
        => new(a.Id, a.FileName, a.ContentType, a.Size, a.CreatedAt);
}

public sealed record MessageHistoryDto(IReadOnlyList<MessageDto> Items, bool HasMore, Guid? NextCursor);
