using ChatApp.Api.Extensions;
using ChatApp.Application.Abstractions;
using ChatApp.Application.Common.Exceptions;
using ChatApp.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ChatApp.Api.Hubs;

/// <summary>Server-to-client event names (PLAN §28). Keep versionable: additive only.</summary>
public static class ChatHubEvents
{
    public const string MessageReceived = "MessageReceived";
    public const string MessageUpdated = "MessageUpdated";
    public const string MessageDeleted = "MessageDeleted";
    public const string MessageRead = "MessageRead";
    public const string UserOnline = "UserOnline";
    public const string UserOffline = "UserOffline";
    public const string TypingStarted = "TypingStarted";
    public const string TypingStopped = "TypingStopped";
}

public sealed record SendMessagePayload(Guid ConversationId, string Content, MessageType? MessageType = null, Guid? ReplyToMessageId = null, IReadOnlyList<Guid>? AttachmentIds = null);

/// <summary>
/// Phase 5: thin real-time facade (PLAN §9). The hub authenticates, authorizes
/// group access, delegates persistence to application services, then broadcasts.
/// It never trusts client-provided user ids and never contains business logic.
/// </summary>
[Authorize]
public sealed class ChatHub(
    IMessageService messages,
    IConversationService conversations,
    IPresenceService presence,
    ILogger<ChatHub> logger) : Hub
{
    public static string GroupName(Guid conversationId) => $"conversation:{conversationId}";

    public override async Task OnConnectedAsync()
    {
        var userId = TryGetUserId();
        logger.LogInformation("SignalR connected. ConnectionId={ConnectionId} UserId={UserId}",
            Context.ConnectionId, userId);

        // Distributed presence (PLAN §14). Presence must never break the connection itself.
        if (userId.HasValue)
        {
            try { await presence.UserConnectedAsync(userId.Value, Context.ConnectionId); }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Presence tracking failed on connect. UserId={UserId}", userId);
            }
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = TryGetUserId();
        logger.LogInformation("SignalR disconnected. ConnectionId={ConnectionId} Error={Error}",
            Context.ConnectionId, exception?.Message);

        if (userId.HasValue)
        {
            try { await presence.UserDisconnectedAsync(userId.Value, Context.ConnectionId); }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Presence tracking failed on disconnect. UserId={UserId}", userId);
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    private Guid? TryGetUserId()
    {
        try { return Context.User!.GetUserId(); }
        catch { return null; }
    }

    /// <summary>
    /// Open a conversation channel. Membership is verified first (PLAN §10):
    /// arbitrary clients can never join arbitrary groups.
    /// </summary>
    public async Task JoinConversation(Guid conversationId)
    {
        var userId = Context.User!.GetUserId();
        try
        {
            await conversations.GetByIdAsync(userId, conversationId);
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(conversationId));
            logger.LogInformation("User {UserId} joined conversation {ConversationId}", userId, conversationId);
        }
        catch (Exception ex)
        {
            throw ToHubError(ex, userId, conversationId);
        }
    }

    public async Task LeaveConversation(Guid conversationId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(conversationId));
        logger.LogInformation("User {UserId} left conversation {ConversationId}",
            Context.User?.GetUserId(), conversationId);
    }

    /// <summary>
    /// Ephemeral typing indicators (PLAN §15): broadcast to the conversation group,
    /// excluding the sender. Membership is verified; nothing is written to SQL or
    /// Redis. Delivery across instances rides the SignalR backplane's group routing.
    /// Clients must clear stale indicators locally (e.g. no refresh within ~5s),
    /// since a crashed client never sends TypingStopped.
    /// </summary>
    public Task TypingStarted(Guid conversationId)
        => SendTypingAsync(ChatHubEvents.TypingStarted, conversationId);

    public Task TypingStopped(Guid conversationId)
        => SendTypingAsync(ChatHubEvents.TypingStopped, conversationId);

    private async Task SendTypingAsync(string @event, Guid conversationId)
    {
        var userId = Context.User!.GetUserId();
        try
        {
            await conversations.GetByIdAsync(userId, conversationId);
            await Clients.OthersInGroup(GroupName(conversationId)).SendAsync(@event, new
            {
                conversationId,
                userId
            });
        }
        catch (Exception ex)
        {
            throw ToHubError(ex, userId, conversationId);
        }
    }

    /// <summary>
    /// Persist-then-broadcast (PLAN §16): the message is only broadcast after
    /// the database transaction succeeds. The sender always comes from the token.
    /// </summary>
    public async Task<MessageDto> SendMessage(SendMessagePayload payload)
    {
        var userId = Context.User!.GetUserId();
        try
        {
            var message = await messages.SendAsync(userId, payload.ConversationId,
                new SendMessageRequest(payload.Content, payload.MessageType, payload.ReplyToMessageId, payload.AttachmentIds));

            await Clients.Group(GroupName(payload.ConversationId)).SendAsync(ChatHubEvents.MessageReceived, new
            {
                messageId = message.Id,
                conversationId = message.ConversationId,
                senderId = message.SenderId,
                content = message.Content,
                messageType = message.MessageType,
                createdAt = message.CreatedAt,
                replyToMessageId = message.ReplyToMessageId
            });

            return message;
        }
        catch (Exception ex)
        {
            throw ToHubError(ex, userId, payload.ConversationId);
        }
    }

    public async Task<MessageDto> EditMessage(Guid messageId, string content)
    {
        var userId = Context.User!.GetUserId();
        try
        {
            var message = await messages.EditAsync(userId, messageId, new EditMessageRequest(content));

            await Clients.Group(GroupName(message.ConversationId)).SendAsync(ChatHubEvents.MessageUpdated, new
            {
                messageId = message.Id,
                conversationId = message.ConversationId,
                content = message.Content,
                editedAt = message.EditedAt
            });

            return message;
        }
        catch (Exception ex)
        {
            throw ToHubError(ex, userId, null);
        }
    }

    public async Task<MessageDto> DeleteMessage(Guid messageId)
    {
        var userId = Context.User!.GetUserId();
        try
        {
            var message = await messages.DeleteAsync(userId, messageId);

            await Clients.Group(GroupName(message.ConversationId)).SendAsync(ChatHubEvents.MessageDeleted, new
            {
                messageId = message.Id,
                conversationId = message.ConversationId,
                deletedAt = message.DeletedAt
            });

            return message;
        }
        catch (Exception ex)
        {
            throw ToHubError(ex, userId, null);
        }
    }

    /// <summary>
    /// Record a read and notify the conversation (PLAN §17). Broadcasts only on
    /// new activity — self-marks are acknowledged without an event.
    /// </summary>
    public async Task<ReadReceiptDto> MarkAsRead(Guid conversationId, Guid messageId)
    {
        var userId = Context.User!.GetUserId();
        try
        {
            var result = await messages.MarkAsReadAsync(userId, conversationId, messageId);

            if (result.IsNewActivity)
            {
                await Clients.Group(GroupName(conversationId)).SendAsync(ChatHubEvents.MessageRead, new
                {
                    conversationId,
                    messageId = result.Receipt.MessageId,
                    userId = result.Receipt.UserId,
                    readAt = result.Receipt.ReadAt
                });
            }

            return result.Receipt;
        }
        catch (Exception ex)
        {
            throw ToHubError(ex, userId, conversationId);
        }
    }

    /// <summary>Health-check round trip for authenticated clients.</summary>
    public Task<string> Ping()
    {
        var username = Context.User?.Identity?.Name ?? "unknown";
        return Task.FromResult($"pong:{username}:{Context.ConnectionId}");
    }

    /// <summary>
    /// Client heartbeat (PLAN §14): refresh the connection TTL so the sweeper
    /// doesn't evict live connections. Call every ~HeartbeatIntervalSeconds.
    /// Returns true when the connection is tracked (false = anonymous or failed).
    /// </summary>
    public async Task<bool> Heartbeat()
    {
        var userId = TryGetUserId();
        if (!userId.HasValue)
            return false;
        try
        {
            await presence.HeartbeatAsync(userId.Value, Context.ConnectionId);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Heartbeat failed. UserId={UserId}", userId);
            return false;
        }
    }

    /// <summary>Returns the server-derived identity — proves userId comes from the token.</summary>
    public Task<object> WhoAmI()
    {
        var user = Context.User!;
        return Task.FromResult<object>(new
        {
            userId = user.GetUserId(),
            username = user.Identity?.Name
        });
    }

    // PLAN §29: consistent, non-leaking errors. Domain failures surface their safe
    // message; anything unexpected becomes generic (no SQL/stack/Redis internals).
    private HubException ToHubError(Exception ex, Guid userId, Guid? conversationId)
    {
        if (ex is HubException)
            return (HubException)ex;
        if (ex is AppException app)
            return new HubException(app.Message);
        logger.LogError(ex, "SignalR failure. UserId={UserId} ConversationId={ConversationId}", userId, conversationId);
        return new HubException("An unexpected error occurred.");
    }
}
