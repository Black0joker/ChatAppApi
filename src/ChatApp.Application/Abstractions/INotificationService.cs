using ChatApp.Application.Abstractions;

namespace ChatApp.Application.Abstractions;

public interface INotificationService
{
    /// <summary>Fan out a sent message: realtime event for online members, durable row + push for offline.</summary>
    Task NotifyMessageAsync(Guid messageId, Guid conversationId, Guid senderId, string contentPreview, CancellationToken ct = default);

    /// <summary>Notify a user added to a group (inviter excluded from notification).</summary>
    Task NotifyAddedToGroupAsync(Guid conversationId, string conversationName, Guid addedUserId, Guid addedByUserId, CancellationToken ct = default);

    Task<NotificationInboxDto> GetInboxAsync(Guid userId, int? limit, CancellationToken ct = default);
    Task MarkAsReadAsync(Guid userId, Guid notificationId, CancellationToken ct = default);
}
