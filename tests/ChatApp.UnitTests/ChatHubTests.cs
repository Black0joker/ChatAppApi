using System.Security.Claims;
using ChatApp.Api.Hubs;
using ChatApp.Application.Abstractions;
using ChatApp.Application.Common.Exceptions;
using ChatApp.Domain.Enums;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;

namespace ChatApp.UnitTests;

#region Fakes

internal sealed class RecordingClientProxy : IClientProxy
{
    public List<(string Method, object?[] Args)> Sent { get; } = [];
    public Task SendCoreAsync(string method, object?[] args, CancellationToken ct = default)
    {
        Sent.Add((method, args));
        return Task.CompletedTask;
    }
}

internal sealed class FakeHubCallerClients : IHubCallerClients
{
    private readonly RecordingClientProxy _default = new();
    public Dictionary<string, RecordingClientProxy> GroupProxies { get; } = new();
    public Dictionary<string, RecordingClientProxy> OthersGroupProxies { get; } = new();

    public IClientProxy All => _default;
    public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => _default;
    public IClientProxy Client(string connectionId) => _default;
    public IClientProxy Clients(IReadOnlyList<string> connectionIds) => _default;
    public IClientProxy Group(string groupName)
        => GroupProxies.TryGetValue(groupName, out var p) ? p : GroupProxies[groupName] = new RecordingClientProxy();
    public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => _default;
    public IClientProxy Groups(IReadOnlyList<string> groupNames) => _default;
    public IClientProxy User(string userId) => _default;
    public IClientProxy Users(IReadOnlyList<string> userIds) => _default;
    public IClientProxy Caller => _default;
    public IClientProxy Others => _default;
    public IClientProxy OthersInGroup(string groupName)
        => OthersGroupProxies.TryGetValue(groupName, out var p) ? p : OthersGroupProxies[groupName] = new RecordingClientProxy();
}

internal sealed class FakeGroupManager : IGroupManager
{
    public List<(string ConnectionId, string Group)> Added { get; } = [];
    public List<(string ConnectionId, string Group)> Removed { get; } = [];
    public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken ct = default)
    {
        Added.Add((connectionId, groupName));
        return Task.CompletedTask;
    }
    public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken ct = default)
    {
        Removed.Add((connectionId, groupName));
        return Task.CompletedTask;
    }
}

internal sealed class FakeHubContext : HubCallerContext
{
    public FakeHubContext(ClaimsPrincipal user, string connectionId = "conn-1")
    {
        ConnectionId = connectionId;
        User = user;
    }

    public override string ConnectionId { get; }
    public override ClaimsPrincipal? User { get; }
    public override string? UserIdentifier => User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    public override IFeatureCollection Features { get; } = new FeatureCollection();
    public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
    public override CancellationToken ConnectionAborted => CancellationToken.None;
    public override void Abort() { }
}

internal sealed class StubMessageService : IMessageService
{
    public Func<Guid, Guid, SendMessageRequest, MessageDto>? OnSend;
    public Func<Guid, Guid, EditMessageRequest, MessageDto>? OnEdit;
    public Func<Guid, Guid, MessageDto>? OnDelete;
    public Func<Guid, Guid, Guid, MarkAsReadResult>? OnMarkAsRead;

    public Task<MessageDto> SendAsync(Guid userId, Guid conversationId, SendMessageRequest request, CancellationToken ct = default)
        => Task.FromResult(OnSend!(userId, conversationId, request));
    public Task<MessageHistoryDto> GetHistoryAsync(Guid userId, Guid conversationId, Guid? before, Guid? after, int? limit, CancellationToken ct = default)
        => throw new NotImplementedException();
    public Task<MessageDto> EditAsync(Guid userId, Guid messageId, EditMessageRequest request, CancellationToken ct = default)
        => Task.FromResult(OnEdit!(userId, messageId, request));
    public Task<MessageDto> DeleteAsync(Guid userId, Guid messageId, CancellationToken ct = default)
        => Task.FromResult(OnDelete!(userId, messageId));
    public Task<MarkAsReadResult> MarkAsReadAsync(Guid userId, Guid conversationId, Guid messageId, CancellationToken ct = default)
        => Task.FromResult(OnMarkAsRead!(userId, conversationId, messageId));
}

