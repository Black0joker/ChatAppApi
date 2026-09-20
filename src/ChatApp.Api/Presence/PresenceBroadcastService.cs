using ChatApp.Api.Hubs;
using ChatApp.Application.Abstractions;
using Microsoft.AspNetCore.SignalR;

namespace ChatApp.Api.Presence;

/// <summary>
/// Bridges presence transitions (local or arrived via Redis pub/sub from another
/// instance) to SignalR client events. One subscriber per API instance; the store
/// guarantees each transition is published once.
/// </summary>
public sealed class PresenceBroadcastService : BackgroundService
{
    private readonly IPresenceStore _store;
    private readonly IHubContext<ChatHub> _hubs;
    private readonly ILogger<PresenceBroadcastService> _logger;

    public PresenceBroadcastService(
        IPresenceStore store,
        IHubContext<ChatHub> hubs,
        ILogger<PresenceBroadcastService> logger)
    {
        _store = store;
        _hubs = hubs;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _store.PresenceChanged += OnPresenceChangedAsync;
        stoppingToken.Register(() => _store.PresenceChanged -= OnPresenceChangedAsync);
        return Task.CompletedTask;
    }

    private Task OnPresenceChangedAsync(Guid userId, bool isOnline)
    {
        _logger.LogInformation("Presence transition. UserId={UserId} IsOnline={IsOnline}", userId, isOnline);
        return isOnline
            ? _hubs.Clients.All.SendAsync(ChatHubEvents.UserOnline, new { userId })
            : _hubs.Clients.All.SendAsync(ChatHubEvents.UserOffline, new { userId, lastSeenAt = DateTimeOffset.UtcNow });
    }
}
