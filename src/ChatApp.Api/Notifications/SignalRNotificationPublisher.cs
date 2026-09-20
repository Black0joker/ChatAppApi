using ChatApp.Api.Hubs;
using ChatApp.Application.Abstractions;
using Microsoft.AspNetCore.SignalR;

namespace ChatApp.Api.Notifications;

/// <summary>
/// Adapts the application fan-out port to SignalR per-user delivery.
/// UserIdentifier defaults to ClaimTypes.NameIdentifier, which our JWT sets —
/// so this reaches all of the user's connections (all instances via backplane).
/// </summary>
public sealed class SignalRNotificationPublisher(IHubContext<ChatHub> hubs) : INotificationPublisher
{
    public Task PublishToUserAsync(Guid userId, string method, object payload, CancellationToken ct = default)
        => hubs.Clients.User(userId.ToString()).SendAsync(method, payload, ct);
}
