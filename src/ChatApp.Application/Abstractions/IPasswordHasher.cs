using ChatApp.Domain.Entities;

namespace ChatApp.Application.Abstractions;

public interface IPasswordHasher
{
    string Hash(User user, string password);
    bool Verify(User user, string passwordHash, string providedPassword);
}
