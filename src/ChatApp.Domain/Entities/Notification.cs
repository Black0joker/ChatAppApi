using ChatApp.Domain.Enums;

namespace ChatApp.Domain.Entities;

/// <summary>
/// Durable inbox entry (PLAN §19). Created for members who missed an event
/// (offline at the time); online members get the real-time event instead.
/// ReadAt null = unread.
/// </summary>
public sealed class Notification
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid UserId { get; private set; }
    public NotificationType Type { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Body { get; private set; } = string.Empty;
    public Guid? ConversationId { get; private set; }
    public Guid? MessageId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReadAt { get; private set; }

    private Notification() { } // EF Core

    public Notification(Guid userId, NotificationType type, string title, string body, Guid? conversationId = null, Guid? messageId = null)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        Type = type;
        Title = title;
        Body = body;
        ConversationId = conversationId;
        MessageId = messageId;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public bool IsRead => ReadAt.HasValue;

    public void MarkAsRead() => ReadAt ??= DateTimeOffset.UtcNow;
}
