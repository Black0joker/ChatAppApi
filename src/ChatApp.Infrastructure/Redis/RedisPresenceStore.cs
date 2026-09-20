using ChatApp.Application.Abstractions;
using ChatApp.Application.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace ChatApp.Infrastructure.Redis;

/// <summary>
/// Distributed presence on Redis (PLAN §13/§14):
/// <c>presence:user:{id}:connections</c> is a Set of live connection ids, and each
/// connection has its own TTL key <c>presence:user:{id}:conn:{connectionId}</c>.
/// Clients heartbeat; the sweeper evicts lapsed connections so crashed servers or
/// lost disconnects can't leave stale "online" state. Add/remove/refresh run as
/// atomic Lua scripts so concurrent connects across instances can't corrupt the
/// first/last transition detection. Transitions are published on
/// <c>chatapp:presence</c> so every API instance broadcasts them to its own
/// SignalR clients. Remote notifications only raise the local event — they are
/// never republished (no loops).
/// </summary>
public sealed class RedisPresenceStore : IPresenceStore, IDisposable
{
    public const string ChannelName = "chatapp:presence";
    private static readonly RedisChannel Channel = RedisChannel.Literal(ChannelName);

    // Returns member count BEFORE the add; refreshes the connection TTL key.
    private const string AddScript = """
        local count = redis.call('SCARD', KEYS[1])
        redis.call('SADD', KEYS[1], ARGV[1])
        redis.call('SET', KEYS[2], '1', 'EX', ARGV[2])
        return count
        """;

    // Returns {removed, remaining}; deletes the set key when drained.
    private const string RemoveScript = """
        local removed = redis.call('SREM', KEYS[1], ARGV[1])
        redis.call('DEL', KEYS[2])
        local remaining = redis.call('SCARD', KEYS[1])
        if remaining == 0 then redis.call('DEL', KEYS[1]) end
        return {removed, remaining}
        """;

    // Evicts members whose TTL key lapsed. Returns {removed, remaining}.
    private const string SweepScript = """
        local members = redis.call('SMEMBERS', KEYS[1])
        local removed = 0
        for _, m in ipairs(members) do
          if redis.call('EXISTS', ARGV[1] .. m) == 0 then
            redis.call('SREM', KEYS[1], m)
            removed = removed + 1
          end
        end
        local remaining = redis.call('SCARD', KEYS[1])
        if remaining == 0 then redis.call('DEL', KEYS[1]) end
        return {removed, remaining}
        """;

    private readonly IConnectionMultiplexer _mux;
    private readonly ILogger<RedisPresenceStore> _logger;
    private readonly int _timeoutSeconds;
    private bool _disposed;

    public event Func<Guid, bool, Task>? PresenceChanged;

    public RedisPresenceStore(
        IConnectionMultiplexer mux,
        IOptions<PresenceOptions> options,
        ILogger<RedisPresenceStore> logger)
    {
        _mux = mux;
        _logger = logger;
        _timeoutSeconds = options.Value.ConnectionTimeoutSeconds <= 0 ? 45 : options.Value.ConnectionTimeoutSeconds;
        _mux.GetSubscriber().Subscribe(Channel, (_, value) => OnRemoteNotification(value!));
    }

    public async Task<bool> AddConnectionAsync(Guid userId, string connectionId, CancellationToken ct = default)
        => await RefreshConnectionAsync(userId, connectionId, ct);

    public async Task<bool> RemoveConnectionAsync(Guid userId, string connectionId, CancellationToken ct = default)
    {
        var db = _mux.GetDatabase();
        var raw = await db.ScriptEvaluateAsync(RemoveScript,
            [SetKey(userId), ConnKey(userId, connectionId)], [connectionId]);
        var result = (RedisResult[]?)raw;
        var last = result is { Length: 2 } && (int)result[0] == 1 && (int)result[1] == 0;
        if (last)
        {
            await db.PublishAsync(Channel, $"offline:{userId:N}");
            await RaiseAsync(userId, false);
        }
        return last;
    }

    public async Task<bool> RefreshConnectionAsync(Guid userId, string connectionId, CancellationToken ct = default)
    {
        var db = _mux.GetDatabase();
        var before = (int)await db.ScriptEvaluateAsync(AddScript,
            [SetKey(userId), ConnKey(userId, connectionId)], [connectionId, _timeoutSeconds]);
        var first = before == 0;
        if (first)
        {
            await db.PublishAsync(Channel, $"online:{userId:N}");
            await RaiseAsync(userId, true);
        }
        return first;
    }

    public async Task<IReadOnlyList<Guid>> SweepStaleConnectionsAsync(CancellationToken ct = default)
    {
        var wentOffline = new List<Guid>();
        var db = _mux.GetDatabase();
        // Standalone Redis (compose) exposes a single endpoint set.
        foreach (var endpoint in _mux.GetEndPoints())
        {
            var server = _mux.GetServer(endpoint);
            await foreach (var key in server.KeysAsync(pattern: "presence:user:*:connections"))
            {
                var userId = ParseUserId(key);
                if (userId is null)
                    continue;
                var raw = await db.ScriptEvaluateAsync(SweepScript, [key], [ConnPrefix(userId.Value)]);
                var result = (RedisResult[]?)raw;
                if (result is { Length: 2 } && (int)result[1] == 0)
                {
                    await db.PublishAsync(Channel, $"offline:{userId:N}");
                    await RaiseAsync(userId.Value, false);
                    wentOffline.Add(userId.Value);
                }
            }
        }
        return wentOffline;
    }

    public async Task<bool> IsOnlineAsync(Guid userId, CancellationToken ct = default)
        => await _mux.GetDatabase().KeyExistsAsync(SetKey(userId));

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

    private static string SetKey(Guid userId) => $"presence:user:{userId:N}:connections";
    private static string ConnKey(Guid userId, string connectionId) => $"{ConnPrefix(userId)}{connectionId}";
    private static string ConnPrefix(Guid userId) => $"presence:user:{userId:N}:conn:";

    private static Guid? ParseUserId(RedisKey key)
    {
        // presence:user:{32 hex}:connections
        var s = key.ToString();
        const string prefix = "presence:user:";
        const string suffix = ":connections";
        if (!s.StartsWith(prefix) || !s.EndsWith(suffix) || s.Length != prefix.Length + 32 + suffix.Length)
            return null;
        return Guid.TryParse(s.Substring(prefix.Length, 32), out var id) ? id : null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        try { _mux.GetSubscriber().Unsubscribe(Channel); } catch { /* shutdown path */ }
    }
}
