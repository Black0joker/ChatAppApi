using ChatApp.Application.Abstractions;
using ChatApp.Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace ChatApp.Infrastructure.Services;

public sealed class PasswordHasher : IPasswordHasher
{
    private readonly PasswordHasher<User> _inner = new();

    public string Hash(User user, string password)
        => _inner.HashPassword(user, password);

    public bool Verify(User user, string passwordHash, string providedPassword)
        => _inner.VerifyHashedPassword(user, passwordHash, providedPassword) != PasswordVerificationResult.Failed;
}
