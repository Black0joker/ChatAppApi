using ChatApp.Application.Abstractions;
using ChatApp.Application.Attachments;
using ChatApp.Application.Common;
using ChatApp.Application.Common.Exceptions;
using ChatApp.Application.Conversations;
using ChatApp.Application.Messages;
using ChatApp.Domain.Entities;
using ChatApp.Infrastructure.Storage;
using Microsoft.Extensions.Options;

namespace ChatApp.UnitTests;

internal sealed class FakeFileStorage : IFileStorage
{
    public readonly Dictionary<string, byte[]> Files = new();

    public Task<StoredFile> SaveAsync(Stream content, string storageKeySuffix, CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        content.CopyTo(ms);
        var key = $"test/{Guid.NewGuid():N}{storageKeySuffix}";
        Files[key] = ms.ToArray();
        return Task.FromResult(new StoredFile(key, ms.Length));
    }

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken ct = default)
        => Task.FromResult<Stream>(new MemoryStream(Files[storageKey]));

    public Task DeleteAsync(string storageKey, CancellationToken ct = default)
    {
        Files.Remove(storageKey);
        return Task.CompletedTask;
    }
}

public sealed class AttachmentServiceTests
{
    // 1x1 transparent PNG.
    private static readonly byte[] PngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static (AttachmentService Attach, MessageService Msgs, ConversationService Convos,
        FakeUserRepository Users, FakeFileStorage Storage) Create(long maxSize = 1024 * 1024)
    {
        var convos = new FakeConversationRepository();
        var msgs = new FakeMessageRepository();
        var users = new FakeUserRepository();
        var receipts = new FakeReadReceiptRepository();
        var attachments = new FakeAttachmentRepository();
        var storage = new FakeFileStorage();
        var opts = Options.Create(new ChatOptions { MaxAttachmentSize = maxSize });
        return (
            new AttachmentService(attachments, convos, msgs, storage, new SignatureProbe(), opts),
            new MessageService(msgs, convos, receipts, attachments, opts),
            new ConversationService(convos, users, opts),
            users,
            storage);
    }

    private static async Task<(User Alice, User Bob, Guid DirectId)> SeedDirectAsync(
        ConversationService convos, FakeUserRepository users)
    {
        var alice = new User("alice", "Alice", "alice@example.com", "HASHED");
        var bob = new User("bob", "Bob", "bob@example.com", "HASHED");
        await users.AddAsync(alice); await users.AddAsync(bob);
        var direct = await convos.CreateDirectAsync(alice.Id, new CreateDirectRequest(bob.Id));
        return (alice, bob, direct.Id);
    }

    [Fact]
    public async Task Upload_valid_png_succeeds_with_server_detected_type()
    {
        var (attach, _, _, users, _) = Create();
        var alice = new User("alice", "Alice", "alice@example.com", "HASHED");
        await users.AddAsync(alice);

        var result = await attach.UploadAsync(alice.Id, "photo.png", new MemoryStream(PngBytes), PngBytes.Length);

        Assert.Equal("image/png", result.ContentType);
        Assert.Equal(PngBytes.Length, result.Size);
    }

    [Fact]
    public async Task Upload_exe_renamed_to_png_is_rejected()
    {
        var (attach, _, _, users, _) = Create();
        var alice = new User("alice", "Alice", "alice@example.com", "HASHED");
        await users.AddAsync(alice);
        var exe = new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x01, 0x02, 0x03, 0x04 };

