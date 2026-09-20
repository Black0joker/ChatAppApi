using ChatApp.Application.Abstractions;
using ChatApp.Application.Common.Exceptions;
using ChatApp.Domain.Entities;
using ChatApp.Domain.Enums;

namespace ChatApp.Application.Notifications;

/// <summary>
/// Separates real-time delivery from durable notifications (PLAN §19):
/// online members get a live event, offline members get a durable inbox row
/// plus a push attempt. Best-effort by design — guaranteed delivery via the
/// Outbox Pattern arrives in Phase 12.
/// </summary>
public sealed class NotificationService(
    INotificationRepository notifications,
    IConversationRepository conversations,
    IUserRepository users,
    IPresenceService presence,
    INotificationPublisher publisher,
    IPushNotificationService push) : INotificationService
{
    private const int PreviewLength = 120;
    private const int DefaultLimit = 50;
    private const int MaxLimit = 100;

    // Keep in sync with ChatHubEvents.NotificationReceived (Api layer owns the wire name).
    private const string NotificationReceivedEvent = "NotificationReceived";

    public async Task NotifyMessageAsync(Guid messageId, Guid conversationId, Guid senderId, string contentPreview, CancellationToken ct = default)
    {
        var conversation = await conversations.GetByIdAsync(conversationId, ct);
        if (conversation is null)
            return; // conversation vanished mid-send; nothing to notify
        var sender = await users.GetByIdAsync(senderId, ct);
        var title = sender?.DisplayName ?? "New message";
        var body = Preview(contentPreview);
        var convName = conversation.Type == ConversationType.Group ? conversation.Name : null;

        foreach (var member in conversation.Members.Where(m => m.IsActive && m.UserId != senderId))
        {
            var payload = new
            {
                userId = member.UserId,
                type = NotificationType.Message.ToString(),
                title,
                body,
                conversationId,
                conversationName = convName,
                messageId
            };

            if (await presence.IsOnlineAsync(member.UserId, ct))
            {
                await publisher.PublishToUserAsync(member.UserId, NotificationReceivedEvent, payload, ct);
            }
            else
            {
                await notifications.AddAsync(new Notification(
                    member.UserId, NotificationType.Message, title, body, conversationId, messageId), ct);
                await push.PushAsync(member.UserId, title, body,
                    new Dictionary<string, string>
                    {
                        ["conversationId"] = conversationId.ToString(),
                        ["messageId"] = messageId.ToString()
                    }, ct);
            }
        }
        await notifications.SaveChangesAsync(ct);
    }

    public async Task NotifyAddedToGroupAsync(Guid conversationId, string conversationName, Guid addedUserId, Guid addedByUserId, CancellationToken ct = default)
    {
        var inviter = await users.GetByIdAsync(addedByUserId, ct);
        var title = "Added to group";
        var body = $"{inviter?.DisplayName ?? "Someone"} added you to {conversationName}";
        var payload = new
        {
            userId = addedUserId,
            type = NotificationType.AddedToGroup.ToString(),
            title,
            body,
            conversationId,
            conversationName,
            messageId = (Guid?)null
        };

        if (await presence.IsOnlineAsync(addedUserId, ct))
        {
            await publisher.PublishToUserAsync(addedUserId, NotificationReceivedEvent, payload, ct);
        }
        else
        {
            await notifications.AddAsync(new Notification(
                addedUserId, NotificationType.AddedToGroup, title, body, conversationId), ct);
            await push.PushAsync(addedUserId, title, body,
                new Dictionary<string, string> { ["conversationId"] = conversationId.ToString() }, ct);
            await notifications.SaveChangesAsync(ct);
        }
    }

    public async Task<NotificationInboxDto> GetInboxAsync(Guid userId, int? limit, CancellationToken ct = default)
    {
        var take = limit is null or <= 0 ? DefaultLimit : Math.Min(limit.Value, MaxLimit);
        var rows = await notifications.GetForUserAsync(userId, take, ct);
        return new NotificationInboxDto(
            rows.Select(n => new NotificationDto(n.Id, n.Type, n.Title, n.Body,
                n.ConversationId, n.MessageId, n.CreatedAt, n.ReadAt)).ToList(),
            await notifications.CountUnreadAsync(userId, ct));
    }

    public async Task MarkAsReadAsync(Guid userId, Guid notificationId, CancellationToken ct = default)
    {
        // Own inbox only: others' ids resolve to 404 (no existence probing).
        var notification = await notifications.GetByIdAsync(notificationId, ct);
        if (notification is null || notification.UserId != userId)
            throw new NotFoundAppException("Notification not found.");
        notification.MarkAsRead();
        await notifications.SaveChangesAsync(ct);
    }

    private static string Preview(string content)
    {
        var trimmed = (content ?? string.Empty).Trim();
        return trimmed.Length <= PreviewLength ? trimmed : trimmed[..PreviewLength] + "…";
    }
}
