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
}

public sealed record SendMessagePayload(Guid ConversationId, string Content, MessageType? MessageType = null, Guid? ReplyToMessageId = null);

/// <summary>
/// Phase 5: thin real-time facade (PLAN §9). The hub authenticates, authorizes
/// group access, delegates persistence to application services, then broadcasts.
/// It never trusts client-provided user ids and never contains business logic.
/// </summary>
[Authorize]
public sealed class ChatHub(
    IMessageService messages,
    IConversationService conversations,
    ILogger<ChatHub> logger) : Hub
{
    public static string GroupName(Guid conversationId) => $"conversation:{conversationId}";

    public override Task OnConnectedAsync()
    {
        logger.LogInformation("SignalR connected. ConnectionId={ConnectionId} UserId={UserId}",
            Context.ConnectionId, Context.User?.GetUserId());
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        logger.LogInformation("SignalR disconnected. ConnectionId={ConnectionId} Error={Error}",
            Context.ConnectionId, exception?.Message);
        return base.OnDisconnectedAsync(exception);
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
    /// Persist-then-broadcast (PLAN §16): the message is only broadcast after
    /// the database transaction succeeds. The sender always comes from the token.
    /// </summary>
    public async Task<MessageDto> SendMessage(SendMessagePayload payload)
    {
        var userId = Context.User!.GetUserId();
        try
        {
            var message = await messages.SendAsync(userId, payload.ConversationId,
                new SendMessageRequest(payload.Content, payload.MessageType, payload.ReplyToMessageId));

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

    /// <summary>Health-check round trip for authenticated clients.</summary>
    public Task<string> Ping()
    {
        var username = Context.User?.Identity?.Name ?? "unknown";
        return Task.FromResult($"pong:{username}:{Context.ConnectionId}");
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
