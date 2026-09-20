using ChatApp.Application.Common;
using ChatApp.Application.Presence;
using ChatApp.Domain.Entities;
using ChatApp.Infrastructure.Redis;
using Microsoft.Extensions.Options;

namespace ChatApp.UnitTests;

public sealed class PresenceServiceTests
{
    private static (PresenceService Svc, InMemoryPresenceStore Store, FakeUserRepository Users) Create(
        int timeoutSeconds = 45)
    {
        var store = new InMemoryPresenceStore(Options.Create(new PresenceOptions
        {
            ConnectionTimeoutSeconds = timeoutSeconds,
            SweepIntervalSeconds = 20,
            HeartbeatIntervalSeconds = 15
        }));
        var users = new FakeUserRepository();
        return (new PresenceService(store, users), store, users);
    }

    [Fact]
    public async Task First_connection_marks_online_second_does_not_retransition()
    {
        var (svc, _, users) = Create();
        var alice = new User("alice", "Alice", "alice@example.com", "HASHED");
        await users.AddAsync(alice);

        Assert.True(await svc.UserConnectedAsync(alice.Id, "c1"));
        Assert.False(await svc.UserConnectedAsync(alice.Id, "c2")); // phone + browser
        Assert.True(await svc.IsOnlineAsync(alice.Id));
    }

    [Fact]
    public async Task Last_disconnect_marks_offline_and_stamps_last_seen()
    {
        var (svc, _, users) = Create();
        var alice = new User("alice", "Alice", "alice@example.com", "HASHED");
        await users.AddAsync(alice);
        await svc.UserConnectedAsync(alice.Id, "c1");
        await svc.UserConnectedAsync(alice.Id, "c2");

        Assert.False(await svc.UserDisconnectedAsync(alice.Id, "c1")); // still online via c2
        Assert.True(await svc.IsOnlineAsync(alice.Id));

        Assert.True(await svc.UserDisconnectedAsync(alice.Id, "c2"));
        Assert.False(await svc.IsOnlineAsync(alice.Id));
        Assert.NotNull(alice.LastSeenAt);
    }

    [Fact]
    public async Task Disconnect_of_unknown_connection_is_not_an_offline_transition()
    {
        var (svc, _, users) = Create();
        var alice = new User("alice", "Alice", "alice@example.com", "HASHED");
        await users.AddAsync(alice);
        await svc.UserConnectedAsync(alice.Id, "c1");

        // Stale/duplicate disconnect must not fake an offline event or touch LastSeen.
        Assert.False(await svc.UserDisconnectedAsync(alice.Id, "stale"));
        Assert.True(await svc.IsOnlineAsync(alice.Id));
        Assert.Null(alice.LastSeenAt);
    }

    [Fact]
    public async Task Transitions_raise_presence_changed_once_each()
    {
        var (svc, store, users) = Create();
        var alice = new User("alice", "Alice", "alice@example.com", "HASHED");
        await users.AddAsync(alice);

        var events = new List<(Guid UserId, bool IsOnline)>();
        store.PresenceChanged += (id, online) => { events.Add((id, online)); return Task.CompletedTask; };

        await svc.UserConnectedAsync(alice.Id, "c1");
        await svc.UserConnectedAsync(alice.Id, "c2"); // no event
        await svc.UserDisconnectedAsync(alice.Id, "c1"); // no event
        await svc.UserDisconnectedAsync(alice.Id, "c2");

        Assert.Equal([(alice.Id, true), (alice.Id, false)], events);
    }

    [Fact]
    public async Task Sweep_evicts_stale_connections_and_stamps_last_seen()
    {
        var (svc, store, users) = Create(timeoutSeconds: 1);
        var alice = new User("alice", "Alice", "alice@example.com", "HASHED");
        await users.AddAsync(alice);
        await svc.UserConnectedAsync(alice.Id, "c1");

        await Task.Delay(TimeSpan.FromSeconds(1.2));

        var events = new List<(Guid UserId, bool IsOnline)>();
        store.PresenceChanged += (id, online) => { events.Add((id, online)); return Task.CompletedTask; };
        var evicted = await svc.SweepStaleConnectionsAsync();

        Assert.Equal(1, evicted);
        Assert.False(await svc.IsOnlineAsync(alice.Id));
        Assert.NotNull(alice.LastSeenAt);
        Assert.Equal([(alice.Id, false)], events);
    }

    [Fact]
    public async Task Heartbeat_keeps_connection_alive_across_sweep()
    {
        var (svc, _, users) = Create(timeoutSeconds: 1);
        var alice = new User("alice", "Alice", "alice@example.com", "HASHED");
        await users.AddAsync(alice);
        await svc.UserConnectedAsync(alice.Id, "c1");

        // Keep heartbeating past the 1s timeout, then sweep: nothing evicted.
        for (var i = 0; i < 3; i++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(400));
            Assert.False(await svc.HeartbeatAsync(alice.Id, "c1"));
        }
        Assert.Equal(0, await svc.SweepStaleConnectionsAsync());
        Assert.True(await svc.IsOnlineAsync(alice.Id));
        Assert.Null(alice.LastSeenAt);
    }

    [Fact]
    public async Task Heartbeat_reactivates_a_silently_dropped_connection()
    {
        var (svc, _, users) = Create(timeoutSeconds: 45);
        var alice = new User("alice", "Alice", "alice@example.com", "HASHED");
        await users.AddAsync(alice);

        // Unknown connection (e.g. store restarted/lost state) comes back online.
        Assert.True(await svc.HeartbeatAsync(alice.Id, "c1"));
        Assert.True(await svc.IsOnlineAsync(alice.Id));
    }
}
