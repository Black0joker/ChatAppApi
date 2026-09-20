using System.Collections.Concurrent;
using ChatApp.Application.Abstractions;
using ChatApp.Application.Common;
using Microsoft.Extensions.Options;

namespace ChatApp.Infrastructure.Redis;

/// <summary>
/// Single-instance fallback when Redis is unreachable. Same transition semantics
/// as <see cref="RedisPresenceStore"/> (TTL + sweep); events stay in-process.
/// </summary>
public sealed class InMemoryPresenceStore(IOptions<PresenceOptions> options) : IPresenceStore
{
    private readonly TimeSpan _timeout = TimeSpan.FromSeconds(
        options.Value.ConnectionTimeoutSeconds <= 0 ? 45 : options.Value.ConnectionTimeoutSeconds);

    // user -> (connection -> last refresh)
    private readonly ConcurrentDictionary<Guid, Dictionary<string, DateTimeOffset>> _connections = new();
    private readonly object _gate = new();

    public event Func<Guid, bool, Task>? PresenceChanged;

    public Task<bool> AddConnectionAsync(Guid userId, string connectionId, CancellationToken ct = default)
        => RefreshConnectionAsync(userId, connectionId, ct);

    public Task<bool> RemoveConnectionAsync(Guid userId, string connectionId, CancellationToken ct = default)
    {
        bool last = false;
        lock (_gate)
        {
            if (_connections.TryGetValue(userId, out var set) && set.Remove(connectionId) && set.Count == 0)
            {
                _connections.TryRemove(userId, out _);
                last = true;
            }
        }
        return RaiseAndReturnAsync(userId, last, false);
    }

    public Task<bool> RefreshConnectionAsync(Guid userId, string connectionId, CancellationToken ct = default)
    {
        bool first;
        lock (_gate)
        {
            var set = _connections.GetOrAdd(userId, _ => []);
            first = set.Count == 0;
            set[connectionId] = DateTimeOffset.UtcNow;
        }
        return RaiseAndReturnAsync(userId, first, true);
    }

    public Task<IReadOnlyList<Guid>> SweepStaleConnectionsAsync(CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow - _timeout;
        var wentOffline = new List<Guid>();
        lock (_gate)
        {
            foreach (var (userId, set) in _connections)
            {
                foreach (var conn in set.Where(kv => kv.Value < cutoff).Select(kv => kv.Key).ToList())
                    set.Remove(conn);
                if (set.Count == 0 && _connections.TryRemove(userId, out _))
                    wentOffline.Add(userId);
            }
        }
        return RaiseOfflineAndReturnAsync(wentOffline);
    }

    public Task<bool> IsOnlineAsync(Guid userId, CancellationToken ct = default)
        => Task.FromResult(_connections.TryGetValue(userId, out var set) && set.Count > 0);

    private async Task<bool> RaiseAndReturnAsync(Guid userId, bool transitioned, bool isOnline)
    {
        if (transitioned && PresenceChanged is not null)
            await PresenceChanged.Invoke(userId, isOnline);
        return transitioned;
    }

    private async Task<IReadOnlyList<Guid>> RaiseOfflineAndReturnAsync(List<Guid> wentOffline)
    {
        if (PresenceChanged is not null)
            foreach (var id in wentOffline)
                await PresenceChanged.Invoke(id, false);
        return wentOffline;
    }
}
