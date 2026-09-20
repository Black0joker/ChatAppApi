using ChatApp.Domain.Enums;

namespace ChatApp.Domain.Entities;

public sealed class Conversation
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public ConversationType Type { get; private set; }
    public string? Name { get; private set; }
    public Guid CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public Guid? LastMessageId { get; private set; }

    private readonly List<ConversationMember> _members = [];
    public IReadOnlyCollection<ConversationMember> Members => _members.AsReadOnly();

    private Conversation() { } // EF Core

    public Conversation(ConversationType type, string? name, Guid createdBy)
    {
        Id = Guid.NewGuid();
        Type = type;
        CreatedBy = createdBy;
        CreatedAt = DateTimeOffset.UtcNow;
        SetName(name);
    }

    public void SetName(string? name)
    {
        if (Type == ConversationType.Group)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100)
                throw new ArgumentException("Group name must be between 1 and 100 characters.", nameof(name));
            Name = name.Trim();
        }
        else
        {
            Name = name?.Trim();
        }
    }

    public ConversationMember AddMember(Guid userId, MemberRole role)
    {
        var member = new ConversationMember(Id, userId, role);
        _members.Add(member);
        return member;
    }

    public void SetLastMessage(Guid messageId) => LastMessageId = messageId;
}
