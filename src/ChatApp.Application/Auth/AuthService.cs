using ChatApp.Application.Abstractions;
using ChatApp.Application.Common.Exceptions;
using ChatApp.Domain.Entities;

namespace ChatApp.Application.Auth;

public sealed class AuthService(
    IUserRepository users,
    IRefreshTokenRepository refreshTokens,
    IPasswordHasher passwordHasher,
    IJwtTokenService jwt) : IAuthService
{
    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        ValidateRegister(request);

        var username = request.Username.Trim();
        var email = request.Email.Trim().ToLowerInvariant();

        if (await users.ExistsByUsernameAsync(username, ct))
            throw new ConflictAppException("Username is already taken.");
        if (await users.ExistsByEmailAsync(email, ct))
            throw new ConflictAppException("Email is already registered.");

        var user = new User(username, request.DisplayName.Trim(), email, "tmp");
        user.SetPasswordHash(passwordHasher.Hash(user, request.Password));
        await users.AddAsync(user, ct);
        await users.SaveChangesAsync(ct);

        return await IssueTokensAsync(user, ct);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.UsernameOrEmail) || string.IsNullOrWhiteSpace(request.Password))
            throw new UnauthorizedAppException("Invalid credentials.");

        var key = request.UsernameOrEmail.Trim();
        User? user = key.Contains('@')
            ? await users.GetByEmailAsync(key.ToLowerInvariant(), ct)
            : await users.GetByUsernameAsync(key, ct);

        if (user is null || !passwordHasher.Verify(user, user.PasswordHash, request.Password))
            throw new UnauthorizedAppException("Invalid credentials.");

        return await IssueTokensAsync(user, ct);
    }

    public async Task<AuthResponse> RefreshAsync(RefreshRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            throw new UnauthorizedAppException("Invalid refresh token.");

        var stored = await refreshTokens.GetByTokenAsync(request.RefreshToken, ct);
        if (stored is null || !stored.IsActive)
            throw new UnauthorizedAppException("Invalid refresh token.");

        var user = await users.GetByIdAsync(stored.UserId, ct)
            ?? throw new UnauthorizedAppException("Invalid refresh token.");

        // Rotate: revoke old, issue new (prevents replay).
        stored.Revoke();
        await refreshTokens.SaveChangesAsync(ct);

        return await IssueTokensAsync(user, ct);
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            return;
        var stored = await refreshTokens.GetByTokenAsync(refreshToken, ct);
        if (stored is null || !stored.IsActive)
            return;
        stored.Revoke();
        await refreshTokens.SaveChangesAsync(ct);
    }

    private async Task<AuthResponse> IssueTokensAsync(User user, CancellationToken ct)
    {
        var accessToken = jwt.GenerateAccessToken(user);
        var refreshTokenValue = jwt.GenerateRefreshToken();
        var refresh = new RefreshToken(user.Id, refreshTokenValue, DateTimeOffset.UtcNow.Add(jwt.RefreshTokenLifetime));
        await refreshTokens.AddAsync(refresh, ct);
        await refreshTokens.SaveChangesAsync(ct);

        // Access token lifetime is encoded inside the JWT itself; expose approx expiry for clients.
        var accessExpiresAt = jwt.AccessTokenExpiresAt;
        var dto = new UserDto(user.Id, user.Username, user.DisplayName, user.Email, user.AvatarUrl, user.CreatedAt);
        return new AuthResponse(dto, accessToken, refreshTokenValue, accessExpiresAt);
    }

    private static void ValidateRegister(RegisterRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || request.Username.Trim().Length is < 3 or > 50)
            throw new ValidationAppException("Username must be between 3 and 50 characters.");
        if (string.IsNullOrWhiteSpace(request.DisplayName) || request.DisplayName.Trim().Length > 100)
            throw new ValidationAppException("DisplayName must be between 1 and 100 characters.");
        if (string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains('@'))
            throw new ValidationAppException("A valid email is required.");
        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
            throw new ValidationAppException("Password must be at least 8 characters.");
    }
}
