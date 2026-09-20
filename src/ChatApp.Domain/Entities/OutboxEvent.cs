namespace ChatApp.Domain.Entities;

/// <summary>
/// Transactional outbox row (PLAN §39). Written in the SAME database transaction
/// as the message it describes, so "saved but never published" is impossible:
/// the dispatcher picks up anything without ProcessedAt, including rows orphaned
/// by a crash between commit and dispatch.
/// </summary>
public sealed class OutboxEvent
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    /// <summary>Client event name, e.g. MessageReceived (matches ChatHubEvents).</summary>
    public string Type { get; private set; } = string.Empty;
    /// <summary>Conversation group the event targets.</summary>
    public Guid ConversationId { get; private set; }
    /// <summary>Serialized event payload (JSON).</summary>
    public string Payload { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ProcessedAt { get; private set; }
    public int Attempts { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; } = DateTimeOffset.UtcNow;
    public string? Error { get; private set; }

    private OutboxEvent() { } // EF Core

    public OutboxEvent(string type, Guid conversationId, string payload)
    {
        Id = Guid.NewGuid();
        Type = type;
        ConversationId = conversationId;
        Payload = payload;
        CreatedAt = DateTimeOffset.UtcNow;
        NextAttemptAt = CreatedAt;
    }

    public void RecordFailure(string error, DateTimeOffset nextAttemptAt)
    {
        Attempts++;
        Error = error;
        NextAttemptAt = nextAttemptAt;
    }

    /// <summary>Marks done — including exhausted retries (dead-lettered, Error kept for ops).</summary>
    public void MarkProcessed() => ProcessedAt = DateTimeOffset.UtcNow;
}
