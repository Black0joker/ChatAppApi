using ChatApp.Domain.Enums;

namespace ChatApp.Application.Outbox;

/// <summary>
/// Wire shape of the MessageReceived client event. Serialized with camelCase so
/// outbox-dispatch produces byte-identical payloads to the former direct broadcast.
/// </summary>
public sealed record MessageReceivedEvent(
    Guid MessageId,
    Guid ConversationId,
    Guid SenderId,
    string? Content,
    MessageType MessageType,
    DateTimeOffset CreatedAt,
    Guid? ReplyToMessageId);
