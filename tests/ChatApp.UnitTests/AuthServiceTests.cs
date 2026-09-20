using ChatApp.Application.Abstractions;
using ChatApp.Application.Auth;
using ChatApp.Application.Common.Exceptions;
using ChatApp.Domain.Entities;

namespace ChatApp.UnitTests;

internal sealed class FakeUserRepository : IUserRepository
{
    private readonly List<User> _users = [];

    public int GetByIdsCallCount { get; internal set; }

    public Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(_users.FirstOrDefault(x => x.Id == id));

    public Task<IReadOnlyDictionary<Guid, User>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        GetByIdsCallCount++;
        var set = ids.ToHashSet();
        return Task.FromResult<IReadOnlyDictionary<Guid, User>>(
            _users.Where(x => set.Contains(x.Id)).ToDictionary(x => x.Id));
    }

    public Task<User?> GetByUsernameAsync(string username, CancellationToken ct = default)
        => Task.FromResult(_users.FirstOrDefault(x => x.Username == username));

    public Task<User?> GetByEmailAsync(string email, CancellationToken ct = default)
        => Task.FromResult(_users.FirstOrDefault(x => x.Email == email));

    public Task<bool> ExistsByUsernameAsync(string username, CancellationToken ct = default)
        => Task.FromResult(_users.Any(x => x.Username == username));

    public Task<bool> ExistsByEmailAsync(string email, CancellationToken ct = default)
        => Task.FromResult(_users.Any(x => x.Email == email));

    public Task AddAsync(User user, CancellationToken ct = default)
    {
        _users.Add(user);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}

internal sealed class FakeRefreshTokenRepository : IRefreshTokenRepository
{
    private readonly List<RefreshToken> _tokens = [];

    public Task<RefreshToken?> GetByTokenAsync(string token, CancellationToken ct = default)
        => Task.FromResult(_tokens.FirstOrDefault(x => x.Token == token));

    public Task<IReadOnlyList<RefreshToken>> GetActiveByUserIdAsync(Guid userId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<RefreshToken>>(_tokens.Where(x => x.UserId == userId && x.IsActive).ToList());

    public Task AddAsync(RefreshToken refreshToken, CancellationToken ct = default)
    {
        _tokens.Add(refreshToken);
        return Task.CompletedTask;
    }

    public Task RevokeAllForUserAsync(Guid userId, CancellationToken ct = default)
    {
        foreach (var t in _tokens.Where(x => x.UserId == userId && x.IsActive))
            t.Revoke();
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}

internal sealed class FakePasswordHasher : IPasswordHasher
{
    public string Hash(User user, string password) => "HASHED:" + password;
    public bool Verify(User user, string passwordHash, string providedPassword)
        => passwordHash == "HASHED:" + providedPassword;
}

internal sealed class FakeJwt : IJwtTokenService
{
    private int _counter;
    public TimeSpan RefreshTokenLifetime => TimeSpan.FromDays(14);
    public DateTimeOffset AccessTokenExpiresAt => DateTimeOffset.UtcNow.AddMinutes(15);
    public string GenerateAccessToken(User user) => "ACCESS:" + user.Id;
    public string GenerateRefreshToken() => "REFRESH:" + (++_counter);
}

public sealed class AuthServiceTests
{
    private static AuthService Create(out FakeRefreshTokenRepository refresh)
    {
        refresh = new FakeRefreshTokenRepository();
        return new AuthService(new FakeUserRepository(), refresh, new FakePasswordHasher(), new FakeJwt());
    }

    [Fact]
    public async Task Register_creates_user_and_returns_tokens()
    {
        var svc = Create(out _);
        var res = await svc.RegisterAsync(new RegisterRequest("alice", "Alice", "alice@example.com", "Secret123!"));
        Assert.Equal("alice", res.User.Username);
        Assert.StartsWith("ACCESS:", res.AccessToken);
        Assert.StartsWith("REFRESH:", res.RefreshToken);
    }

    [Fact]
    public async Task Register_duplicate_username_throws_conflict()
    {
        var svc = Create(out _);
        await svc.RegisterAsync(new RegisterRequest("alice", "Alice", "alice@example.com", "Secret123!"));
        await Assert.ThrowsAsync<ConflictAppException>(() =>
            svc.RegisterAsync(new RegisterRequest("alice", "Alice2", "alice2@example.com", "Secret123!")));
    }

    [Fact]
    public async Task Login_wrong_password_throws_unauthorized()
    {
        var svc = Create(out _);
        await svc.RegisterAsync(new RegisterRequest("alice", "Alice", "alice@example.com", "Secret123!"));
        await Assert.ThrowsAsync<UnauthorizedAppException>(() =>
            svc.LoginAsync(new LoginRequest("alice", "WrongPass1!")));
    }

    [Fact]
    public async Task Refresh_rotates_and_rejects_replay()
    {
        var svc = Create(out _);
        var reg = await svc.RegisterAsync(new RegisterRequest("alice", "Alice", "alice@example.com", "Secret123!"));
        var refreshed = await svc.RefreshAsync(new RefreshRequest(reg.RefreshToken));
        Assert.NotEqual(reg.RefreshToken, refreshed.RefreshToken);
        await Assert.ThrowsAsync<UnauthorizedAppException>(() =>
            svc.RefreshAsync(new RefreshRequest(reg.RefreshToken)));
    }

    [Fact]
    public async Task Logout_revokes_refresh_token()
    {
        var svc = Create(out _);
        var reg = await svc.RegisterAsync(new RegisterRequest("alice", "Alice", "alice@example.com", "Secret123!"));
        await svc.LogoutAsync(reg.RefreshToken);
        await Assert.ThrowsAsync<UnauthorizedAppException>(() =>
            svc.RefreshAsync(new RefreshRequest(reg.RefreshToken)));
    }
}