        await Assert.ThrowsAsync<ValidationAppException>(() =>
            attach.UploadAsync(alice.Id, "evil.png", new MemoryStream(exe), exe.Length));
    }

    [Fact]
    public async Task Upload_disallowed_extension_is_rejected()
    {
        var (attach, _, _, users, _) = Create();
        var alice = new User("alice", "Alice", "alice@example.com", "HASHED");
        await users.AddAsync(alice);

        await Assert.ThrowsAsync<ValidationAppException>(() =>
            attach.UploadAsync(alice.Id, "run.exe", new MemoryStream(PngBytes), PngBytes.Length));
    }

    [Fact]
    public async Task Upload_oversize_is_rejected()
    {
        var (attach, _, _, users, _) = Create(maxSize: 10);
        var alice = new User("alice", "Alice", "alice@example.com", "HASHED");
        await users.AddAsync(alice);

        await Assert.ThrowsAsync<ValidationAppException>(() =>
            attach.UploadAsync(alice.Id, "photo.png", new MemoryStream(PngBytes), PngBytes.Length));
    }

    [Fact]
    public async Task Send_with_attachments_links_them_to_the_message()
    {
        var (attach, msgs, convos, users, _) = Create();
        var (alice, _, directId) = await SeedDirectAsync(convos, users);
        var up = await attach.UploadAsync(alice.Id, "photo.png", new MemoryStream(PngBytes), PngBytes.Length);

        var sent = await msgs.SendAsync(alice.Id, directId,
            new SendMessageRequest("see this", null, null, [up.Id]));

        Assert.Contains(sent.Attachments, a => a.Id == up.Id && a.ContentType == "image/png");

        var history = await msgs.GetHistoryAsync(alice.Id, directId, null, null, 10);
        Assert.Contains(history.Items.First(m => m.Id == sent.Id).Attachments, a => a.Id == up.Id);
    }

    [Fact]
    public async Task Send_with_another_users_upload_is_forbidden()
    {
        var (attach, msgs, convos, users, _) = Create();
        var (alice, bob, directId) = await SeedDirectAsync(convos, users);
        var up = await attach.UploadAsync(alice.Id, "photo.png", new MemoryStream(PngBytes), PngBytes.Length);

        await Assert.ThrowsAsync<ForbiddenAppException>(() =>
            msgs.SendAsync(bob.Id, directId, new SendMessageRequest("mine?", null, null, [up.Id])));
    }

    [Fact]
    public async Task Attachment_cannot_be_reused_across_messages()
    {
        var (attach, msgs, convos, users, _) = Create();
        var (alice, _, directId) = await SeedDirectAsync(convos, users);
        var up = await attach.UploadAsync(alice.Id, "photo.png", new MemoryStream(PngBytes), PngBytes.Length);
        await msgs.SendAsync(alice.Id, directId, new SendMessageRequest("first", null, null, [up.Id]));

        await Assert.ThrowsAsync<ConflictAppException>(() =>
            msgs.SendAsync(alice.Id, directId, new SendMessageRequest("second", null, null, [up.Id])));
    }

    [Fact]
    public async Task Download_is_owner_only_until_attached_then_members()
    {
        var (attach, msgs, convos, users, _) = Create();
        var (alice, bob, directId) = await SeedDirectAsync(convos, users);
        var carol = new User("carol", "Carol", "carol@example.com", "HASHED");
        await users.AddAsync(carol);
        var up = await attach.UploadAsync(alice.Id, "photo.png", new MemoryStream(PngBytes), PngBytes.Length);

        // Unattached: invisible to others.
        await Assert.ThrowsAsync<NotFoundAppException>(() => attach.GetDownloadAsync(bob.Id, up.Id));

        await msgs.SendAsync(alice.Id, directId, new SendMessageRequest("see this", null, null, [up.Id]));

        // Attached: member can download, outsider cannot.
        await using (var s = (await attach.GetDownloadAsync(bob.Id, up.Id)).Content)
            Assert.Equal(PngBytes.Length, s.Length);
        await Assert.ThrowsAsync<NotFoundAppException>(() => attach.GetDownloadAsync(carol.Id, up.Id));
    }

    [Fact]
    public async Task Delete_attached_is_conflict_unattached_ok()
    {
        var (attach, msgs, convos, users, storage) = Create();
        var (alice, _, directId) = await SeedDirectAsync(convos, users);
        var attached = await attach.UploadAsync(alice.Id, "a.png", new MemoryStream(PngBytes), PngBytes.Length);
        var loose = await attach.UploadAsync(alice.Id, "b.png", new MemoryStream(PngBytes), PngBytes.Length);
        await msgs.SendAsync(alice.Id, directId, new SendMessageRequest("see this", null, null, [attached.Id]));

        await Assert.ThrowsAsync<ConflictAppException>(() => attach.DeleteAsync(alice.Id, attached.Id));

        await attach.DeleteAsync(alice.Id, loose.Id);
        Assert.Single(storage.Files); // the attached file's bytes remain
    }
}
