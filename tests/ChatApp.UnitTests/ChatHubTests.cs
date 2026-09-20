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
    public IClientProxy OthersInGroup(string groupName) => _default;
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

    public Task<MessageDto> SendAsync(Guid userId, Guid conversationId, SendMessageRequest request, CancellationToken ct = default)
        => Task.FromResult(OnSend!(userId, conversationId, request));
    public Task<MessageHistoryDto> GetHistoryAsync(Guid userId, Guid conversationId, Guid? before, Guid? after, int? limit, CancellationToken ct = default)
        => throw new NotImplementedException();
    public Task<MessageDto> EditAsync(Guid userId, Guid messageId, EditMessageRequest request, CancellationToken ct = default)
        => Task.FromResult(OnEdit!(userId, messageId, request));
    public Task<MessageDto> DeleteAsync(Guid userId, Guid messageId, CancellationToken ct = default)
        => Task.FromResult(OnDelete!(userId, messageId));
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

#endregion

public sealed class ChatHubTests
{
    private static readonly Guid AliceId = Guid.NewGuid();
    private static readonly Guid ConversationId = Guid.NewGuid();

    private static ClaimsPrincipal AlicePrincipal() => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, AliceId.ToString()), new Claim(ClaimTypes.Name, "alice")], "test"));

    private static ChatHub CreateHub(
        StubMessageService messages, StubConversationService conversations,
        out FakeHubCallerClients clients, out FakeGroupManager groups)
    {
        clients = new FakeHubCallerClients();
        groups = new FakeGroupManager();
        return new ChatHub(messages, conversations, NullLogger<ChatHub>.Instance)
        {
            Context = new FakeHubContext(AlicePrincipal()),
            Clients = clients,
            Groups = groups
        };
    }

    private static MessageDto SentMessage() => new(
        Guid.NewGuid(), ConversationId, AliceId, "hello", MessageType.Text,
        DateTimeOffset.UtcNow, null, null, null);

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
}
