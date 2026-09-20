namespace ChatApp.Application.Abstractions;

public sealed record UserProfileDto(
    Guid Id,
    string Username,
    string DisplayName,
    string Email,
    string? AvatarUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastSeenAt,
    bool IsOnline);
