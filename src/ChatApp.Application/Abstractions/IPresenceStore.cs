namespace ChatApp.Application.Abstractions;

/// <summary>
/// Distributed connection registry (PLAN §13/§14). Redis-backed in production,
/// in-memory fallback for single-instance dev. A user is online while at least
/// one connection exists — never one row per connect/disconnect in SQL.
/// </summary>
public interface IPresenceStore
{
    /// <summary>Raised on online/offline transitions, local and (via pub/sub) remote.</summary>
    event Func<Guid, bool, Task>? PresenceChanged; // (userId, isOnline)

    /// <returns>True when this was the user's first active connection.</returns>
    Task<bool> AddConnectionAsync(Guid userId, string connectionId, CancellationToken ct = default);

    /// <returns>True when the user's last active connection was removed.</returns>
    Task<bool> RemoveConnectionAsync(Guid userId, string connectionId, CancellationToken ct = default);

    /// <summary>
    /// Refresh a connection's TTL. Re-activates silently-dropped connections.
    /// </summary>
    /// <returns>True when this (re)activated the user's online state.</returns>
    Task<bool> RefreshConnectionAsync(Guid userId, string connectionId, CancellationToken ct = default);

    /// <summary>
    /// Evict connections whose TTL lapsed (crashed servers, lost disconnects).
    /// </summary>
    /// <returns>Ids of users that transitioned to offline.</returns>
    Task<IReadOnlyList<Guid>> SweepStaleConnectionsAsync(CancellationToken ct = default);

    Task<bool> IsOnlineAsync(Guid userId, CancellationToken ct = default);
}
