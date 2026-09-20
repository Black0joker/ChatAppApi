using ChatApp.Application.Abstractions;
using ChatApp.Application.Common;
using ChatApp.Application.Conversations;
using ChatApp.Application.Messages;
using ChatApp.Application.Notifications;
using ChatApp.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ChatApp.UnitTests;

internal sealed class FakeNotificationRepository : INotificationRepository
{
    public readonly List<Notification> Rows = [];

    public Task<Notification?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(Rows.FirstOrDefault(x => x.Id == id));

    public Task<IReadOnlyList<Notification>> GetForUserAsync(Guid userId, int limit, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Notification>>(
            Rows.Where(x => x.UserId == userId).OrderByDescending(x => x.CreatedAt).Take(limit).ToList());

    public Task<int> CountUnreadAsync(Guid userId, CancellationToken ct = default)
        => Task.FromResult(Rows.Count(x => x.UserId == userId && !x.IsRead));

    public Task AddAsync(Notification notification, CancellationToken ct = default)
    {
        Rows.Add(notification);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}

internal sealed class ConfigurablePresenceService(HashSet<Guid> online) : IPresenceService
{
    public Task<bool> UserConnectedAsync(Guid userId, string connectionId, CancellationToken ct = default) => Task.FromResult(true);
    public Task<bool> UserDisconnectedAsync(Guid userId, string connectionId, CancellationToken ct = default) => Task.FromResult(true);
    public Task<bool> HeartbeatAsync(Guid userId, string connectionId, CancellationToken ct = default) => Task.FromResult(true);
    public Task<int> SweepStaleConnectionsAsync(CancellationToken ct = default) => Task.FromResult(0);
    public Task<bool> IsOnlineAsync(Guid userId, CancellationToken ct = default) => Task.FromResult(online.Contains(userId));
}

internal sealed class FakePublisher : INotificationPublisher
{
    public List<(Guid UserId, string Method, object Payload)> Published { get; } = [];
    public Task PublishToUserAsync(Guid userId, string method, object payload, CancellationToken ct = default)
    {
        Published.Add((userId, method, payload));
        return Task.CompletedTask;
    }
}

internal sealed class FakePush : IPushNotificationService
{
    public List<(Guid UserId, string Title, string Body)> Pushed { get; } = [];
    public Task PushAsync(Guid userId, string title, string body, IReadOnlyDictionary<string, string>? data, CancellationToken ct = default)
    {
        Pushed.Add((userId, title, body));
        return Task.CompletedTask;
    }
}

public sealed class NotificationServiceTests
{
    private static (NotificationService Svc, FakeNotificationRepository Repo, FakePublisher Publisher,
        FakePush Push, FakeUserRepository Users, FakeConversationRepository Convos) Create(HashSet<Guid>? online = null)
    {
        var repo = new FakeNotificationRepository();
        var convos = new FakeConversationRepository();
        var users = new FakeUserRepository();
        var publisher = new FakePublisher();
        var push = new FakePush();
        var svc = new NotificationService(repo, convos, users,
            new ConfigurablePresenceService(online ?? []), publisher, push);
        return (svc, repo, publisher, push, users, convos);
    }

    private static async Task<(User Alice, User Bob, Guid DirectId)> SeedDirectAsync(
        FakeUserRepository users, FakeConversationRepository convos)
    {
        var alice = new User("alice", "Alice", "alice@example.com", "HASHED");
        var bob = new User("bob", "Bob", "bob@example.com", "HASHED");
        await users.AddAsync(alice); await users.AddAsync(bob);
        var convoSvc = new ConversationService(convos, users, new NullNotificationService(),
            NullLogger<ConversationService>.Instance, Options.Create(new ChatOptions()));
        var direct = await convoSvc.CreateDirectAsync(alice.Id, new CreateDirectRequest(bob.Id));
        return (alice, bob, direct.Id);
    }

    [Fact]
    public async Task Offline_member_gets_row_and_push_not_realtime()
    {
        var (svc, repo, publisher, push, users, convos) = Create();
        var (alice, bob, directId) = await SeedDirectAsync(users, convos);
        var msgId = Guid.NewGuid();

        await svc.NotifyMessageAsync(msgId, directId, alice.Id, "hello there");

        var row = Assert.Single(repo.Rows);
        Assert.Equal(bob.Id, row.UserId);
        Assert.Equal(ChatApp.Domain.Enums.NotificationType.Message, row.Type);
        Assert.Equal("Alice", row.Title);
        Assert.Single(push.Pushed);
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task Online_member_gets_realtime_only()
    {
        var (svc, repo, publisher, push, users, convos) = Create();
        var (alice, bob, directId) = await SeedDirectAsync(users, convos);
        var onlineSvc = new NotificationService(repo, convos, users,
            new ConfigurablePresenceService([bob.Id]), publisher, push);
        var msgId = Guid.NewGuid();

        await onlineSvc.NotifyMessageAsync(msgId, directId, alice.Id, "hello there");

        Assert.Empty(repo.Rows);
        Assert.Empty(push.Pushed);
        var evt = Assert.Single(publisher.Published);
        Assert.Equal(bob.Id, evt.UserId);
        Assert.Equal("NotificationReceived", evt.Method);
    }

    [Fact]
    public async Task Sender_is_never_notified()
    {
        var (svc, repo, publisher, push, users, convos) = Create([Guid.NewGuid()]);
        var (alice, _, directId) = await SeedDirectAsync(users, convos);

        await svc.NotifyMessageAsync(Guid.NewGuid(), directId, alice.Id, "self talk");

        Assert.DoesNotContain(repo.Rows, r => r.UserId == alice.Id); // sender skipped…
        Assert.Single(repo.Rows); // …but offline bob still notified
    }

    [Fact]
    public async Task AddedToGroup_notifies_offline_user_with_row()
    {
        var (svc, repo, publisher, push, users, _) = Create();
        var alice = new User("alice", "Alice", "alice@example.com", "HASHED");
        var bob = new User("bob", "Bob", "bob@example.com", "HASHED");
        await users.AddAsync(alice); await users.AddAsync(bob);
        var convId = Guid.NewGuid();

        await svc.NotifyAddedToGroupAsync(convId, "Team", bob.Id, alice.Id);

        var row = Assert.Single(repo.Rows);
        Assert.Equal(ChatApp.Domain.Enums.NotificationType.AddedToGroup, row.Type);
        Assert.Contains("Team", row.Body);
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task Inbox_and_mark_read_respect_ownership()
    {
        var (svc, repo, _, _, users, convos) = Create();
        var (alice, bob, directId) = await SeedDirectAsync(users, convos);
        await svc.NotifyMessageAsync(Guid.NewGuid(), directId, alice.Id, "one");
        await svc.NotifyMessageAsync(Guid.NewGuid(), directId, alice.Id, "two");

        var inbox = await svc.GetInboxAsync(bob.Id, null);
        Assert.Equal(2, inbox.Items.Count);
        Assert.Equal(2, inbox.UnreadCount);

        await svc.MarkAsReadAsync(bob.Id, inbox.Items[0].Id);
        Assert.Equal(1, (await svc.GetInboxAsync(bob.Id, null)).UnreadCount);

        await Assert.ThrowsAsync<ChatApp.Application.Common.Exceptions.NotFoundAppException>(() =>
            svc.MarkAsReadAsync(alice.Id, inbox.Items[1].Id)); // someone else's row
    }

    [Fact]
    public async Task Send_triggers_fanout_for_offline_members()
    {
        var convos = new FakeConversationRepository();
        var msgs = new FakeMessageRepository();
        var users = new FakeUserRepository();
        var repo = new FakeNotificationRepository();
        var publisher = new FakePublisher();
        var push = new FakePush();
        var opts = Options.Create(new ChatOptions());
        var notify = new NotificationService(repo, convos, users,
            new ConfigurablePresenceService([]), publisher, push);
        var convoSvc = new ConversationService(convos, users, notify,
            NullLogger<ConversationService>.Instance, opts);
        var msgSvc = new MessageService(msgs, convos, new FakeReadReceiptRepository(),
            new FakeAttachmentRepository(), notify, NullLogger<MessageService>.Instance, opts);

        var alice = new User("alice", "Alice", "alice@example.com", "HASHED");
        var bob = new User("bob", "Bob", "bob@example.com", "HASHED");
        await users.AddAsync(alice); await users.AddAsync(bob);
        var direct = await convoSvc.CreateDirectAsync(alice.Id, new CreateDirectRequest(bob.Id));

        await msgSvc.SendAsync(alice.Id, direct.Id, new SendMessageRequest("ping"));

        Assert.Single(repo.Rows); // bob was offline
    }

    [Fact]
    public async Task Notification_failure_does_not_fail_the_send()
    {
        var convos = new FakeConversationRepository();
        var msgs = new FakeMessageRepository();
        var users = new FakeUserRepository();
        var opts = Options.Create(new ChatOptions());
        var failingNotify = new ThrowingNotificationService();
        var msgSvc = new MessageService(msgs, convos, new FakeReadReceiptRepository(),
            new FakeAttachmentRepository(), failingNotify, NullLogger<MessageService>.Instance, opts);
        var convoSvc = new ConversationService(convos, users, new NullNotificationService(),
            NullLogger<ConversationService>.Instance, opts);

        var alice = new User("alice", "Alice", "alice@example.com", "HASHED");
        var bob = new User("bob", "Bob", "bob@example.com", "HASHED");
        await users.AddAsync(alice); await users.AddAsync(bob);
        var direct = await convoSvc.CreateDirectAsync(alice.Id, new CreateDirectRequest(bob.Id));

        var sent = await msgSvc.SendAsync(alice.Id, direct.Id, new SendMessageRequest("still sent"));

        Assert.Equal("still sent", sent.Content);
    }

    private sealed class ThrowingNotificationService : INotificationService
    {
        public Task NotifyMessageAsync(Guid messageId, Guid conversationId, Guid senderId, string contentPreview, CancellationToken ct = default)
            => throw new InvalidOperationException("push exploded");
        public Task NotifyAddedToGroupAsync(Guid conversationId, string conversationName, Guid addedUserId, Guid addedByUserId, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task<NotificationInboxDto> GetInboxAsync(Guid userId, int? limit, CancellationToken ct = default)
            => Task.FromResult(new NotificationInboxDto([], 0));
        public Task MarkAsReadAsync(Guid userId, Guid notificationId, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
