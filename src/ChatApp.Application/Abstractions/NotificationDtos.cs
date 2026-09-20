using ChatApp.Domain.Enums;

namespace ChatApp.Application.Abstractions;

public sealed record NotificationDto(
    Guid Id,
    NotificationType Type,
    string Title,
    string Body,
    Guid? ConversationId,
    Guid? MessageId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt);

public sealed record NotificationInboxDto(IReadOnlyList<NotificationDto> Items, int UnreadCount);
