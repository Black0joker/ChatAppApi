using ChatApp.Domain.Enums;

namespace ChatApp.Domain.Entities;

public sealed class ConversationMember
{
    public Guid ConversationId { get; private set; }
    public Guid UserId { get; private set; }
    public MemberRole Role { get; private set; }
    public DateTimeOffset JoinedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LeftAt { get; private set; }
    public Guid? LastReadMessageId { get; private set; }

    public Conversation? Conversation { get; private set; }

    private ConversationMember() { } // EF Core

    public ConversationMember(Guid conversationId, Guid userId, MemberRole role)
    {
        ConversationId = conversationId;
        UserId = userId;
        Role = role;
        JoinedAt = DateTimeOffset.UtcNow;
    }

    public bool IsActive => LeftAt is null;

    public void Leave() => LeftAt = DateTimeOffset.UtcNow;

    public void Rejoin(MemberRole role)
    {
        LeftAt = null;
        Role = role;
        JoinedAt = DateTimeOffset.UtcNow;
    }

    public void SetRole(MemberRole role) => Role = role;

    public void SetLastRead(Guid messageId) => LastReadMessageId = messageId;
}
