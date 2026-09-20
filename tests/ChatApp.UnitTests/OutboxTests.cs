using ChatApp.Application.Abstractions;
using ChatApp.Application.Common;
using ChatApp.Application.Conversations;
using ChatApp.Application.Messages;
using ChatApp.Application.Outbox;
using ChatApp.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ChatApp.UnitTests;

internal sealed class FakeGroupPublisher : INotificationPublisher
{
    public List<(Guid ConversationId, string Method, string Payload)> Sent { get; } = [];
    public Func<int, Exception?>? Fault;
    private int _calls;

    public Task PublishToUserAsync(Guid userId, string method, object payload, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task PublishToConversationAsync(Guid conversationId, string method, string payloadJson, CancellationToken ct = default)
    {
        var ex = Fault?.Invoke(_calls++);
        if (ex is not null)
            throw ex;
        Sent.Add((conversationId, method, payloadJson));
        return Task.CompletedTask;
    }
}

public sealed class OutboxTests
{
    private static void SetPrivate(object target, string property, object? value)
        => target.GetType().GetProperty(property)!
            .GetSetMethod(nonPublic: true)!
            .Invoke(target, [value]);

    private static OutboxProcessor CreateProcessor(
        FakeOutboxRepository outbox, FakeGroupPublisher publisher, int maxAttempts = 3)
        => new(outbox, publisher,
            Options.Create(new OutboxOptions { MaxAttempts = maxAttempts, BatchSize = 50, RetentionHours = 24 }),
            NullLogger<OutboxProcessor>.Instance);

    private static (MessageService Msgs, ConversationService Convos, FakeUserRepository Users, FakeOutboxRepository Outbox) CreateMessaging()
    {
        var convos = new FakeConversationRepository();
        var msgs = new FakeMessageRepository();
        var users = new FakeUserRepository();
        var outbox = new FakeOutboxRepository();
        var opts = Options.Create(new ChatOptions());
        return (
            new MessageService(msgs, convos, new FakeReadReceiptRepository(), new FakeAttachmentRepository(),
                outbox, new NullNotificationService(), NullLogger<MessageService>.Instance, opts),
            new ConversationService(convos, users, new NullNotificationService(), NullLogger<ConversationService>.Instance, opts),
            users, outbox);
    }

    [Fact]
    public async Task Retry_with_same_key_returns_original_without_duplicate_row_or_event()
    {
        var (msgs, convos, users, outbox) = CreateMessaging();
        var alice = new User("alice", "Alice", "alice@example.com", "HASHED");
        var bob = new User("bob", "Bob", "bob@example.com", "HASHED");
        await users.AddAsync(alice); await users.AddAsync(bob);
        var direct = await convos.CreateDirectAsync(alice.Id, new CreateDirectRequest(bob.Id));
        var key = Guid.NewGuid();

        var first = await msgs.SendAsync(alice.Id, direct.Id, new SendMessageRequest("hi", null, null, null, key));
        var retry = await msgs.SendAsync(alice.Id, direct.Id, new SendMessageRequest("hi", null, null, null, key));

        Assert.Equal(first.Id, retry.Id);
        Assert.Single(outbox.Events); // no second outbox row -> no rebroadcast, no re-notify
    }

    [Fact]
    public async Task Same_key_in_another_conversation_is_rejected()
    {
        var (msgs, convos, users, _) = CreateMessaging();
        var alice = new User("alice", "Alice", "alice@example.com", "HASHED");
        var bob = new User("bob", "Bob", "bob@example.com", "HASHED");
        var carol = new User("carol", "Carol", "carol@example.com", "HASHED");
        await users.AddAsync(alice); await users.AddAsync(bob); await users.AddAsync(carol);
        var ab = await convos.CreateDirectAsync(alice.Id, new CreateDirectRequest(bob.Id));
        var ac = await convos.CreateDirectAsync(alice.Id, new CreateDirectRequest(carol.Id));
        var key = Guid.NewGuid();
        await msgs.SendAsync(alice.Id, ab.Id, new SendMessageRequest("hi", null, null, null, key));

        await Assert.ThrowsAsync<ChatApp.Application.Common.Exceptions.ValidationAppException>(() =>
            msgs.SendAsync(alice.Id, ac.Id, new SendMessageRequest("hi", null, null, null, key)));
    }

