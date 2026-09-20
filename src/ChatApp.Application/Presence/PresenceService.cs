using ChatApp.Application.Abstractions;

namespace ChatApp.Application.Presence;

/// <summary>
/// Application-level presence: distributed connection tracking via the store,
/// durable LastSeen in SQL only on the online→offline transition (PLAN §13:
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
        {
            var user = await users.GetByIdAsync(userId, ct);
            if (user is not null)
            {
                user.UpdateLastSeen(DateTimeOffset.UtcNow);
                await users.SaveChangesAsync(ct);
            }
        }
        return becameOffline;
    }

    public Task<bool> IsOnlineAsync(Guid userId, CancellationToken ct = default)
        => store.IsOnlineAsync(userId, ct);
}
