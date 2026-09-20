namespace ChatApp.Application.Abstractions;

public interface IPresenceService
{
    /// <returns>True when the user just came online (first connection).</returns>
    Task<bool> UserConnectedAsync(Guid userId, string connectionId, CancellationToken ct = default);

    /// <returns>True when the user just went offline (last connection removed).</returns>
    Task<bool> UserDisconnectedAsync(Guid userId, string connectionId, CancellationToken ct = default);

    /// <summary>Client heartbeat: refresh the connection TTL.</summary>
    /// <returns>True when this (re)activated the user's online state.</returns>
    Task<bool> HeartbeatAsync(Guid userId, string connectionId, CancellationToken ct = default);

    /// <summary>Evict stale connections; stamps LastSeen for users that went offline.</summary>
    /// <returns>Number of connections evicted.</returns>
    Task<int> SweepStaleConnectionsAsync(CancellationToken ct = default);

    Task<bool> IsOnlineAsync(Guid userId, CancellationToken ct = default);
}
