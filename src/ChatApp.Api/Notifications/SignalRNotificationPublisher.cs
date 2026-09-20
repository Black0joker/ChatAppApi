using System.Text.Json.Nodes;
using ChatApp.Api.Hubs;
using ChatApp.Application.Abstractions;
using Microsoft.AspNetCore.SignalR;

namespace ChatApp.Api.Notifications;

/// <summary>
/// Adapts the application fan-out port to SignalR delivery.
/// UserIdentifier defaults to ClaimTypes.NameIdentifier, which our JWT sets —
/// so user delivery reaches all of the user's connections (all instances via
/// backplane). Group delivery likewise rides the backplane cross-instance.
/// </summary>
public sealed class SignalRNotificationPublisher(IHubContext<ChatHub> hubs) : INotificationPublisher
{
    public Task PublishToUserAsync(Guid userId, string method, object payload, CancellationToken ct = default)
        => hubs.Clients.User(userId.ToString()).SendAsync(method, payload, ct);

    public Task PublishToConversationAsync(Guid conversationId, string method, string payloadJson, CancellationToken ct = default)
    {
        // Group naming lives here, next to the hub — the only place that must match JoinConversation.
        // JsonNode: no disposal hazards, serializes back to the stored shape.
        var payload = JsonNode.Parse(payloadJson)
            ?? throw new InvalidOperationException("Outbox payload is not valid JSON.");
        return hubs.Clients.Group(ChatHub.GroupName(conversationId)).SendAsync(method, payload, ct);
    }
}
