using ChatApp.Application.Abstractions;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace ChatApp.Infrastructure.Redis;

/// <summary>
/// Distributed presence on Redis (PLAN §13/§14):
/// <c>presence:user:{id}:connections</c> is a Set of live connection ids.
/// Add/remove run as atomic Lua scripts so concurrent connects across instances
/// can't corrupt the first/last transition detection. Transitions are published
/// on <c>chatapp:presence</c> so every API instance broadcasts them to its own
/// SignalR clients. Remote notifications only raise the local event — they are
/// never republished (no loops).
/// </summary>
public sealed class RedisPresenceStore : IPresenceStore, IDisposable
{
    public const string ChannelName = "chatapp:presence";
    private static readonly RedisChannel Channel = RedisChannel.Literal(ChannelName);

    // Returns member count BEFORE the add.
    private const string AddScript = """
        local count = redis.call('SCARD', KEYS[1])
        redis.call('SADD', KEYS[1], ARGV[1])
        return count
        """;

    // Returns {removed, remaining}; deletes the key when drained.
    private const string RemoveScript = """
        local removed = redis.call('SREM', KEYS[1], ARGV[1])
        local remaining = redis.call('SCARD', KEYS[1])
        if remaining == 0 then redis.call('DEL', KEYS[1]) end
        return {removed, remaining}
        """;

    private readonly IConnectionMultiplexer _mux;
    private readonly ILogger<RedisPresenceStore> _logger;
    private bool _disposed;

    public event Func<Guid, bool, Task>? PresenceChanged;

    public RedisPresenceStore(IConnectionMultiplexer mux, ILogger<RedisPresenceStore> logger)
    {
        _mux = mux;
        _logger = logger;
        _mux.GetSubscriber().Subscribe(Channel, (_, value) => OnRemoteNotification(value!));
    }

    public async Task<bool> AddConnectionAsync(Guid userId, string connectionId, CancellationToken ct = default)
    {
        var db = _mux.GetDatabase();
        var before = (int)await db.ScriptEvaluateAsync(AddScript, [Key(userId)], [connectionId]);
        var first = before == 0;
        if (first)
        {
            await db.PublishAsync(Channel, $"online:{userId:N}");
            await RaiseAsync(userId, true);
        }
        return first;
    }

    public async Task<bool> RemoveConnectionAsync(Guid userId, string connectionId, CancellationToken ct = default)
    {
        var db = _mux.GetDatabase();
        var raw = await db.ScriptEvaluateAsync(RemoveScript, [Key(userId)], [connectionId]);
        var result = (RedisResult[]?)raw;
        var last = result is { Length: 2 } && (int)result[0] == 1 && (int)result[1] == 0;
        if (last)
        {
            await db.PublishAsync(Channel, $"offline:{userId:N}");
            await RaiseAsync(userId, false);
        }
        return last;
    }

    public async Task<bool> IsOnlineAsync(Guid userId, CancellationToken ct = default)
        => await _mux.GetDatabase().KeyExistsAsync(Key(userId));

    private void OnRemoteNotification(string value)
    {
        // Pub/sub callback is sync: parse here, raise without republishing.
        var sep = value.IndexOf(':');
        if (sep <= 0 || !Guid.TryParse(value[(sep + 1)..], out var userId))
        {
            _logger.LogWarning("Ignoring malformed presence notification: {Value}", value);
            return;
        }
        var online = value.StartsWith("online:", StringComparison.Ordinal);
        _ = Task.Run(() => RaiseAsync(userId, online));
    }

    private Task RaiseAsync(Guid userId, bool isOnline)
        => PresenceChanged?.Invoke(userId, isOnline) ?? Task.CompletedTask;

    private static string Key(Guid userId) => $"presence:user:{userId:N}:connections";

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        try { _mux.GetSubscriber().Unsubscribe(Channel); } catch { /* shutdown path */ }
    }
}