internal sealed class StubConversationService : IConversationService
{
    public Func<Guid, Guid, ConversationDto>? OnGetById;
    public Task<ConversationDto> CreateDirectAsync(Guid userId, CreateDirectRequest request, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<ConversationDto> CreateGroupAsync(Guid userId, CreateGroupRequest request, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<IReadOnlyList<ConversationDto>> GetMineAsync(Guid userId, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<ConversationDto> GetByIdAsync(Guid userId, Guid conversationId, CancellationToken ct = default)
        => Task.FromResult(OnGetById!(userId, conversationId));
    public Task<ConversationDto> AddMemberAsync(Guid userId, Guid conversationId, AddMemberRequest request, CancellationToken ct = default) => throw new NotImplementedException();
    public Task RemoveMemberAsync(Guid userId, Guid conversationId, Guid targetUserId, CancellationToken ct = default) => throw new NotImplementedException();
}

internal sealed class StubPresenceService : IPresenceService
{
    public List<(Guid UserId, string ConnectionId)> Connected { get; } = [];
    public List<(Guid UserId, string ConnectionId)> Disconnected { get; } = [];

    public Task<bool> UserConnectedAsync(Guid userId, string connectionId, CancellationToken ct = default)
    {
        Connected.Add((userId, connectionId));
        return Task.FromResult(true);
    }

    public Task<bool> UserDisconnectedAsync(Guid userId, string connectionId, CancellationToken ct = default)
    {
        Disconnected.Add((userId, connectionId));
        return Task.FromResult(true);
    }

    public Task<bool> HeartbeatAsync(Guid userId, string connectionId, CancellationToken ct = default)
        => Task.FromResult(true);

    public Task<int> SweepStaleConnectionsAsync(CancellationToken ct = default) => Task.FromResult(0);

    public Task<bool> IsOnlineAsync(Guid userId, CancellationToken ct = default) => Task.FromResult(true);
}

#endregion

public sealed class ChatHubTests
{
    private static readonly Guid AliceId = Guid.NewGuid();
    private static readonly Guid ConversationId = Guid.NewGuid();

    private static ClaimsPrincipal AlicePrincipal() => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, AliceId.ToString()), new Claim(ClaimTypes.Name, "alice")], "test"));

    private static ChatHub CreateHub(
        StubMessageService messages, StubConversationService conversations,
        out FakeHubCallerClients clients, out FakeGroupManager groups,
        StubPresenceService? presence = null)
    {
        clients = new FakeHubCallerClients();
        groups = new FakeGroupManager();
        return new ChatHub(messages, conversations, presence ?? new StubPresenceService(), NullLogger<ChatHub>.Instance)
        {
            Context = new FakeHubContext(AlicePrincipal()),
            Clients = clients,
            Groups = groups
        };
    }

    private static MessageDto SentMessage() => new(
        Guid.NewGuid(), ConversationId, AliceId, "hello", MessageType.Text,
        DateTimeOffset.UtcNow, null, null, null, []);

    [Fact]
    public async Task SendMessage_persists_with_token_identity_then_broadcasts()
    {
        Guid seenUser = Guid.Empty;
        var stub = new StubMessageService { OnSend = (u, c, r) => { seenUser = u; return SentMessage(); } };
        var hub = CreateHub(stub, new StubConversationService(), out var clients, out _);

        // Note: the payload carries NO user id — identity can only come from the token.
        var result = await hub.SendMessage(new SendMessagePayload(ConversationId, "hello"));

        Assert.Equal(AliceId, seenUser);
        Assert.Equal(AliceId, result.SenderId);
        var group = $"conversation:{ConversationId}";
        Assert.True(clients.GroupProxies.ContainsKey(group));
        var sent = Assert.Single(clients.GroupProxies[group].Sent);
        Assert.Equal(ChatHubEvents.MessageReceived, sent.Method);
        var json = System.Text.Json.JsonSerializer.Serialize(sent.Args[0]);
        Assert.Contains(AliceId.ToString(), json);
        Assert.Contains(ConversationId.ToString(), json);
    }

    [Fact]
    public async Task JoinConversation_member_joins_group()
    {
        var convos = new StubConversationService
        {
            OnGetById = (u, c) => new ConversationDto(c, ConversationType.Direct, null, u, DateTimeOffset.UtcNow, null, [])
        };
        var hub = CreateHub(new StubMessageService(), convos, out _, out var groups);

        await hub.JoinConversation(ConversationId);

        Assert.Contains(groups.Added, x => x is { ConnectionId: "conn-1" } && x.Group == $"conversation:{ConversationId}");
    }

    [Fact]
    public async Task JoinConversation_nonmember_throws_and_never_joins()
    {
        var convos = new StubConversationService
        {
            OnGetById = (u, c) => throw new NotFoundAppException("Conversation not found.")
        };
        var hub = CreateHub(new StubMessageService(), convos, out _, out var groups);

        var ex = await Assert.ThrowsAsync<HubException>(() => hub.JoinConversation(ConversationId));

        Assert.Equal("Conversation not found.", ex.Message);
        Assert.Empty(groups.Added);
    }

    [Fact]
    public async Task LeaveConversation_removes_from_group()
    {
        var hub = CreateHub(new StubMessageService(), new StubConversationService(), out _, out var groups);

        await hub.LeaveConversation(ConversationId);

        Assert.Contains(groups.Removed, x => x is { ConnectionId: "conn-1" } && x.Group == $"conversation:{ConversationId}");
    }

    [Fact]
    public async Task EditMessage_broadcasts_updated_event()
    {
        var edited = SentMessage() with { Content = "edited", EditedAt = DateTimeOffset.UtcNow };
        var stub = new StubMessageService { OnEdit = (u, m, r) => edited };
        var hub = CreateHub(stub, new StubConversationService(), out var clients, out _);

        await hub.EditMessage(edited.Id, "edited");

        var sent = Assert.Single(clients.GroupProxies[$"conversation:{ConversationId}"].Sent);
        Assert.Equal(ChatHubEvents.MessageUpdated, sent.Method);
        Assert.Contains("edited", System.Text.Json.JsonSerializer.Serialize(sent.Args[0]));
    }

    [Fact]
    public async Task DeleteMessage_broadcasts_deleted_event()
    {
        var deleted = SentMessage() with { Content = null, DeletedAt = DateTimeOffset.UtcNow };
        var stub = new StubMessageService { OnDelete = (u, m) => deleted };
        var hub = CreateHub(stub, new StubConversationService(), out var clients, out _);

        await hub.DeleteMessage(deleted.Id);

        var sent = Assert.Single(clients.GroupProxies[$"conversation:{ConversationId}"].Sent);
        Assert.Equal(ChatHubEvents.MessageDeleted, sent.Method);
    }

    [Fact]
    public async Task Unexpected_errors_are_masked()
    {
        var stub = new StubMessageService
        {
            OnSend = (u, c, r) => throw new InvalidOperationException("SqlException: dbo.Exploded")
        };
        var hub = CreateHub(stub, new StubConversationService(), out var clients, out _);

        var ex = await Assert.ThrowsAsync<HubException>(() =>
            hub.SendMessage(new SendMessagePayload(ConversationId, "hi")));

        Assert.Equal("An unexpected error occurred.", ex.Message);
        Assert.False(clients.GroupProxies.ContainsKey($"conversation:{ConversationId}")); // never broadcast on failure
    }

    [Fact]
    public async Task Connect_registers_presence()
    {
        var presence = new StubPresenceService();
        var hub = CreateHub(new StubMessageService(), new StubConversationService(), out _, out _, presence);

        await hub.OnConnectedAsync();

        Assert.Contains(presence.Connected, x => x.UserId == AliceId && x.ConnectionId == "conn-1");
    }

    [Fact]
    public async Task Disconnect_unregisters_presence()
    {
        var presence = new StubPresenceService();
        var hub = CreateHub(new StubMessageService(), new StubConversationService(), out _, out _, presence);

        await hub.OnDisconnectedAsync(null);

        Assert.Contains(presence.Disconnected, x => x.UserId == AliceId && x.ConnectionId == "conn-1");
    }

    [Fact]
    public async Task Heartbeat_returns_false_for_anonymous_callers()
    {
        var hub = CreateHub(new StubMessageService(), new StubConversationService(), out _, out _);
        hub.Context = new FakeHubContext(new ClaimsPrincipal(new ClaimsIdentity()));

        Assert.False(await hub.Heartbeat());
    }

    [Fact]
    public async Task MarkAsRead_broadcasts_read_event_on_new_activity()
    {
        var sent = SentMessage();
        var stub = new StubMessageService
        {
            OnMarkAsRead = (u, c, m) => new MarkAsReadResult(
                new ReadReceiptDto(m, u, DateTimeOffset.UtcNow), true)
        };
        var hub = CreateHub(stub, new StubConversationService(), out var clients, out _);

        var receipt = await hub.MarkAsRead(ConversationId, sent.Id);

        Assert.Equal(AliceId, receipt.UserId);
        var group = $"conversation:{ConversationId}";
        var evt = Assert.Single(clients.GroupProxies[group].Sent);
        Assert.Equal(ChatHubEvents.MessageRead, evt.Method);
        var json = System.Text.Json.JsonSerializer.Serialize(evt.Args[0]);
        Assert.Contains(sent.Id.ToString(), json);
        Assert.Contains(AliceId.ToString(), json);
    }

    [Fact]
    public async Task MarkAsRead_self_mark_acknowledged_without_broadcast()
    {
        var sent = SentMessage();
        var stub = new StubMessageService
        {
            OnMarkAsRead = (u, c, m) => new MarkAsReadResult(
                new ReadReceiptDto(m, u, DateTimeOffset.UtcNow), false)
        };
        var hub = CreateHub(stub, new StubConversationService(), out var clients, out _);

        await hub.MarkAsRead(ConversationId, sent.Id);

        Assert.False(clients.GroupProxies.ContainsKey($"conversation:{ConversationId}"));
    }

    [Fact]
    public async Task MarkAsRead_by_nonmember_throws()
    {
        var stub = new StubMessageService
        {
            OnMarkAsRead = (u, c, m) => throw new NotFoundAppException("Conversation not found.")
        };
        var hub = CreateHub(stub, new StubConversationService(), out var clients, out _);

        var ex = await Assert.ThrowsAsync<HubException>(() => hub.MarkAsRead(ConversationId, Guid.NewGuid()));

        Assert.Equal("Conversation not found.", ex.Message);
        Assert.False(clients.GroupProxies.ContainsKey($"conversation:{ConversationId}"));
    }

    [Fact]
    public async Task TypingStarted_broadcasts_to_group_others_with_token_identity()
    {
        var convos = new StubConversationService
        {
            OnGetById = (u, c) => new ConversationDto(c, ConversationType.Direct, null, u, DateTimeOffset.UtcNow, null, [])
        };
        var hub = CreateHub(new StubMessageService(), convos, out var clients, out _);

        await hub.TypingStarted(ConversationId);

        // Sender excluded: nothing on Group (all) or Caller, only OthersInGroup.
        Assert.False(clients.GroupProxies.ContainsKey($"conversation:{ConversationId}"));
        var sent = Assert.Single(clients.OthersGroupProxies[$"conversation:{ConversationId}"].Sent);
        Assert.Equal(ChatHubEvents.TypingStarted, sent.Method);
        var json = System.Text.Json.JsonSerializer.Serialize(sent.Args[0]);
        Assert.Contains(ConversationId.ToString(), json);
        Assert.Contains(AliceId.ToString(), json);
    }

    [Fact]
    public async Task TypingStopped_broadcasts_and_nonmember_is_rejected()
    {
        var convos = new StubConversationService
        {
            OnGetById = (u, c) => new ConversationDto(c, ConversationType.Direct, null, u, DateTimeOffset.UtcNow, null, [])
        };
        var hub = CreateHub(new StubMessageService(), convos, out var clients, out _);

        await hub.TypingStopped(ConversationId);

        var sent = Assert.Single(clients.OthersGroupProxies[$"conversation:{ConversationId}"].Sent);
        Assert.Equal(ChatHubEvents.TypingStopped, sent.Method);
    }

    [Fact]
    public async Task Typing_by_nonmember_throws_and_broadcasts_nothing()
    {
        var convos = new StubConversationService
        {
            OnGetById = (u, c) => throw new NotFoundAppException("Conversation not found.")
        };
        var hub = CreateHub(new StubMessageService(), convos, out var clients, out _);

        var ex = await Assert.ThrowsAsync<HubException>(() => hub.TypingStarted(ConversationId));

        Assert.Equal("Conversation not found.", ex.Message);
        Assert.Empty(clients.OthersGroupProxies);
    }
}
