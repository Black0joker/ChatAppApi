namespace ChatApp.Domain.Entities;

public sealed class User
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Username { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public string? AvatarUrl { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastSeenAt { get; private set; }

    private User() { } // EF Core

    public User(string username, string displayName, string email, string passwordHash)
    {
        Id = Guid.NewGuid();
        SetUsername(username);
        SetDisplayName(displayName);
        SetEmail(email);
        PasswordHash = passwordHash;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public void SetUsername(string username)
    {
        if (string.IsNullOrWhiteSpace(username) || username.Length is < 3 or > 50)
            throw new ArgumentException("Username must be between 3 and 50 characters.", nameof(username));
        Username = username.Trim();
    }

    public void SetDisplayName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Length > 100)
            throw new ArgumentException("DisplayName must be between 1 and 100 characters.", nameof(displayName));
        DisplayName = displayName.Trim();
    }

    public void SetEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 256 || !email.Contains('@'))
            throw new ArgumentException("Invalid email address.", nameof(email));
        Email = email.Trim().ToLowerInvariant();
    }

    public void SetPasswordHash(string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new ArgumentException("Password hash must not be empty.", nameof(passwordHash));
        PasswordHash = passwordHash;
    }

    public void SetAvatar(string? avatarUrl) => AvatarUrl = avatarUrl;

    public void UpdateLastSeen(DateTimeOffset when) => LastSeenAt = when;
}
