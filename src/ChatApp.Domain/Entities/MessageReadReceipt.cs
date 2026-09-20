namespace ChatApp.Domain.Entities;

/// <summary>
/// Per-member read state (PLAN §17). One row per (message, reader); doubles as the
/// "delivered→read" source for 1:1 and per-member state for groups.
/// </summary>
public sealed class MessageReadReceipt
{
    public Guid MessageId { get; private set; }
    public Guid UserId { get; private set; }
    public DateTimeOffset ReadAt { get; private set; }

    public Message? Message { get; private set; }

    private MessageReadReceipt() { } // EF Core

    public MessageReadReceipt(Guid messageId, Guid userId, DateTimeOffset readAt)
    {
        MessageId = messageId;
        UserId = userId;
        ReadAt = readAt;
    }

    public void MarkRead(DateTimeOffset readAt) => ReadAt = readAt;
}