    [Fact]
    public async Task After_cursor_sync_returns_only_newer_messages()
    {
        var (msgs, convos, users, _) = CreateMessaging();
        var alice = new User("alice", "Alice", "alice@example.com", "HASHED");
        var bob = new User("bob", "Bob", "bob@example.com", "HASHED");
        await users.AddAsync(alice); await users.AddAsync(bob);
        var direct = await convos.CreateDirectAsync(alice.Id, new CreateDirectRequest(bob.Id));
        var first = await msgs.SendAsync(alice.Id, direct.Id, new SendMessageRequest("one"));

        // Client reconnects knowing only `first`: sync catches it up.
        await msgs.SendAsync(alice.Id, direct.Id, new SendMessageRequest("two"));
        await msgs.SendAsync(alice.Id, direct.Id, new SendMessageRequest("three"));

        var sync = await msgs.GetHistoryAsync(bob.Id, direct.Id, null, first.Id, 50);
        Assert.Equal(2, sync.Items.Count);
        Assert.False(sync.HasMore);
    }

    [Fact]
    public async Task Dispatch_publishes_pending_in_order_and_marks_processed()
    {
        var outbox = new FakeOutboxRepository();
        var publisher = new FakeGroupPublisher();
        var convId = Guid.NewGuid();
        outbox.Events.Add(new OutboxEvent("MessageReceived", convId, """{"a":1}"""));
        outbox.Events.Add(new OutboxEvent("MessageReceived", convId, """{"a":2}"""));

        var result = await CreateProcessor(outbox, publisher).ProcessBatchAsync();

        Assert.Equal(2, result.Processed);
        Assert.Equal(2, publisher.Sent.Count);
        Assert.Equal("""{"a":1}""", publisher.Sent[0].Payload);
        Assert.Equal(convId, publisher.Sent[0].ConversationId);
        Assert.All(outbox.Events, e => Assert.NotNull(e.ProcessedAt));
    }

    [Fact]
    public async Task Failed_publish_retries_with_backoff_then_dead_letters()
    {
        var outbox = new FakeOutboxRepository();
        var publisher = new FakeGroupPublisher { Fault = _ => new InvalidOperationException("bus down") };
        outbox.Events.Add(new OutboxEvent("MessageReceived", Guid.NewGuid(), "{}"));
        var processor = CreateProcessor(outbox, publisher, maxAttempts: 2);

        var before = DateTimeOffset.UtcNow;
        var r1 = await processor.ProcessBatchAsync();
        Assert.Equal(1, r1.Failed);
        var evt = Assert.Single(outbox.Events);
        Assert.Equal(1, evt.Attempts);
        Assert.Null(evt.ProcessedAt);
        Assert.True(evt.NextAttemptAt > before);

        // Not due yet: second tick skips it.
        var r2 = await processor.ProcessBatchAsync();
        Assert.Equal(0, r2.Processed + r2.Failed);

        // Attempts exhausted -> dead-lettered (processed flag set, error kept).
        SetPrivate(evt, "NextAttemptAt", DateTimeOffset.UtcNow.AddSeconds(-1));
        var r3 = await processor.ProcessBatchAsync();
        Assert.Equal(1, r3.DeadLettered);
        Assert.NotNull(evt.ProcessedAt);
        Assert.Contains("bus down", evt.Error);
    }

    [Fact]
    public async Task Cleanup_deletes_old_processed_rows()
    {
        var outbox = new FakeOutboxRepository();
        var publisher = new FakeGroupPublisher();
        var old = new OutboxEvent("MessageReceived", Guid.NewGuid(), "{}");
        old.MarkProcessed();
        SetPrivate(old, "ProcessedAt", DateTimeOffset.UtcNow.AddHours(-25));
        var fresh = new OutboxEvent("MessageReceived", Guid.NewGuid(), "{}");
        fresh.MarkProcessed();
        outbox.Events.Add(old);
        outbox.Events.Add(fresh);

        var result = await CreateProcessor(outbox, publisher).ProcessBatchAsync();

        Assert.Equal(1, result.CleanedUp);
        Assert.DoesNotContain(outbox.Events, e => e.Id == old.Id);
        Assert.Contains(outbox.Events, e => e.Id == fresh.Id);
    }
}
