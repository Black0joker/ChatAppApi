using ChatApp.Domain.Enums;

namespace ChatApp.Application.Abstractions;

public sealed record CreateDirectRequest(Guid OtherUserId);
public sealed record CreateGroupRequest(string Name, IReadOnlyList<Guid> MemberIds);
public sealed record AddMemberRequest(Guid UserId, MemberRole? Role = null);

public sealed record MemberDto(Guid UserId, string Username, string DisplayName, MemberRole Role, DateTimeOffset JoinedAt);

public sealed record ConversationDto(
    Guid Id,
    ConversationType Type,
    string? Name,
    Guid CreatedBy,
    DateTimeOffset CreatedAt,
    Guid? LastMessageId,
    IReadOnlyList<MemberDto> Members);
