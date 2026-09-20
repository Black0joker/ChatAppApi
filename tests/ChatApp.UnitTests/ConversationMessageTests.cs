using ChatApp.Application.Abstractions;
using ChatApp.Application.Common;
using ChatApp.Application.Common.Exceptions;
using ChatApp.Application.Conversations;
using ChatApp.Application.Messages;
using ChatApp.Domain.Entities;
using ChatApp.Domain.Enums;
using Microsoft.Extensions.Options;

namespace ChatApp.UnitTests;

internal sealed class FakeConversationRepository : IConversationRepository
{
    private readonly List<Conversation> _conversations = [];

    public Task<Conversation?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(_conversations.FirstOrDefault(x => x.Id == id));

    public Task<Conversation?> GetDirectBetweenAsync(Guid userA, Guid userB, CancellationToken ct = default)
        => Task.FromResult(_conversations.FirstOrDefault(x =>
            x.Type == ConversationType.Direct
            && x.Members.Any(m => m.UserId == userA && m.IsActive)
            && x.Members.Any(m => m.UserId == userB && m.IsActive)));

    public Task<IReadOnlyList<Conversation>> GetForUserAsync(Guid userId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Conversation>>(
            _conversations.Where(x => x.Members.Any(m => m.UserId == userId && m.IsActive)).ToList());

    public Task AddAsync(Conversation conversation, CancellationToken ct = default)
    {
        _conversations.Add(conversation);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}

internal sealed class FakeMessageRepository : IMessageRepository
{
    private readonly List<Message> _messages = [];

    public Task<Message?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(_messages.FirstOrDefault(x => x.Id == id));

    public Task<IReadOnlyList<Message>> GetHistoryAsync(
        Guid conversationId,
        DateTimeOffset? beforeCreatedAt, Guid? beforeId,
        DateTimeOffset? afterCreatedAt, Guid? afterId,
        int limit, CancellationToken ct = default)
    {
        var q = _messages.Where(x => x.ConversationId == conversationId);
        if (beforeCreatedAt.HasValue && beforeId.HasValue)
            q = q.Where(x => x.CreatedAt < beforeCreatedAt
                || (x.CreatedAt == beforeCreatedAt && x.Id.CompareTo(beforeId.Value) < 0));
        if (afterCreatedAt.HasValue && afterId.HasValue)
            q = q.Where(x => x.CreatedAt > afterCreatedAt
                || (x.CreatedAt == afterCreatedAt && x.Id.CompareTo(afterId.Value) > 0));
        return Task.FromResult<IReadOnlyList<Message>>(q
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Take(limit + 1).ToList());
    }

    public Task AddAsync(Message message, CancellationToken ct = default)
    {
        _messages.Add(message);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}

internal static class TestUsers
{
    public static User Alice() => new("alice", "Alice", "alice@example.com", "HASHED");
    public static User Bob() => new("bob", "Bob", "bob@example.com", "HASHED");
    public static User Carol() => new("carol", "Carol", "carol@example.com", "HASHED");
}

internal sealed class FakeReadReceiptRepository : IReadReceiptRepository
{
    private readonly List<MessageReadReceipt> _receipts = [];

    public Task<MessageReadReceipt?> GetAsync(Guid messageId, Guid userId, CancellationToken ct = default)
        => Task.FromResult(_receipts.FirstOrDefault(x => x.MessageId == messageId && x.UserId == userId));

    public Task<IReadOnlyList<MessageReadReceipt>> GetForMessagesAsync(IEnumerable<Guid> messageIds, CancellationToken ct = default)
    {
        var set = messageIds.ToHashSet();
        return Task.FromResult<IReadOnlyList<MessageReadReceipt>>(_receipts.Where(x => set.Contains(x.MessageId)).ToList());
    }

    public Task AddAsync(MessageReadReceipt receipt, CancellationToken ct = default)
    {
        _receipts.Add(receipt);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}

internal sealed class FakeAttachmentRepository : IAttachmentRepository
{
    private readonly List<MessageAttachment> _attachments = [];

    public Task<MessageAttachment?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(_attachments.FirstOrDefault(x => x.Id == id));

    public Task<IReadOnlyList<MessageAttachment>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        var set = ids.ToHashSet();
        return Task.FromResult<IReadOnlyList<MessageAttachment>>(_attachments.Where(x => set.Contains(x.Id)).ToList());
    }

    public Task<IReadOnlyList<MessageAttachment>> GetForMessagesAsync(IEnumerable<Guid> messageIds, CancellationToken ct = default)
    {
        var set = messageIds.ToHashSet();
        return Task.FromResult<IReadOnlyList<MessageAttachment>>(
            _attachments.Where(x => x.MessageId.HasValue && set.Contains(x.MessageId.Value)).ToList());
    }

    public Task AddAsync(MessageAttachment attachment, CancellationToken ct = default)
    {
        _attachments.Add(attachment);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(MessageAttachment attachment, CancellationToken ct = default)
    {
        _attachments.Remove(attachment);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}

public sealed class ConversationServiceTests
{
    private static (ConversationService Svc, FakeConversationRepository Convos, FakeUserRepository Users) Create()
    {
        var convos = new FakeConversationRepository();
        var users = new FakeUserRepository();
        var svc = new ConversationService(convos, users, Options.Create(new ChatOptions()));
        return (svc, convos, users);
    }

    private static async Task<User> AddUser(FakeUserRepository users, User u)
    {
        await users.AddAsync(u);
        return u;
    }

    [Fact]
    public async Task CreateDirect_twice_returns_same_conversation()
    {
        var (svc, _, users) = Create();
        var alice = await AddUser(users, TestUsers.Alice());
        var bob = await AddUser(users, TestUsers.Bob());

        var first = await svc.CreateDirectAsync(alice.Id, new CreateDirectRequest(bob.Id));
        var second = await svc.CreateDirectAsync(alice.Id, new CreateDirectRequest(bob.Id));

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(2, first.Members.Count);
    }

    [Fact]
    public async Task CreateDirect_with_self_throws_validation()
    {
        var (svc, _, users) = Create();
        var alice = await AddUser(users, TestUsers.Alice());
        await Assert.ThrowsAsync<ValidationAppException>(() =>
            svc.CreateDirectAsync(alice.Id, new CreateDirectRequest(alice.Id)));
    }

    [Fact]
    public async Task Non_member_cannot_read_conversation()
    {
        var (svc, _, users) = Create();
        var alice = await AddUser(users, TestUsers.Alice());
        var bob = await AddUser(users, TestUsers.Bob());
        var carol = await AddUser(users, TestUsers.Carol());
        var direct = await svc.CreateDirectAsync(alice.Id, new CreateDirectRequest(bob.Id));

        // Existence is not leaked: 404, not 403.
        await Assert.ThrowsAsync<NotFoundAppException>(() => svc.GetByIdAsync(carol.Id, direct.Id));
    }

    [Fact]
    public async Task Plain_member_cannot_add_members()
    {
        var (svc, _, users) = Create();
        var alice = await AddUser(users, TestUsers.Alice());
        var bob = await AddUser(users, TestUsers.Bob());
        var carol = await AddUser(users, TestUsers.Carol());
        var group = await svc.CreateGroupAsync(alice.Id, new CreateGroupRequest("Team", [bob.Id]));

        await Assert.ThrowsAsync<ForbiddenAppException>(() =>
            svc.AddMemberAsync(bob.Id, group.Id, new AddMemberRequest(carol.Id)));
    }

    [Fact]
    public async Task Only_owner_can_promote_admin()
    {
        var (svc, _, users) = Create();
        var alice = await AddUser(users, TestUsers.Alice());
        var bob = await AddUser(users, TestUsers.Bob());
        var group = await svc.CreateGroupAsync(alice.Id, new CreateGroupRequest("Team", [bob.Id]));

        // Owner promotes Bob to admin.
        var updated = await svc.AddMemberAsync(alice.Id, group.Id, new AddMemberRequest(bob.Id, MemberRole.Admin));
        Assert.Equal(MemberRole.Admin, updated.Members.First(m => m.UserId == bob.Id).Role);
    }

    [Fact]
    public async Task Owner_leave_promotes_oldest_admin()
    {
        var (svc, convos, users) = Create();
        var alice = await AddUser(users, TestUsers.Alice());
        var bob = await AddUser(users, TestUsers.Bob());
        var group = await svc.CreateGroupAsync(alice.Id, new CreateGroupRequest("Team", [bob.Id]));
        await svc.AddMemberAsync(alice.Id, group.Id, new AddMemberRequest(bob.Id, MemberRole.Admin));

        await svc.RemoveMemberAsync(alice.Id, group.Id, alice.Id);

        var stored = await convos.GetByIdAsync(group.Id);
        Assert.Equal(MemberRole.Owner, stored!.Members.First(m => m.UserId == bob.Id).Role);
    }

    [Fact]
    public async Task Direct_conversation_rejects_member_management()
    {
        var (svc, _, users) = Create();
        var alice = await AddUser(users, TestUsers.Alice());
        var bob = await AddUser(users, TestUsers.Bob());
        var carol = await AddUser(users, TestUsers.Carol());
        var direct = await svc.CreateDirectAsync(alice.Id, new CreateDirectRequest(bob.Id));

        await Assert.ThrowsAsync<ValidationAppException>(() =>
            svc.AddMemberAsync(alice.Id, direct.Id, new AddMemberRequest(carol.Id)));
    }

    [Fact]
    public async Task GetMine_fetches_users_in_a_single_batch()
    {
        var (svc, _, users) = Create();
        var alice = await AddUser(users, TestUsers.Alice());
        var bob = await AddUser(users, TestUsers.Bob());
        var carol = await AddUser(users, TestUsers.Carol());
        await svc.CreateDirectAsync(alice.Id, new CreateDirectRequest(bob.Id));
        await svc.CreateGroupAsync(alice.Id, new CreateGroupRequest("Team", [bob.Id, carol.Id]));
        await svc.CreateGroupAsync(bob.Id, new CreateGroupRequest("Duo", [alice.Id]));

        users.GetByIdsCallCount = 0; // ignore setup traffic
        var before = users.GetByIdsCallCount;

        var mine = await svc.GetMineAsync(alice.Id);

        Assert.Equal(3, mine.Count);
        Assert.Equal(before + 1, users.GetByIdsCallCount); // 1 batch, not M x N
        Assert.All(mine, c => Assert.All(c.Members, m => Assert.NotEqual("unknown", m.Username)));
    }
}

public sealed class MessageServiceTests
{
    private static (MessageService Svc, ConversationService ConvoSvc, FakeUserRepository Users) Create()
    {
        var convos = new FakeConversationRepository();
        var msgs = new FakeMessageRepository();
        var users = new FakeUserRepository();
        var opts = Options.Create(new ChatOptions());
        return (new MessageService(msgs, convos, new FakeReadReceiptRepository(), new FakeAttachmentRepository(), opts), new ConversationService(convos, users, opts), users);
    }

    [Fact]
    public async Task Non_member_cannot_send()
    {
        var (svc, convoSvc, users) = Create();
        var alice = new User("alice", "Alice", "alice@example.com", "HASHED");
        var bob = new User("bob", "Bob", "bob@example.com", "HASHED");
        var carol = new User("carol", "Carol", "carol@example.com", "HASHED");
        await users.AddAsync(alice); await users.AddAsync(bob); await users.AddAsync(carol);
        var direct = await convoSvc.CreateDirectAsync(alice.Id, new CreateDirectRequest(bob.Id));

        await Assert.ThrowsAsync<NotFoundAppException>(() =>
            svc.SendAsync(carol.Id, direct.Id, new SendMessageRequest("hi")));
    }

    [Fact]
    public async Task History_paginates_newest_first()
    {
        var (svc, convoSvc, users) = Create();
        var alice = new User("alice", "Alice", "alice@example.com", "HASHED");
        var bob = new User("bob", "Bob", "bob@example.com", "HASHED");
        await users.AddAsync(alice); await users.AddAsync(bob);
        var direct = await convoSvc.CreateDirectAsync(alice.Id, new CreateDirectRequest(bob.Id));

        for (var i = 1; i <= 5; i++)
            await svc.SendAsync(alice.Id, direct.Id, new SendMessageRequest($"msg {i}"));

        var page1 = await svc.GetHistoryAsync(bob.Id, direct.Id, null, null, 2);
        Assert.Equal(2, page1.Items.Count);
        Assert.True(page1.HasMore);
        Assert.Equal("msg 5", page1.Items[0].Content);
        Assert.NotNull(page1.NextCursor);

        var page2 = await svc.GetHistoryAsync(bob.Id, direct.Id, page1.NextCursor, null, 2);
        Assert.Equal("msg 3", page2.Items[0].Content);
        Assert.True(page2.HasMore);

        var page3 = await svc.GetHistoryAsync(bob.Id, direct.Id, page2.NextCursor, null, 2);
        Assert.Single(page3.Items);
        Assert.False(page3.HasMore);
    }

    [Fact]
    public async Task Only_sender_can_edit()
    {
        var (svc, convoSvc, users) = Create();
        var alice = new User("alice", "Alice", "alice@example.com", "HASHED");
        var bob = new User("bob", "Bob", "bob@example.com", "HASHED");
        await users.AddAsync(alice); await users.AddAsync(bob);
        var direct = await convoSvc.CreateDirectAsync(alice.Id, new CreateDirectRequest(bob.Id));
        var sent = await svc.SendAsync(alice.Id, direct.Id, new SendMessageRequest("hello"));

        await Assert.ThrowsAsync<ForbiddenAppException>(() =>
            svc.EditAsync(bob.Id, sent.Id, new EditMessageRequest("hijacked")));
    }

    [Fact]
    public async Task Delete_hides_content_but_keeps_row()
    {
        var (svc, convoSvc, users) = Create();
        var alice = new User("alice", "Alice", "alice@example.com", "HASHED");
        var bob = new User("bob", "Bob", "bob@example.com", "HASHED");
        await users.AddAsync(alice); await users.AddAsync(bob);
        var direct = await convoSvc.CreateDirectAsync(alice.Id, new CreateDirectRequest(bob.Id));
        var sent = await svc.SendAsync(alice.Id, direct.Id, new SendMessageRequest("oops"));

        await svc.DeleteAsync(alice.Id, sent.Id);

        var history = await svc.GetHistoryAsync(bob.Id, direct.Id, null, null, 10);
        var deleted = history.Items.First(x => x.Id == sent.Id);
        Assert.Null(deleted.Content);
        Assert.NotNull(deleted.DeletedAt);
    }

    [Fact]
    public async Task Reply_must_stay_in_same_conversation()
    {
        var (svc, convoSvc, users) = Create();
        var alice = new User("alice", "Alice", "alice@example.com", "HASHED");
        var bob = new User("bob", "Bob", "bob@example.com", "HASHED");
        var carol = new User("carol", "Carol", "carol@example.com", "HASHED");
        await users.AddAsync(alice); await users.AddAsync(bob); await users.AddAsync(carol);
        var ab = await convoSvc.CreateDirectAsync(alice.Id, new CreateDirectRequest(bob.Id));
        var ac = await convoSvc.CreateDirectAsync(alice.Id, new CreateDirectRequest(carol.Id));
        var first = await svc.SendAsync(alice.Id, ab.Id, new SendMessageRequest("hello"));

        await Assert.ThrowsAsync<ValidationAppException>(() =>
            svc.SendAsync(alice.Id, ac.Id, new SendMessageRequest("reply", null, first.Id)));
    }

    private static async Task<(Guid DirectId, MessageDto First, MessageDto Second)> SeedDirectAsync(
        MessageService svc, ConversationService convoSvc, FakeUserRepository users)
    {
        var alice = new User("alice", "Alice", "alice@example.com", "HASHED");
        var bob = new User("bob", "Bob", "bob@example.com", "HASHED");
        await users.AddAsync(alice); await users.AddAsync(bob);
        var direct = await convoSvc.CreateDirectAsync(alice.Id, new CreateDirectRequest(bob.Id));
        var first = await svc.SendAsync(alice.Id, direct.Id, new SendMessageRequest("one"));
        var second = await svc.SendAsync(alice.Id, direct.Id, new SendMessageRequest("two"));
        return (direct.Id, first, second);
    }

    [Fact]
    public async Task MarkAsRead_persists_receipt_and_advances_last_read()
    {
        var (svc, convoSvc, users) = Create();
        var (directId, first, second) = await SeedDirectAsync(svc, convoSvc, users);
        var bob = (await users.GetByUsernameAsync("bob"))!;

        var result = await svc.MarkAsReadAsync(bob.Id, directId, second.Id);

        Assert.True(result.IsNewActivity);
        Assert.Equal(second.Id, result.Receipt.MessageId);
        Assert.Equal(bob.Id, result.Receipt.UserId);

        var history = await svc.GetHistoryAsync(bob.Id, directId, null, null, 10);
        var read = history.Items.First(x => x.Id == second.Id);
        Assert.Contains(read.ReadBy, r => r.UserId == bob.Id);
        Assert.DoesNotContain(history.Items.First(x => x.Id == first.Id).ReadBy, r => r.UserId == bob.Id);
    }

    [Fact]
    public async Task MarkAsRead_by_nonmember_is_not_found()
    {
        var (svc, convoSvc, users) = Create();
        var (directId, _, second) = await SeedDirectAsync(svc, convoSvc, users);
        var carol = new User("carol", "Carol", "carol@example.com", "HASHED");
        await users.AddAsync(carol);

        await Assert.ThrowsAsync<NotFoundAppException>(() =>
            svc.MarkAsReadAsync(carol.Id, directId, second.Id));
    }

    [Fact]
    public async Task MarkAsRead_rejects_message_from_another_conversation()
    {
        var (svc, convoSvc, users) = Create();
        var (directId, _, _) = await SeedDirectAsync(svc, convoSvc, users);
        var bob = (await users.GetByUsernameAsync("bob"))!;
        var carol = new User("carol", "Carol", "carol@example.com", "HASHED");
        await users.AddAsync(carol);
        var other = await convoSvc.CreateDirectAsync(bob.Id, new CreateDirectRequest(carol.Id));
        var foreign = await svc.SendAsync(carol.Id, other.Id, new SendMessageRequest("hi"));

        await Assert.ThrowsAsync<ValidationAppException>(() =>
            svc.MarkAsReadAsync(bob.Id, directId, foreign.Id));
    }

    [Fact]
    public async Task Self_mark_is_acknowledged_without_row_or_broadcast_flag()
    {
        var (svc, convoSvc, users) = Create();
        var (directId, _, second) = await SeedDirectAsync(svc, convoSvc, users);
        var alice = (await users.GetByUsernameAsync("alice"))!;

        var result = await svc.MarkAsReadAsync(alice.Id, directId, second.Id);

        Assert.False(result.IsNewActivity);
        var history = await svc.GetHistoryAsync(alice.Id, directId, null, null, 10);
        Assert.DoesNotContain(history.Items.First(x => x.Id == second.Id).ReadBy, r => r.UserId == alice.Id);
    }

    [Fact]
    public async Task LastRead_never_regresses_on_stale_mark()
    {
        var convos = new FakeConversationRepository();
        var users = new FakeUserRepository();
        var opts = Options.Create(new ChatOptions());
        var svc = new MessageService(new FakeMessageRepository(), convos, new FakeReadReceiptRepository(), new FakeAttachmentRepository(), opts);
        var convoSvc = new ConversationService(convos, users, opts);

        var alice = new User("alice", "Alice", "alice@example.com", "HASHED");
        var bob = new User("bob", "Bob", "bob@example.com", "HASHED");
        await users.AddAsync(alice); await users.AddAsync(bob);
        var direct = await convoSvc.CreateDirectAsync(alice.Id, new CreateDirectRequest(bob.Id));
        var first = await svc.SendAsync(alice.Id, direct.Id, new SendMessageRequest("one"));
        var second = await svc.SendAsync(alice.Id, direct.Id, new SendMessageRequest("two"));

        await svc.MarkAsReadAsync(bob.Id, direct.Id, second.Id);
        await svc.MarkAsReadAsync(bob.Id, direct.Id, first.Id); // stale: receipts yes, position no

        var stored = await convos.GetByIdAsync(direct.Id);
        Assert.Equal(second.Id, stored!.Members.First(m => m.UserId == bob.Id).LastReadMessageId);
    }
}
