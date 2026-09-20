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

    Task<bool> IsOnlineAsync(Guid userId, CancellationToken ct = default);
}
