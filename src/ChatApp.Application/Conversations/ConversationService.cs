using ChatApp.Application.Abstractions;
using ChatApp.Application.Common;
using ChatApp.Application.Common.Exceptions;
using ChatApp.Domain.Entities;
using ChatApp.Domain.Enums;
using Microsoft.Extensions.Options;

namespace ChatApp.Application.Conversations;

public sealed class ConversationService(
    IConversationRepository conversations,
    IUserRepository users,
    IOptions<ChatOptions> chatOptions) : IConversationService
{
    private readonly ChatOptions _chat = chatOptions.Value;

    public async Task<ConversationDto> CreateDirectAsync(Guid userId, CreateDirectRequest request, CancellationToken ct = default)
    {
        if (request.OtherUserId == Guid.Empty || request.OtherUserId == userId)
            throw new ValidationAppException("A direct conversation requires a different user.");

        var other = await users.GetByIdAsync(request.OtherUserId, ct)
            ?? throw new NotFoundAppException("User not found.");

        // Idempotent: exactly one direct conversation per pair.
        var existing = await conversations.GetDirectBetweenAsync(userId, other.Id, ct);
        if (existing is not null)
            return ToDto(existing, await users.GetByIdsAsync(MemberIds(existing), ct));

        var conversation = new Conversation(ConversationType.Direct, null, userId);
        conversation.AddMember(userId, MemberRole.Member);
        conversation.AddMember(other.Id, MemberRole.Member);

        await conversations.AddAsync(conversation, ct);
        await conversations.SaveChangesAsync(ct);
        return ToDto(conversation, await users.GetByIdsAsync(MemberIds(conversation), ct));
    }

    public async Task<ConversationDto> CreateGroupAsync(Guid userId, CreateGroupRequest request, CancellationToken ct = default)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
            throw new ValidationAppException("Group name must be between 1 and 100 characters.");

        var memberIds = request.MemberIds.Distinct().Where(x => x != userId && x != Guid.Empty).ToList();
        if (memberIds.Count + 1 > _chat.MaxGroupMembers)
            throw new ValidationAppException($"Group cannot exceed {_chat.MaxGroupMembers} members.");

        // Single round-trip existence check instead of one query per invited member.
        var found = await users.GetByIdsAsync(memberIds.Append(userId), ct);
        var missing = memberIds.Where(id => !found.ContainsKey(id)).ToList();
        if (missing.Count > 0)
            throw new NotFoundAppException($"Users not found: {string.Join(", ", missing)}.");

        var conversation = new Conversation(ConversationType.Group, name, userId);
        conversation.AddMember(userId, MemberRole.Owner);
        foreach (var id in memberIds)
            conversation.AddMember(id, MemberRole.Member);

        await conversations.AddAsync(conversation, ct);
        await conversations.SaveChangesAsync(ct);
        return ToDto(conversation, found);
    }

    public async Task<IReadOnlyList<ConversationDto>> GetMineAsync(Guid userId, CancellationToken ct = default)
    {
        var list = await conversations.GetForUserAsync(userId, ct);
        // One batched user fetch for ALL conversations: 2 queries total
        // instead of M conversations x N members.
        var lookup = await users.GetByIdsAsync(list.SelectMany(MemberIds), ct);
        return list.Select(c => ToDto(c, lookup)).ToList();
    }

    public async Task<ConversationDto> GetByIdAsync(Guid userId, Guid conversationId, CancellationToken ct = default)
    {
        var conversation = await RequireMemberAsync(userId, conversationId, ct);
        return ToDto(conversation, await users.GetByIdsAsync(MemberIds(conversation), ct));
    }

    public async Task<ConversationDto> AddMemberAsync(Guid userId, Guid conversationId, AddMemberRequest request, CancellationToken ct = default)
    {
        var conversation = await RequireMemberAsync(userId, conversationId, ct);
        if (conversation.Type != ConversationType.Group)
            throw new ValidationAppException("Direct conversations do not support member management.");

        var requester = ActiveMember(conversation, userId)!;
        if (requester.Role is not (MemberRole.Owner or MemberRole.Admin))
            throw new ForbiddenAppException("Only group owners or admins can add members.");

        var role = request.Role ?? MemberRole.Member;
        if (role == MemberRole.Owner)
            throw new ValidationAppException("Ownership cannot be granted this way.");
        if (role == MemberRole.Admin && requester.Role != MemberRole.Owner)
            throw new ForbiddenAppException("Only the group owner can promote admins.");

        if (request.UserId == Guid.Empty)
            throw new ValidationAppException("A valid user id is required.");
        if (await users.GetByIdAsync(request.UserId, ct) is null)
            throw new NotFoundAppException("User not found.");

        var existing = conversation.Members.FirstOrDefault(m => m.UserId == request.UserId);
        if (existing is not null)
        {
            if (existing.IsActive)
            {
                // Role change on an active member (e.g. promote to admin).
                if (existing.Role == role)
                    throw new ConflictAppException("User is already a member.");
                if (existing.Role != MemberRole.Member && requester.Role != MemberRole.Owner)
                    throw new ForbiddenAppException("Only the group owner can change an admin's role.");
                existing.SetRole(role);
            }
            else
            {
                existing.Rejoin(role); // re-activate a previously left member
            }
        }
        else
        {
            if (conversation.Members.Count(m => m.IsActive) >= _chat.MaxGroupMembers)
                throw new ValidationAppException($"Group cannot exceed {_chat.MaxGroupMembers} members.");
            conversation.AddMember(request.UserId, role);
        }

        await conversations.SaveChangesAsync(ct);
        return ToDto(conversation, await users.GetByIdsAsync(MemberIds(conversation), ct));
    }

    public async Task RemoveMemberAsync(Guid userId, Guid conversationId, Guid targetUserId, CancellationToken ct = default)
    {
        var conversation = await RequireMemberAsync(userId, conversationId, ct);
        if (conversation.Type != ConversationType.Group)
            throw new ValidationAppException("Direct conversations do not support member management.");

        var target = conversation.Members.FirstOrDefault(m => m.UserId == targetUserId && m.IsActive)
            ?? throw new NotFoundAppException("Member not found.");

        if (targetUserId == userId)
        {
            // Leaving: an owner must hand over ownership first (auto-promote longest-standing admin/member).
            if (target.Role == MemberRole.Owner)
            {
                var successor = conversation.Members
                    .Where(m => m.IsActive && m.UserId != userId)
                    .OrderByDescending(m => m.Role == MemberRole.Admin)
                    .ThenBy(m => m.JoinedAt)
                    .FirstOrDefault()
                    ?? throw new ValidationAppException("The last remaining member cannot leave; delete rules arrive in a later phase.");
                successor.SetRole(MemberRole.Owner);
            }
            target.Leave();
        }
        else
        {
            var requester = ActiveMember(conversation, userId)!;
            if (requester.Role == MemberRole.Member)
                throw new ForbiddenAppException("Only group owners or admins can remove members.");
            if (target.Role != MemberRole.Member && requester.Role != MemberRole.Owner)
                throw new ForbiddenAppException("Only the group owner can remove admins or owners.");
            target.Leave();
        }

        await conversations.SaveChangesAsync(ct);
    }

    // Membership is the authorization gate (PLAN §31): non-members see 404, never 403,
    // so conversation existence cannot be probed.
    private async Task<Conversation> RequireMemberAsync(Guid userId, Guid conversationId, CancellationToken ct)
    {
        var conversation = await conversations.GetByIdAsync(conversationId, ct)
            ?? throw new NotFoundAppException("Conversation not found.");
        if (ActiveMember(conversation, userId) is null)
            throw new NotFoundAppException("Conversation not found.");
        return conversation;
    }

    private static ConversationMember? ActiveMember(Conversation conversation, Guid userId)
        => conversation.Members.FirstOrDefault(m => m.UserId == userId && m.IsActive);

    private static IEnumerable<Guid> MemberIds(Conversation conversation)
        => conversation.Members.Where(m => m.IsActive).Select(m => m.UserId);

    private static ConversationDto ToDto(Conversation conversation, IReadOnlyDictionary<Guid, User> userLookup)
    {
        var members = conversation.Members
            .Where(m => m.IsActive)
            .OrderBy(m => m.JoinedAt)
            .Select(m =>
            {
                userLookup.TryGetValue(m.UserId, out var u);
                return new MemberDto(m.UserId, u?.Username ?? "unknown", u?.DisplayName ?? "unknown", m.Role, m.JoinedAt);
            })
            .ToList();
        return new ConversationDto(conversation.Id, conversation.Type, conversation.Name,
            conversation.CreatedBy, conversation.CreatedAt, conversation.LastMessageId, members);
    }
}
