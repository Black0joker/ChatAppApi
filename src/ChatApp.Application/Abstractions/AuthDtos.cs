namespace ChatApp.Application.Abstractions;

public sealed record RegisterRequest(string Username, string DisplayName, string Email, string Password);
public sealed record LoginRequest(string UsernameOrEmail, string Password);
public sealed record RefreshRequest(string RefreshToken);

public sealed record UserDto(Guid Id, string Username, string DisplayName, string Email, string? AvatarUrl, DateTimeOffset CreatedAt);
public sealed record AuthResponse(UserDto User, string AccessToken, string RefreshToken, DateTimeOffset AccessTokenExpiresAt);
