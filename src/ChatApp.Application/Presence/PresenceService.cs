using ChatApp.Application.Abstractions;

namespace ChatApp.Application.Presence;

/// <summary>
/// Application-level presence: distributed connection tracking via the store,
/// durable LastSeen in SQL only on online→offline transitions (PLAN §13:
/// never write SQL per connect/disconnect event).
/// </summary>
public sealed class PresenceService(IPresenceStore store, IUserRepository users) : IPresenceService
{
    public Task<bool> UserConnectedAsync(Guid userId, string connectionId, CancellationToken ct = default)
        => store.AddConnectionAsync(userId, connectionId, ct);

    public async Task<bool> UserDisconnectedAsync(Guid userId, string connectionId, CancellationToken ct = default)
    {
        var becameOffline = await store.RemoveConnectionAsync(userId, connectionId, ct);
        if (becameOffline)
            await StampLastSeenAsync([userId], ct);
        return becameOffline;
    }

    public Task<bool> HeartbeatAsync(Guid userId, string connectionId, CancellationToken ct = default)
        => store.RefreshConnectionAsync(userId, connectionId, ct);

    public async Task<int> SweepStaleConnectionsAsync(CancellationToken ct = default)
    {
        var offlineUsers = await store.SweepStaleConnectionsAsync(ct);
        if (offlineUsers.Count > 0)
            await StampLastSeenAsync(offlineUsers, ct);
        return offlineUsers.Count;
    }

    public Task<bool> IsOnlineAsync(Guid userId, CancellationToken ct = default)
        => store.IsOnlineAsync(userId, ct);

    private async Task StampLastSeenAsync(IReadOnlyList<Guid> userIds, CancellationToken ct)
    {
        if (userIds.Count == 0)
            return;
        await users.UpdateLastSeenAsync(userIds, DateTimeOffset.UtcNow, ct);
    }
}
