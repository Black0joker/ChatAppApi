using ChatApp.Domain.Enums;

namespace ChatApp.Domain.Entities;

public sealed class Message
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid ConversationId { get; private set; }
    public Guid SenderId { get; private set; }
    public string Content { get; private set; } = string.Empty;
    public MessageType MessageType { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? EditedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public Guid? ReplyToMessageId { get; private set; }

    public Conversation? Conversation { get; private set; }

    private Message() { } // EF Core

    public Message(Guid conversationId, Guid senderId, string content, MessageType messageType, Guid? replyToMessageId = null)
    {
        // Server is the authority for identity and time (PLAN §23): GUID + server timestamp.
        Id = Guid.NewGuid();
        ConversationId = conversationId;
        SenderId = senderId;
        Content = content;
        MessageType = messageType;
        ReplyToMessageId = replyToMessageId;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public bool IsDeleted => DeletedAt.HasValue;

    public void Edit(string content)
    {
        if (IsDeleted)
            throw new InvalidOperationException("A deleted message cannot be edited.");
        Content = content;
        EditedAt = DateTimeOffset.UtcNow;
    }

    public void Delete() => DeletedAt ??= DateTimeOffset.UtcNow;
}
