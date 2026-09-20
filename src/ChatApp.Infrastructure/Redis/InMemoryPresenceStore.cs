using System.Collections.Concurrent;
using ChatApp.Application.Abstractions;

namespace ChatApp.Infrastructure.Redis;

/// <summary>
/// Single-instance fallback when Redis is unreachable. Same transition semantics
/// as <see cref="RedisPresenceStore"/>; events stay in-process.
/// </summary>
public sealed class InMemoryPresenceStore : IPresenceStore
{
    private readonly ConcurrentDictionary<Guid, HashSet<string>> _connections = new();
    private readonly object _gate = new();

    public event Func<Guid, bool, Task>? PresenceChanged;

    public async Task<bool> AddConnectionAsync(Guid userId, string connectionId, CancellationToken ct = default)
    {
        bool first;
        lock (_gate)
        {
            var set = _connections.GetOrAdd(userId, _ => []);
            first = set.Count == 0;
            set.Add(connectionId);
        }
        if (first)
            await RaiseAsync(userId, true);
        return first;
    }

    public async Task<bool> RemoveConnectionAsync(Guid userId, string connectionId, CancellationToken ct = default)
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
        if (last)
            await RaiseAsync(userId, false);
        return last;
    }

    public Task<bool> IsOnlineAsync(Guid userId, CancellationToken ct = default)
        => Task.FromResult(_connections.TryGetValue(userId, out var set) && set.Count > 0);

    private Task RaiseAsync(Guid userId, bool isOnline)
        => PresenceChanged?.Invoke(userId, isOnline) ?? Task.CompletedTask;
}
