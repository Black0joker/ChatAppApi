namespace ChatApp.Application.Abstractions;

/// <summary>
/// Real-time fan-out port, adapted in the Api layer with SignalR's IHubContext.
/// Lets application services notify online users without depending on SignalR.
/// </summary>
public interface INotificationPublisher
{
    Task PublishToUserAsync(Guid userId, string method, object payload, CancellationToken ct = default);

    /// <summary>
    /// Conversation fan-out for outbox-dispatch (payload is pre-serialized JSON).
    /// Group naming is the adapter's job — the single source of truth lives with the hub.
    /// </summary>
    Task PublishToConversationAsync(Guid conversationId, string method, string payloadJson, CancellationToken ct = default);
}
