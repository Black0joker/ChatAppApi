using ChatApp.Domain.Entities;

namespace ChatApp.Application.Abstractions;

public interface IJwtTokenService
{
    string GenerateAccessToken(User user);
    string GenerateRefreshToken();
    TimeSpan RefreshTokenLifetime { get; }
    DateTimeOffset AccessTokenExpiresAt { get; }
}
